using Nexus.Core.Events;

namespace Nexus.Core.Agents;

public interface IAgent
{
    AgentId Id { get; }
    string Name { get; }
    AgentState State { get; }

    Task<AgentResult> ExecuteAsync(
        AgentTask task, IAgentContext context, CancellationToken ct = default);

    IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(
        AgentTask task, IAgentContext context,
        CancellationToken ct = default);
}
