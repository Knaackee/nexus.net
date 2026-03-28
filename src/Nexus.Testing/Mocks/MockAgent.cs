using System.Runtime.CompilerServices;
using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Testing.Mocks;

/// <summary>
/// A mock agent with pre-configured responses for testing orchestration scenarios.
/// </summary>
public sealed class MockAgent : IAgent
{
    private readonly Func<AgentTask, AgentResult> _handler;
    public AgentId Id { get; }
    public string Name { get; }
    public AgentState State { get; private set; } = AgentState.Idle;
    public List<AgentTask> ReceivedTasks { get; } = [];

    private MockAgent(AgentId id, string name, Func<AgentTask, AgentResult> handler)
    {
        Id = id;
        Name = name;
        _handler = handler;
    }

    public static MockAgent AlwaysReturns(string output, string? name = null)
    {
        var agentName = name ?? $"mock-{Guid.NewGuid():N}";
        var id = AgentId.New();
        return new MockAgent(id, agentName, _ => AgentResult.Success(output));
    }

    public static MockAgent AlwaysFails(string error, string? name = null)
    {
        var agentName = name ?? $"mock-fail-{Guid.NewGuid():N}";
        var id = AgentId.New();
        return new MockAgent(id, agentName, _ => AgentResult.Failed(error));
    }

    public static MockAgent WithHandler(Func<AgentTask, AgentResult> handler, string? name = null)
    {
        var agentName = name ?? $"mock-{Guid.NewGuid():N}";
        var id = AgentId.New();
        return new MockAgent(id, agentName, handler);
    }

    public static MockAgent WithResponses(params (string input, string output)[] responses)
    {
        var lookup = responses.ToDictionary(r => r.input, r => r.output, StringComparer.OrdinalIgnoreCase);
        var id = AgentId.New();
        return new MockAgent(id, "mock-responses", task =>
        {
            if (lookup.TryGetValue(task.Description, out var output))
                return AgentResult.Success(output);
            return AgentResult.Failed($"No response for: {task.Description}");
        });
    }

    public Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
    {
        ReceivedTasks.Add(task);
        State = AgentState.Running;
        var result = _handler(task);
        State = AgentState.Idle;
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(
        AgentTask task, IAgentContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ReceivedTasks.Add(task);
        State = AgentState.Running;
        var result = _handler(task);
        State = AgentState.Idle;

        if (result.Status == AgentResultStatus.Success && result.Text is not null)
        {
            yield return new TextChunkEvent(Id, result.Text);
        }

        yield return new AgentCompletedEvent(Id, result);
        await Task.CompletedTask;
    }
}
