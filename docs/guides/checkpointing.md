# Checkpointing

Save and restore orchestration state for long-running workflows, crash recovery, and auditing.

## ICheckpointStore

```csharp
public interface ICheckpointStore
{
    Task<CheckpointId> SaveAsync(OrchestrationSnapshot snapshot, CancellationToken ct = default);
    Task<OrchestrationSnapshot?> LoadAsync(CheckpointId id, CancellationToken ct = default);
    Task<OrchestrationSnapshot?> LoadLatestAsync(TaskGraphId graphId, CancellationToken ct = default);
    Task<IReadOnlyList<CheckpointId>> ListAsync(TaskGraphId graphId, CancellationToken ct = default);
    Task DeleteAsync(CheckpointId id, CancellationToken ct = default);
}
```

## OrchestrationSnapshot

A snapshot captures the complete state of a graph execution:

```csharp
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
```

## Saving Checkpoints

Save a checkpoint during or after graph execution:

```csharp
var store = sp.GetRequiredService<ICheckpointStore>();

var snapshot = new OrchestrationSnapshot
{
    Id = CheckpointId.New(),
    GraphId = graph.Id,
    CreatedAt = DateTimeOffset.UtcNow,
    NodeStates = new Dictionary<TaskId, TaskNodeState>
    {
        [task1.TaskId] = TaskNodeState.Completed,
        [task2.TaskId] = TaskNodeState.Running,
        [task3.TaskId] = TaskNodeState.NotStarted,
    },
    CompletedResults = new Dictionary<TaskId, AgentResult>
    {
        [task1.TaskId] = AgentResult.Success("Task 1 output"),
    },
};

var checkpointId = await store.SaveAsync(snapshot);
```

## Resuming from Checkpoint

Resume a failed or interrupted orchestration:

```csharp
// Load the latest checkpoint for a graph
var snapshot = await store.LoadLatestAsync(graph.Id);

if (snapshot is not null)
{
    // Resume execution — completed nodes are skipped
    var result = await orchestrator.ResumeFromCheckpointAsync(snapshot, graph);
}
```

## Listing and Managing Checkpoints

```csharp
// List all checkpoints for a graph
var checkpoints = await store.ListAsync(graph.Id);

// Load a specific checkpoint
var specific = await store.LoadAsync(checkpoints[0]);

// Delete old checkpoints
await store.DeleteAsync(checkpoints[0]);
```

## Agent Pool Checkpointing

Checkpoint all active agents before shutdown:

```csharp
var pool = sp.GetRequiredService<IAgentPool>();
var store = sp.GetRequiredService<ICheckpointStore>();

// Gracefully checkpoint and stop all running agents
await pool.CheckpointAndStopAllAsync(store);
```

## Configuration

```csharp
services.AddNexus(nexus =>
{
    nexus.AddCheckpointing(c =>
    {
        // Built-in in-memory store (default)
        // Implement ICheckpointStore for persistent storage
    });
});
```

## Custom Store

Implement `ICheckpointStore` for persistent backends:

```csharp
public class SqlCheckpointStore : ICheckpointStore
{
    public async Task<CheckpointId> SaveAsync(OrchestrationSnapshot snapshot, CancellationToken ct)
    {
        // Serialize snapshot to database
    }

    public async Task<OrchestrationSnapshot?> LoadAsync(CheckpointId id, CancellationToken ct)
    {
        // Load from database
    }

    // ... other methods
}
```

The default in-memory implementation serializes snapshots as JSON. The `Nexus.Orchestration.Checkpointing` package provides the JSON serialization infrastructure.
