using Nexus.Core.Agents;

namespace Nexus.Orchestration;

public interface ICheckpointStore
{
    Task<CheckpointId> SaveAsync(OrchestrationSnapshot snapshot, CancellationToken ct = default);
    Task<OrchestrationSnapshot?> LoadAsync(CheckpointId id, CancellationToken ct = default);
    Task<OrchestrationSnapshot?> LoadLatestAsync(TaskGraphId graphId, CancellationToken ct = default);
    Task<IReadOnlyList<CheckpointId>> ListAsync(TaskGraphId graphId, CancellationToken ct = default);
    Task DeleteAsync(CheckpointId id, CancellationToken ct = default);
}

public record OrchestrationSnapshot
{
    public required CheckpointId Id { get; init; }
    public required TaskGraphId GraphId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyDictionary<TaskId, TaskNodeState> NodeStates { get; init; }
    public required IReadOnlyDictionary<TaskId, AgentResult> CompletedResults { get; init; }
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

public enum TaskNodeState { NotStarted, Running, Completed, Failed, Skipped }
