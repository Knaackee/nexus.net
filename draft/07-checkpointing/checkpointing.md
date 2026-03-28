# Checkpointing — Nexus.Orchestration.Checkpointing

> Assembly: `Nexus.Orchestration.Checkpointing`  
> Deps: `Nexus.Orchestration`

## 1. Warum Checkpointing

Ohne Checkpointing verliert ein 10-Minuten-Agent bei Fehler in Minute 9 alles. In Production inakzeptabel.

## 2. ICheckpointStore

```csharp
public interface ICheckpointStore
{
    Task<CheckpointId> SaveAsync(OrchestrationSnapshot snapshot, CancellationToken ct);
    Task<OrchestrationSnapshot?> LoadAsync(CheckpointId id, CancellationToken ct);
    Task<OrchestrationSnapshot?> LoadLatestAsync(string threadId, CancellationToken ct);
    Task<IReadOnlyList<CheckpointInfo>> ListAsync(string threadId, CancellationToken ct);
    Task DeleteAsync(CheckpointId id, CancellationToken ct);
}
```

## 3. OrchestrationSnapshot

```csharp
public record OrchestrationSnapshot
{
    public required CheckpointId Id { get; init; }
    public required string ThreadId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required IReadOnlyDictionary<AgentId, AgentSnapshot> AgentStates { get; init; }
    public required TaskGraphSnapshot GraphState { get; init; }
    public required JsonElement SharedState { get; init; }
    public required IReadOnlyDictionary<AgentId, IReadOnlyList<ChatMessage>> Histories { get; init; }
    public required CostSummary AccumulatedCost { get; init; }
}

public record AgentSnapshot(AgentId Id, AgentState State, JsonElement? CustomState);
public record TaskGraphSnapshot(IReadOnlySet<TaskId> Completed, IReadOnlySet<TaskId> Failed, IReadOnlySet<TaskId> Pending);
public record CheckpointInfo(CheckpointId Id, DateTimeOffset Timestamp, int CompletedNodes, int TotalNodes);
```

## 4. Strategien

```csharp
public enum CheckpointStrategy
{
    None,           // Kein Checkpointing
    AfterEachNode,  // Nach jedem fertigen Node (Default)
    OnError,        // Nur bei Fehlern
    Manual          // Consumer ruft explizit CheckpointAsync
}
```

## 5. Nutzung

```csharp
// Automatisch:
var result = await orchestrator.ExecuteGraphAsync(graph, new OrchestrationOptions
{
    ThreadId = "session-123",
    CheckpointStore = checkpointStore,
    CheckpointStrategy = CheckpointStrategy.AfterEachNode,
});

// Resume nach Crash:
var snapshot = await checkpointStore.LoadLatestAsync("session-123");
if (snapshot != null)
    result = await orchestrator.ResumeFromCheckpointAsync(snapshot, graph);
```

## 6. ISnapshotSerializer

```csharp
public interface ISnapshotSerializer
{
    byte[] Serialize(OrchestrationSnapshot snapshot);
    OrchestrationSnapshot Deserialize(byte[] data);
}
// Default: System.Text.Json
```

## 7. Backends

| Backend | Paket | Use Case |
|---------|-------|----------|
| InMemory | `Nexus.Orchestration.Checkpointing` | Dev/Test |
| SQLite | `Nexus.Orchestration.Checkpointing` | Single-Node Production |
| Redis | `Nexus.Checkpointing.Redis` | Distributed Production |
| Postgres | `Nexus.Checkpointing.Postgres` | Enterprise |
