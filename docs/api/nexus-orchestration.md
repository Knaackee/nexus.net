# Nexus.Orchestration API Reference

## Namespace: Nexus.Orchestration

### IOrchestrator

```csharp
public interface IOrchestrator
{
    Task<OrchestrationResult> ExecuteGraphAsync(ITaskGraph graph, CancellationToken ct = default);
    Task<OrchestrationResult> ExecuteGraphAsync(ITaskGraph graph, OrchestrationOptions options, CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(ITaskGraph graph, CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(ITaskGraph graph, OrchestrationOptions options, CancellationToken ct = default);

    Task<OrchestrationResult> ExecuteSequenceAsync(IEnumerable<AgentTask> tasks, CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteSequenceStreamingAsync(IEnumerable<AgentTask> tasks, CancellationToken ct = default);

    Task<OrchestrationResult> ExecuteParallelAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteParallelStreamingAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default);

    Task<OrchestrationResult> ExecuteHierarchicalAsync(
        AgentTask rootTask, HierarchyOptions options, CancellationToken ct = default);

    Task<OrchestrationResult> ResumeFromCheckpointAsync(
        OrchestrationSnapshot snapshot, ITaskGraph graph, CancellationToken ct = default);

    ITaskGraph CreateGraph();
    IObservable<OrchestrationEvent> Events { get; }
}
```

### IAgentPool

```csharp
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
```

### ITaskGraph

```csharp
public interface ITaskGraph
{
    TaskGraphId Id { get; }
    ITaskNode AddTask(AgentTask task);
    void AddDependency(ITaskNode source, ITaskNode target);
    void AddDependency(ITaskNode source, ITaskNode target, EdgeOptions? options);
    void AddConditionalEdge(ITaskNode source, ITaskNode target, Func<AgentResult, bool> condition);
    ValidationResult Validate();
    IReadOnlyList<ITaskNode> Nodes { get; }
}
```

### ITaskNode

```csharp
public interface ITaskNode
{
    TaskId TaskId { get; }
    AgentTask Task { get; }
    IReadOnlyList<ITaskNode> Dependencies { get; }
    IReadOnlyList<ITaskNode> Dependants { get; }
}
```

### EdgeOptions

```csharp
public record EdgeOptions
{
    public IContextPropagator? ContextPropagator { get; init; }
    public TimeSpan? Timeout { get; init; }
}
```

### ValidationResult

```csharp
public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success { get; }
    public static ValidationResult Fail(params string[] errors);
}
```

### ICheckpointStore

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

### OrchestrationSnapshot

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
```

### TaskNodeState

```csharp
public enum TaskNodeState { NotStarted, Running, Completed, Failed, Skipped }
```
