# Orchestration

The orchestration layer coordinates multi-agent execution with four modes: graph, sequence, parallel, and hierarchical.

## Use This Guide When

Use orchestration when the work structure is the main concern.

If the main concern is turn-by-turn chat behavior, approvals inside a loop, or session continuity, start with `Nexus.AgentLoop` instead.

## Quick Mode Chooser

| If you need... | Use |
|---|---|
| one task after another | `ExecuteSequenceAsync` |
| independent tasks at the same time | `ExecuteParallelAsync` |
| explicit dependencies and branches | `ExecuteGraphAsync` |
| manager-worker delegation | `ExecuteHierarchicalAsync` |

## IOrchestrator

```csharp
public interface IOrchestrator
{
    // Graph execution with DAG topology
    Task<OrchestrationResult> ExecuteGraphAsync(ITaskGraph graph, CancellationToken ct = default);
    Task<OrchestrationResult> ExecuteGraphAsync(ITaskGraph graph, OrchestrationOptions options, CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(ITaskGraph graph, CancellationToken ct = default);

    // Sequential execution
    Task<OrchestrationResult> ExecuteSequenceAsync(IEnumerable<AgentTask> tasks, CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteSequenceStreamingAsync(IEnumerable<AgentTask> tasks, CancellationToken ct = default);

    // Parallel execution with optional aggregation
    Task<OrchestrationResult> ExecuteParallelAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteParallelStreamingAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default);

    // Manager-worker hierarchy
    Task<OrchestrationResult> ExecuteHierarchicalAsync(
        AgentTask rootTask, HierarchyOptions options, CancellationToken ct = default);

    // Resume from checkpoint
    Task<OrchestrationResult> ResumeFromCheckpointAsync(
        OrchestrationSnapshot snapshot, ITaskGraph graph, CancellationToken ct = default);

    ITaskGraph CreateGraph();
    IObservable<OrchestrationEvent> Events { get; }
}
```

## Execution Modes

### Sequence

Tasks run one after another. Each task's output is available to the next:

```csharp
var result = await orchestrator.ExecuteSequenceAsync([
    new AgentTask { Id = TaskId.New(), Description = "Research AI trends", AssignedAgent = researcher.Id },
    new AgentTask { Id = TaskId.New(), Description = "Write blog post about findings", AssignedAgent = writer.Id },
    new AgentTask { Id = TaskId.New(), Description = "Review and edit the post", AssignedAgent = editor.Id },
]);
```

### Parallel

Tasks run concurrently. An optional aggregator combines results:

```csharp
var result = await orchestrator.ExecuteParallelAsync(
    [
        new AgentTask { Id = TaskId.New(), Description = "Analyze sales data", AssignedAgent = analyst1.Id },
        new AgentTask { Id = TaskId.New(), Description = "Analyze marketing data", AssignedAgent = analyst2.Id },
        new AgentTask { Id = TaskId.New(), Description = "Analyze customer feedback", AssignedAgent = analyst3.Id },
    ],
    results => AgentResult.Success(string.Join("\n---\n", results.Select(r => r.Text)))
);
```

### Graph (DAG)

Define a directed acyclic graph of tasks with dependencies:

```csharp
var graph = orchestrator.CreateGraph();

var research = graph.AddTask(new AgentTask
{
    Id = TaskId.New(), Description = "Research topic", AssignedAgent = researcher.Id
});

var outline = graph.AddTask(new AgentTask
{
    Id = TaskId.New(), Description = "Create outline", AssignedAgent = planner.Id
});

var write = graph.AddTask(new AgentTask
{
    Id = TaskId.New(), Description = "Write content", AssignedAgent = writer.Id
});

var review = graph.AddTask(new AgentTask
{
    Id = TaskId.New(), Description = "Review content", AssignedAgent = reviewer.Id
});

// research and outline run in parallel, both must complete before write
graph.AddDependency(research, write);
graph.AddDependency(outline, write);
graph.AddDependency(write, review);

// Validate before execution
var validation = graph.Validate();
if (!validation.IsValid)
    throw new InvalidOperationException(string.Join(", ", validation.Errors));

var result = await orchestrator.ExecuteGraphAsync(graph);
```

