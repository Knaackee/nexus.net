using Nexus.Core.Agents;

namespace Nexus.Orchestration;

public interface IAgentPool
{
    Task<IAgent> SpawnAsync(AgentDefinition definition, CancellationToken ct = default);
    Task PauseAsync(AgentId id, CancellationToken ct = default);
    Task ResumeAsync(AgentId id, CancellationToken ct = default);
    Task KillAsync(AgentId id, CancellationToken ct = default);
    IReadOnlyList<IAgent> ActiveAgents { get; }
    IObservable<AgentLifecycleEvent> Lifecycle { get; }
    Task DrainAsync(TimeSpan timeout, CancellationToken ct = default);
    Task CheckpointAndStopAllAsync(ICheckpointStore store, CancellationToken ct = default);
}
