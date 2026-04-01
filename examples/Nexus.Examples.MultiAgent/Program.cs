// ============================================================
// Nexus.Examples.MultiAgent — Graph Orchestration & Workflows
// ============================================================
// This example demonstrates advanced Nexus capabilities:
//   1. Multi-agent task graph with dependencies
//   2. Streaming orchestration events
//   3. Checkpointing for fault tolerance
//   4. Workflow DSL (JSON-defined pipelines)
//   5. Message bus for inter-agent communication
//
// To connect real LLMs, replace EchoChatClient with actual providers.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.CostTracking;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Tools;
using Nexus.Memory;
using Nexus.Messaging;
using Nexus.Orchestration;
using Nexus.Orchestration.Checkpointing;
using Nexus.Workflows.Dsl;

// ── 1. Configure full Nexus stack ───────────────────────────
var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => new EchoChatClient());
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddCostTracking(c => c.AddModel("multi-agent-demo", input: 0.20m, output: 0.80m));
    nexus.AddMemory(m => m.UseInMemory());
    nexus.AddMessaging(m => m.UseInMemory());
    nexus.AddCheckpointing(c => c.UseInMemory());
    nexus.AddGuardrails();
});

services.AddWorkflowDsl();

var sp = services.BuildServiceProvider();

// Register tools via IToolRegistry after building the service provider
var toolRegistry = sp.GetRequiredService<IToolRegistry>();
toolRegistry.Register(new LambdaTool("web_search", "Searches the web",
    (input, _, _) =>
    {
        var query = input.GetProperty("query").GetString();
        return Task.FromResult(ToolResult.Success($"[Search results for: {query}]"));
    }));

// ── 2. Multi-Agent Graph Orchestration ──────────────────────
Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine("  Multi-Agent Graph Orchestration");
Console.WriteLine("═══════════════════════════════════════════════\n");

var pool = sp.GetRequiredService<IAgentPool>();
var orchestrator = sp.GetRequiredService<IOrchestrator>();

// Spawn specialized agents
var researcher = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Researcher",
    SystemPrompt = "You are a thorough researcher. Find comprehensive information.",
    ToolNames = ["web_search"],
    Budget = new AgentBudget { MaxCostUsd = 1.00m },
});

var writer = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Writer",
    SystemPrompt = "You are a professional content writer. Create clear, engaging content.",
    Budget = new AgentBudget { MaxCostUsd = 0.50m },
});

var reviewer = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Reviewer",
    SystemPrompt = "You are a critical reviewer. Check for accuracy and quality.",
});

Console.WriteLine($"Spawned agents: {string.Join(", ", pool.ActiveAgents.Select(a => $"{a.Name} ({a.Id})"))}");

// Build a task graph: Research → Write → Review
var graph = orchestrator.CreateGraph();
var researchNode = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Research AI trends for 2026",
    AssignedAgent = researcher.Id,
});
var writeNode = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Write a blog post about the research findings",
    AssignedAgent = writer.Id,
});
var reviewNode = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Review the blog post for accuracy and quality",
    AssignedAgent = reviewer.Id,
});

graph.AddDependency(researchNode, writeNode);
graph.AddDependency(writeNode, reviewNode);

// Validate the graph
var validation = graph.Validate();
Console.WriteLine($"Graph valid: {validation.IsValid} ({graph.Nodes.Count} nodes)\n");

// Execute with streaming events
Console.WriteLine("Executing graph (streaming)...\n");
await foreach (var evt in orchestrator.ExecuteGraphStreamingAsync(graph))
{
    switch (evt)
    {
        case NodeStartedEvent started:
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  ▶ Node {started.NodeId} started (Agent: {started.AgentId})");
            Console.ResetColor();
            break;

        case AgentEventInGraph { InnerEvent: TextChunkEvent textChunk }:
            Console.Write(textChunk.Text);
            break;

        case NodeCompletedEvent completed:
            Console.ForegroundColor = ConsoleColor.Green;
            var completedText = completed.Result.Text ?? "";
            Console.WriteLine($"\n  ✓ Node {completed.NodeId} completed: {completedText[..Math.Min(60, completedText.Length)]}...");
            if (completed.Result.TokenUsage is { } usage)
                Console.WriteLine($"    Tokens: {usage.TotalInputTokens} input, {usage.TotalOutputTokens} output, {usage.TotalTokens} total");
            if (completed.Result.EstimatedCost is decimal estimatedCost)
                Console.WriteLine($"    Estimated cost: ${estimatedCost:F6}");
            Console.ResetColor();
            break;

        case NodeFailedEvent failed:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ✗ Node {failed.NodeId} failed: {failed.Error.Message}");
            Console.ResetColor();
            break;

        case OrchestrationCompletedEvent done:
            Console.WriteLine($"\n  ═ Orchestration {done.Result.Status} in {done.Result.Duration.TotalMilliseconds:F0}ms");
            break;
    }
}

