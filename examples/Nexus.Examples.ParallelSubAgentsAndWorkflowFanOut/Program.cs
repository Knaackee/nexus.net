using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Tools.Standard;
using Nexus.Workflows.Dsl;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => new RoleAwareChatClient());
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddStandardTools(tools => tools.Only(StandardToolCategory.Agents));
});

services.AddWorkflowDsl();

await using var serviceProvider = services.BuildServiceProvider();
var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
var agentTool = toolRegistry.Resolve("agent")!;

var toolResult = await agentTool.ExecuteAsync(JsonDocument.Parse("""
{
  "maxConcurrency": 3,
  "tasks": [
    { "agent": "Researcher", "task": "Collect the strongest supporting evidence" },
    { "agent": "RiskAnalyst", "task": "List failure modes and missing controls" },
    { "agent": "Reviewer", "task": "Identify weak assumptions and unclear claims" }
  ]
}
""").RootElement.Clone(), new RecipeToolContext(toolRegistry), CancellationToken.None);

var batch = (AgentBatchToolResult)toolResult.Value!;
Console.WriteLine($"Sub-agents: {batch.CompletedCount} completed / {batch.FailedCount} failed");

var workflow = new WorkflowDefinition
{
    Id = "fanout-merge",
    Name = "Fan-Out Merge",
    Nodes =
    [
        new NodeDefinition { Id = "merge", Name = "Merge", Description = "Merge the findings into one brief." },
        new NodeDefinition { Id = "publish", Name = "Publish", Description = "Publish the approved brief." },
    ],
    Edges =
    [
        new EdgeDefinition { From = "merge", To = "publish", Condition = "result.text.contains('approved')" },
    ],
    Options = new WorkflowOptions { MaxConcurrentNodes = 4, GlobalTimeoutSeconds = 300 }
};

var executor = serviceProvider.GetRequiredService<IWorkflowExecutor>();
var orchestrationResult = await executor.ExecuteAsync(workflow);
Console.WriteLine($"Workflow: {orchestrationResult.Status}");

sealed class RecipeToolContext : IToolContext
{
    public RecipeToolContext(IToolRegistry tools)
    {
        Tools = tools;
    }

    public IToolRegistry Tools { get; }
    public Nexus.Core.Contracts.ISecretProvider? Secrets => null;
    public Nexus.Core.Contracts.IBudgetTracker? Budget => null;
    public Nexus.Core.Contracts.CorrelationContext Correlation { get; } = new() { TraceId = "recipe", SpanId = "fanout" };
    public AgentId AgentId => AgentId.New();
}

sealed class RoleAwareChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var system = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
        var user = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        var text = system switch
        {
            var value when value.Contains("Researcher", StringComparison.OrdinalIgnoreCase) => $"Research findings approved for: {user}",
            var value when value.Contains("RiskAnalyst", StringComparison.OrdinalIgnoreCase) => $"Risk findings approved for: {user}",
            var value when value.Contains("Reviewer", StringComparison.OrdinalIgnoreCase) => $"Review findings approved for: {user}",
            var value when value.Contains("Merge", StringComparison.OrdinalIgnoreCase) => "approved merged brief",
            _ => "approved publication",
        };

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = response.Messages[^1].Contents,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}