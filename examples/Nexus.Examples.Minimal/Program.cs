// ============================================================
// Nexus.Examples.Minimal — Single Agent with Tools & Guardrails
// ============================================================
// This example demonstrates the fundamental Nexus building blocks:
//   1. Configuring the DI container with Nexus services
//   2. Registering inline tools (LambdaTool)
//   3. Running a single ChatAgent with a buffered execution
//   4. Input guardrails that reject prompt injection
//
// To connect a real LLM, replace EchoChatClient with:
//   new OpenAIClient(apiKey).GetChatClient("gpt-4o").AsIChatClient()

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.Guardrails;
using Nexus.Guardrails.BuiltIn;
using Nexus.Memory;
using Nexus.Orchestration;

// ── 1. Build the service container ──────────────────────────
var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    // Register a chat client (echo stub for demo — swap for a real LLM)
    nexus.UseChatClient(_ => new EchoChatClient());

    // Orchestration gives us IAgentPool and IOrchestrator
    nexus.AddOrchestration(o => o.UseDefaults());

    // Memory stores conversation history in-process
    nexus.AddMemory(m => m.UseInMemory());
});

var sp = services.BuildServiceProvider();

// Register inline tools via IToolRegistry
var toolRegistry = sp.GetRequiredService<IToolRegistry>();
toolRegistry.Register(new LambdaTool(
    "get_time",
    "Returns the current UTC time",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O")))));

toolRegistry.Register(new LambdaTool(
    "add",
    "Adds two numbers",
    (input, _, _) =>
    {
        var a = input.GetProperty("a").GetDouble();
        var b = input.GetProperty("b").GetDouble();
        return Task.FromResult(ToolResult.Success((a + b).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }));

// ── 2. Input guardrails ─────────────────────────────────────
var guardrailPipeline = new DefaultGuardrailPipeline([
    new PromptInjectionDetector(),
    new InputLengthLimiter { MaxTokens = 5000 },
]);

var userInput = "What time is it right now?";
Console.WriteLine($"User: {userInput}");

var guardrailResult = await guardrailPipeline.EvaluateInputAsync(userInput);
if (!guardrailResult.IsAllowed)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Blocked: {guardrailResult.Reason}");
    Console.ResetColor();
    return;
}

// ── 3. Run an agent ─────────────────────────────────────────
Console.WriteLine($"\nRegistered tools: {string.Join(", ", toolRegistry.ListAll().Select(t => t.Name))}");

// Spawn an agent via the pool
var pool = sp.GetRequiredService<IAgentPool>();
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "You are a helpful assistant. Use tools when needed.",
    ToolNames = ["get_time", "add"],
    Budget = new AgentBudget { MaxIterations = 5 },
});

Console.WriteLine($"Agent '{agent.Name}' created (Id: {agent.Id}, State: {agent.State})");

// Create and execute a task
var task = AgentTask.Create(userInput);
Console.WriteLine($"Task {task.Id}: {task.Description}");

var orchestrator = sp.GetRequiredService<IOrchestrator>();
var result = await orchestrator.ExecuteSequenceAsync([task with { AssignedAgent = agent.Id }]);

Console.WriteLine($"\nOrchestration: {result.Status} ({result.Duration.TotalMilliseconds:F0}ms)");
foreach (var (taskId, taskResult) in result.TaskResults)
{
    var status = taskResult.Status == AgentResultStatus.Success ? "Success" : "Failed";
    Console.WriteLine($"  [{status}] {taskResult.Text}");
}

// ── 4. Demonstrate PII output redaction ─────────────────────
var outputPipeline = new DefaultGuardrailPipeline([
    new PiiRedactor(GuardrailPhase.Output),
    new SecretsDetector(),
]);

var sensitiveOutput = "Contact john@example.com or call 555-123-4567. API key: sk-abcdef1234567890abcdef";
Console.WriteLine($"\nRaw output: {sensitiveOutput}");

var outputResult = await outputPipeline.EvaluateOutputAsync(sensitiveOutput);
if (outputResult.SanitizedContent is not null)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"Sanitized:  {outputResult.SanitizedContent}");
    Console.ResetColor();
}

Console.WriteLine("\nDone.");

// ════════════════════════════════════════════════════════════
// Minimal stubs for self-contained demo
// ════════════════════════════════════════════════════════════

/// <summary>Echo chat client that returns the user's message. Replace with a real LLM provider.</summary>
sealed class EchoChatClient : IChatClient
{
    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var reply = new ChatMessage(ChatRole.Assistant, $"[Echo] {last?.Text ?? "..."}");
        return Task.FromResult(new ChatResponse([reply]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await GetResponseAsync(messages, options, ct);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(response.Text ?? "...")],
        };
    }
}