// ── 3. Checkpointing ───────────────────────────────────────
Console.WriteLine("\n═══════════════════════════════════════════════");
Console.WriteLine("  Checkpointing");
Console.WriteLine("═══════════════════════════════════════════════\n");

var checkpointStore = sp.GetRequiredService<ICheckpointStore>();
var serializer = new JsonSnapshotSerializer();

var snapshot = new OrchestrationSnapshot
{
    Id = CheckpointId.New(),
    GraphId = graph.Id,
    NodeStates = new Dictionary<TaskId, TaskNodeState>
    {
        [researchNode.TaskId] = TaskNodeState.Completed,
        [writeNode.TaskId] = TaskNodeState.Completed,
        [reviewNode.TaskId] = TaskNodeState.Running,
    },
    CompletedResults = new Dictionary<TaskId, AgentResult>
    {
        [researchNode.TaskId] = AgentResult.Success("Research complete."),
        [writeNode.TaskId] = AgentResult.Success("Blog post drafted."),
    },
    CreatedAt = DateTimeOffset.UtcNow,
};

await checkpointStore.SaveAsync(snapshot);
var bytes = serializer.Serialize(snapshot);
Console.WriteLine($"Snapshot serialized: {bytes.Length} bytes");

var restored = serializer.Deserialize(bytes);
Console.WriteLine($"Snapshot restored: {restored.NodeStates.Count} node states, {restored.CompletedResults.Count} results");

// ── 4. Message Bus ──────────────────────────────────────────
Console.WriteLine("\n═══════════════════════════════════════════════");
Console.WriteLine("  Inter-Agent Messaging");
Console.WriteLine("═══════════════════════════════════════════════\n");

var bus = sp.GetRequiredService<IMessageBus>();
var received = new List<object>();

using var sub = bus.Subscribe(researcher.Id, "research-updates", msg =>
{
    received.Add(msg.Payload);
    Console.WriteLine($"  Received from {msg.Sender} on research-updates: {msg.Payload}");
    return Task.CompletedTask;
});

await bus.PublishAsync("research-updates", new AgentMessage
{
    Id = MessageId.New(),
    Sender = writer.Id,
    Type = "status",
    Payload = "Blog post draft ready for review",
});

Console.WriteLine($"  Messages received: {received.Count}");

// ── 5. Workflow DSL ─────────────────────────────────────────
Console.WriteLine("\n═══════════════════════════════════════════════");
Console.WriteLine("  Workflow DSL (JSON)");
Console.WriteLine("═══════════════════════════════════════════════\n");

var workflowJson = """
{
    "id": "content-pipeline",
    "name": "Content Production Pipeline",
    "nodes": [
        {
            "id": "research",
            "name": "Researcher",
            "description": "Research the given topic thoroughly",
            "agent": {
                "tools": ["web_search"],
                "budget": { "maxCostUsd": 1.00 }
            }
        },
        {
            "id": "draft",
            "name": "Writer",
            "description": "Write engaging content based on research",
            "agent": {
                "budget": { "maxCostUsd": 0.50 }
            }
        },
        {
            "id": "review",
            "name": "Reviewer",
            "description": "Review content for accuracy and quality"
        }
    ],
    "edges": [
        { "from": "research", "to": "draft" },
        { "from": "draft", "to": "review" }
    ],
    "variables": {
        "topic": "AI Agent Orchestration Patterns"
    }
}
""";

var loader = sp.GetRequiredService<IWorkflowLoader>();
var workflow = loader.LoadFromString(workflowJson, "json");
Console.WriteLine($"Loaded workflow: {workflow.Name} ({workflow.Nodes.Count} nodes, {workflow.Edges.Count} edges)");

