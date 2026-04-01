using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;
using Nexus.Orchestration;
using Nexus.Workflows.Dsl;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => new SequentialChatClient("Research summary", "Approved implementation plan", "Execution complete"));
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddAgentLoop(loop => loop.UseDefaults());
});

services.AddSingleton<IApprovalGate, ScriptedApprovalGate>();

await using var serviceProvider = services.BuildServiceProvider();
var loop = serviceProvider.GetRequiredService<IAgentLoop>();

var workflow = new WorkflowDefinition
{
    Id = "approved-change",
    Name = "Approved Change Workflow",
    Nodes =
    [
        new NodeDefinition { Id = "research", Name = "Research", Description = "Research the request: {input}" },
        new NodeDefinition { Id = "plan", Name = "Plan", Description = "Create a plan from: {previous}", RequiresApproval = true },
        new NodeDefinition { Id = "execute", Name = "Execute", Description = "Execute the approved plan: {previous}" },
    ],
    Edges =
    [
        new EdgeDefinition { From = "research", To = "plan" },
        new EdgeDefinition { From = "plan", To = "execute" },
    ],
};

await foreach (var evt in loop.RunAsync(new AgentLoopOptions
{
    RoutingStrategy = new WorkflowRoutingStrategy(workflow),
    UserInput = "Prepare the production deployment checklist.",
}))
{
    if (evt is ApprovalRequestedLoopEvent approval)
        Console.WriteLine($"Approval requested: {approval.Description}");

    if (evt is LoopCompletedEvent completed)
        Console.WriteLine($"Workflow finished: {completed.Reason} / {completed.FinalResult.Text}");
}

sealed class ScriptedApprovalGate : IApprovalGate
{
    public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var modified = JsonSerializer.SerializeToElement(new
        {
            Output = "Approved implementation plan with rollback checklist"
        });
        return Task.FromResult(new ApprovalResult(true, "recipe-demo", ModifiedContext: modified));
    }
}

sealed class SequentialChatClient : IChatClient
{
    private readonly Queue<string> _responses;

    public SequentialChatClient(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = _responses.Count > 0 ? _responses.Dequeue() : "No configured response.";
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