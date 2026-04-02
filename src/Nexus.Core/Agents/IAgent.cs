using Nexus.Core.Events;

namespace Nexus.Core.Agents;

/// <summary>
/// Represents an executable agent that can process tasks and produce results.
/// </summary>
public interface IAgent
{
    /// <summary>Unique identifier for this agent instance.</summary>
    AgentId Id { get; }
    /// <summary>Human-readable name of the agent.</summary>
    string Name { get; }
    /// <summary>Current lifecycle state of the agent.</summary>
    AgentState State { get; }

    /// <summary>Executes a task and returns the final result.</summary>
    Task<AgentResult> ExecuteAsync(
        AgentTask task, IAgentContext context, CancellationToken ct = default);

    /// <summary>Executes a task and streams events as they occur.</summary>
    IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(
        AgentTask task, IAgentContext context,
        CancellationToken ct = default);
}