### Conditional Edges

Route execution based on results:

```csharp
graph.AddConditionalEdge(
    review, revision,
    result => result.Text?.Contains("needs revision", StringComparison.OrdinalIgnoreCase) == true
);

graph.AddConditionalEdge(
    review, publish,
    result => result.Status == AgentResultStatus.Success
);
```

### Context Propagation

Pass data between graph nodes via `EdgeOptions`:

```csharp
graph.AddDependency(research, write, new EdgeOptions
{
    ContextPropagator = new SummaryPropagator(),
    Timeout = TimeSpan.FromMinutes(2),
});
```

### Hierarchical

A manager agent delegates subtasks to worker agents:

```csharp
var result = await orchestrator.ExecuteHierarchicalAsync(
    new AgentTask { Id = TaskId.New(), Description = "Build a web app", AssignedAgent = manager.Id },
    new HierarchyOptions
    {
        WorkerAgentIds = [frontend.Id, backend.Id, designer.Id],
        MaxDelegationDepth = 3,
    }
);
```

## Agent Pool

The agent pool manages agent lifecycle:

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

**Usage:**

```csharp
var pool = sp.GetRequiredService<IAgentPool>();

// Spawn agents
var agent1 = await pool.SpawnAsync(new AgentDefinition { Name = "Agent-1", SystemPrompt = "..." });
var agent2 = await pool.SpawnAsync(new AgentDefinition { Name = "Agent-2", SystemPrompt = "..." });

// Lifecycle management
await pool.PauseAsync(agent1.Id);
await pool.ResumeAsync(agent1.Id);

// Graceful shutdown
await pool.DrainAsync(TimeSpan.FromSeconds(30));
```

When a task uses `AssignedAgent`, the default orchestrator reuses that exact agent instance instead of spawning an anonymous replacement. If cost tracking is enabled and the provider returns usage metadata, completed node results also include `AgentResult.TokenUsage`, `AgentResult.EstimatedCost`, and `AgentResultStatus.BudgetExceeded` when a configured budget is crossed.

## Tool Execution Inside ChatAgent

When you use `AddOrchestration(o => o.UseDefaults())`, Nexus also registers a default `IToolExecutor`.

Its behavior is:

- contiguous read-only tool calls are executed in parallel
- mutating tools are executed serially
- registered tool middleware is applied during execution
- permission prompts continue to flow through `ChatAgent` and `IApprovalGate`, so approval events and agent state transitions are preserved

You can tune read-only concurrency:

```csharp
services.AddNexus(nexus =>
{
    nexus.AddOrchestration(o => o
        .UseDefaults()
        .ConfigureToolExecutor(options => options.MaxReadOnlyConcurrency = 8));
});
```

## Task Graphs

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

The graph validates for:
- Duplicate node IDs
- Missing dependency references
- Cycles (via DFS)
- Unreachable nodes

## Streaming

All execution modes support streaming for real-time progress:

```csharp
await foreach (var evt in orchestrator.ExecuteGraphStreamingAsync(graph))
{
    switch (evt)
    {
        case NodeStartedEvent ns:
            Console.WriteLine($"Started: {ns.NodeId}");
            break;
        case AgentEventInGraph ae:
            Console.Write(ae.InnerEvent); // TextChunkEvent, etc.
            break;
        case NodeCompletedEvent nc:
            Console.WriteLine($"Completed: {nc.NodeId} — {nc.Result.Status}");
            break;
    }
}
```

## Read Next

- declarative workflow files: [Workflows DSL](workflows-dsl.md)
- loop-driven staged execution: [Human-Approved Workflow](../recipes/human-approved-workflow.md)
- recovery and resume: [Checkpointing](checkpointing.md)