var validator = sp.GetRequiredService<IWorkflowValidator>();
var workflowValidation = validator.Validate(workflow);
Console.WriteLine($"Validation: {(workflowValidation.IsValid ? "PASSED" : "FAILED")}");

if (!workflowValidation.IsValid)
{
    foreach (var error in workflowValidation.Errors)
        Console.WriteLine($"  Error: {error}");
}

Console.WriteLine($"\nWorkflow variables: {string.Join(", ", workflow.Variables.Select(v => $"{v.Key}={v.Value}"))}");

// ── 6. Sequence Execution ───────────────────────────────────
Console.WriteLine("\n═══════════════════════════════════════════════");
Console.WriteLine("  Sequence Execution");
Console.WriteLine("═══════════════════════════════════════════════\n");

var sequenceTasks = new[]
{
    new AgentTask { Id = TaskId.New(), Description = "Step 1: Gather requirements", AssignedAgent = researcher.Id },
    new AgentTask { Id = TaskId.New(), Description = "Step 2: Design solution", AssignedAgent = writer.Id },
    new AgentTask { Id = TaskId.New(), Description = "Step 3: Review design", AssignedAgent = reviewer.Id },
};

var seqResult = await orchestrator.ExecuteSequenceAsync(sequenceTasks);
Console.WriteLine($"Sequence result: {seqResult.Status} ({seqResult.TaskResults.Count} tasks, {seqResult.Duration.TotalMilliseconds:F0}ms)");

foreach (var (taskId, taskResult) in seqResult.TaskResults)
{
    var status = taskResult.Status == AgentResultStatus.Success ? "✓" : taskResult.Status.ToString();
    var text = taskResult.Text ?? "";
    Console.WriteLine($"  {status} Task {taskId}: {text[..Math.Min(50, text.Length)]}");
    if (taskResult.EstimatedCost is decimal estimatedCost)
        Console.WriteLine($"    Cost: ${estimatedCost:F6}");
}

var tracker = sp.GetRequiredService<ICostTracker>();
var totals = await tracker.GetSnapshotAsync();
Console.WriteLine($"\nTracked usage across orchestration: {totals.TotalInputTokens} input, {totals.TotalOutputTokens} output, ${totals.TotalCost:F6} estimated");

Console.WriteLine("\nDone.");

// ════════════════════════════════════════════════════════════
// Minimal stubs for self-contained demo
// ════════════════════════════════════════════════════════════

/// <summary>Echo chat client that simulates LLM responses. Replace with a real provider.</summary>
sealed class EchoChatClient : IChatClient
{
    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var systemPrompt = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;
        var agentHint = systemPrompt?[..Math.Min(30, systemPrompt.Length)] ?? "Agent";

        var reply = new ChatMessage(ChatRole.Assistant,
            $"[{agentHint}] Response to: {last?.Text ?? "..."}");
        var response = new ChatResponse([reply]);
        UsageMetadataHelper.TrySetModelAndUsage(response, "multi-agent-demo", inputTokens: 80, outputTokens: 30);
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await GetResponseAsync(messages, options, ct);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(response.Text ?? "...")],
        };
        var usageUpdate = new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [] };
        UsageMetadataHelper.TrySetModelAndUsage(usageUpdate, "multi-agent-demo", inputTokens: 80, outputTokens: 30);
        yield return usageUpdate;
    }
}

static class UsageMetadataHelper
{
    public static void TrySetModelAndUsage(object target, string modelId, long inputTokens, long outputTokens)
    {
        TrySetProperty(target, "ModelId", modelId);

        var usageProperty = target.GetType().GetProperty("Usage");
        if (usageProperty?.CanWrite != true)
            return;

        var usage = Activator.CreateInstance(usageProperty.PropertyType);
        if (usage is null)
            return;

        TrySetProperty(usage, "InputTokenCount", inputTokens);
        TrySetProperty(usage, "OutputTokenCount", outputTokens);
        TrySetProperty(usage, "TotalTokenCount", inputTokens + outputTokens);
        TrySetProperty(usage, "PromptTokenCount", inputTokens);
        TrySetProperty(usage, "CompletionTokenCount", outputTokens);
        usageProperty.SetValue(target, usage);
    }

    private static void TrySetProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property?.CanWrite != true)
            return;

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        property.SetValue(target, Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture));
    }
}
