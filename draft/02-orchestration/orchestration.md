# Orchestration — Nexus.Orchestration

> Assembly: `Nexus.Orchestration`  
> Deps: `Nexus.Core`

## 1. IAgentPool — Lifecycle Management

```csharp
public interface IAgentPool
{
    Task<IAgent> SpawnAsync(AgentDefinition definition, CancellationToken ct);
    Task PauseAsync(AgentId id, CancellationToken ct);
    Task ResumeAsync(AgentId id, CancellationToken ct);
    Task KillAsync(AgentId id, CancellationToken ct);
    IReadOnlyList<IAgent> ActiveAgents { get; }
    IObservable<AgentLifecycleEvent> Lifecycle { get; }
    Task DrainAsync(TimeSpan timeout, CancellationToken ct);
    Task CheckpointAndStopAllAsync(ICheckpointStore store, CancellationToken ct);
}
```

## 2. ITaskGraph — DAG Definition

```csharp
public interface ITaskGraph
{
    TaskGraphId Id { get; }
    ITaskNode AddTask(AgentTask task);
    void AddDependency(ITaskNode source, ITaskNode target);
    void AddDependency(ITaskNode source, ITaskNode target, EdgeOptions? options);
    void AddConditionalEdge(ITaskNode source, ITaskNode target, Func<AgentResult, bool> condition);
    ValidationResult Validate();
}

public record EdgeOptions
{
    public IContextPropagator? ContextPropagator { get; init; }
    public TimeSpan? Timeout { get; init; }
}

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
```

## 3. IOrchestrator — Execution Engine

```csharp
public interface IOrchestrator
{
    // DAG — Buffered + Streaming
    Task<OrchestrationResult> ExecuteGraphAsync(ITaskGraph graph, CancellationToken ct);
    Task<OrchestrationResult> ExecuteGraphAsync(ITaskGraph graph, OrchestrationOptions options, CancellationToken ct);
    IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(ITaskGraph graph, CancellationToken ct);
    IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(ITaskGraph graph, OrchestrationOptions options, CancellationToken ct);

    // Pipeline (sequentiell)
    Task<OrchestrationResult> ExecuteSequenceAsync(IEnumerable<AgentTask> tasks, CancellationToken ct);
    IAsyncEnumerable<OrchestrationEvent> ExecuteSequenceStreamingAsync(IEnumerable<AgentTask> tasks, CancellationToken ct);

    // Fan-Out / Fan-In
    Task<OrchestrationResult> ExecuteParallelAsync(IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null, CancellationToken ct = default);
    IAsyncEnumerable<OrchestrationEvent> ExecuteParallelStreamingAsync(IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null, CancellationToken ct = default);

    // Hierarchisch (Manager delegiert)
    Task<OrchestrationResult> ExecuteHierarchicalAsync(AgentTask rootTask,
        HierarchyOptions options, CancellationToken ct);

    // Resume
    Task<OrchestrationResult> ResumeFromCheckpointAsync(OrchestrationSnapshot snapshot,
        ITaskGraph graph, CancellationToken ct);

    // Graph erstellen
    ITaskGraph CreateGraph();

    // Events
    IObservable<OrchestrationEvent> Events { get; }
}
```

## 4. OrchestrationOptions

```csharp
public record OrchestrationOptions
{
    public string? ThreadId { get; init; }
    public ICheckpointStore? CheckpointStore { get; init; }
    public CheckpointStrategy CheckpointStrategy { get; init; } = CheckpointStrategy.AfterEachNode;
    public int MaxConcurrentNodes { get; init; } = 10;
    public TimeSpan GlobalTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public decimal? MaxTotalCostUsd { get; init; }
}

public enum CheckpointStrategy { None, AfterEachNode, OnError, Manual }
```

## 5. OrchestrationResult

```csharp
public record OrchestrationResult
{
    public required OrchestrationStatus Status { get; init; }
    public required IReadOnlyDictionary<TaskId, AgentResult> TaskResults { get; init; }
    public TimeSpan Duration { get; init; }
    public TokenUsageSummary TokenUsage { get; init; }
    public CostSummary Cost { get; init; }
    public int CheckpointCount { get; init; }
}

public enum OrchestrationStatus { Completed, PartiallyCompleted, Failed, Cancelled, Timeout }

public record TokenUsageSummary(int TotalInputTokens, int TotalOutputTokens, int TotalTokens);
public record CostSummary(decimal TotalUsd, IReadOnlyDictionary<string, decimal> PerProvider);
```

## 6. OrchestrationEvents

```csharp
public abstract record OrchestrationEvent(TaskGraphId GraphId, DateTimeOffset Timestamp);

public record NodeStartedEvent(TaskGraphId GraphId, TaskId NodeId, AgentId AgentId)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record NodeCompletedEvent(TaskGraphId GraphId, TaskId NodeId, AgentResult Result)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record NodeFailedEvent(TaskGraphId GraphId, TaskId NodeId, Exception Error)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record NodeSkippedEvent(TaskGraphId GraphId, TaskId NodeId, string Reason)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

// Durchgeleitetes Agent-Event mit Graph-Kontext
public record AgentEventInGraph(TaskGraphId GraphId, TaskId NodeId, AgentEvent InnerEvent)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record CheckpointCreatedEvent(TaskGraphId GraphId, CheckpointId CheckpointId)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);

public record OrchestrationCompletedEvent(TaskGraphId GraphId, OrchestrationResult Result)
    : OrchestrationEvent(GraphId, DateTimeOffset.UtcNow);
```

## 7. TaskErrorPolicy

```csharp
public record TaskErrorPolicy
{
    public RetryOptions? Retry { get; init; }
    public FallbackOptions? Fallback { get; init; }
    public Func<AgentResult, IAgentContext, Task>? CompensationAction { get; init; }
    public AgentResult? SkipWithDefault { get; init; }
    public bool EscalateToHuman { get; init; }
    public bool SendToDeadLetter { get; init; }
    public CircuitBreakerOptions? CircuitBreaker { get; init; }
    public int MaxIterations { get; init; } = 25;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

public record RetryOptions
{
    public int MaxRetries { get; init; } = 3;
    public BackoffType BackoffType { get; init; } = BackoffType.ExponentialWithJitter;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public IReadOnlyList<Type>? RetryOn { get; init; }
}

public record FallbackOptions
{
    public string? AlternateModelId { get; init; }
    public string? AlternateChatClientName { get; init; }
    public IAgent? FallbackAgent { get; init; }
}

public record CircuitBreakerOptions
{
    public double FailureThreshold { get; init; } = 0.5;
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(60);
}
```

## 8. IContextPropagator

```csharp
public interface IContextPropagator
{
    Task<PropagatedContext> ExtractAsync(AgentResult result, AgentTask nextTask,
        int maxTokens, CancellationToken ct);
}

public record PropagatedContext
{
    public required string Summary { get; init; }
    public IReadOnlyDictionary<string, object> StructuredData { get; init; } = new Dictionary<string, object>();
    public IReadOnlyList<ArtifactReference> Artifacts { get; init; } = [];
    public int EstimatedTokens { get; init; }
}
```

Built-in Propagators:
- `FullPassthroughPropagator` — Alles durchreichen (Default)
- `SummarizingPropagator` — LLM-basierte Zusammenfassung auf N Tokens
- `StructuredOnlyPropagator` — Nur JSON, kein Freitext
- `SelectivePropagator` — Regex/JsonPath Filter

## 9. ChatAgent — Built-in Default Agent

```csharp
public class ChatAgent : IAgent
{
    public ChatAgent(IChatClient client, ChatAgentOptions options) { }

    public static AgentDefinition Define(string name, IChatClient client,
        string systemPrompt, string[]? tools = null, AgentBudget? budget = null) { }
}

public record ChatAgentOptions
{
    public string? SystemPrompt { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    public int MaxIterations { get; init; } = 25;
    public ContextWindowOptions? ContextWindow { get; init; }
}
```

## 10. Built-in Routers

```csharp
public interface IChatClientRouter : IChatClient
{
    void Register(string name, IChatClient client);
}

// Implementierungen:
public class FallbackRouter : IChatClientRouter { }        // Primary → Secondary → Tertiary
public class CostAwareRouter : IChatClientRouter { }       // Günstigstes das Capabilities erfüllt
public class LatencyRouter : IChatClientRouter { }         // Schnellstes Modell
public class RoundRobinRouter : IChatClientRouter { }      // Load Balancing
public class CapabilityRouter : IChatClientRouter { }      // Filtert nach Features
```

## 11. Built-in Middleware

| Middleware | Phase | Beschreibung |
|-----------|-------|-------------|
| `LoggingMiddleware` | Agent | Loggt Start/Ende/Duration |
| `BudgetGuardMiddleware` | Agent | Bricht ab bei Budget-Überschreitung |
| `TelemetryMiddleware` | Agent + Tool | OpenTelemetry Spans |
| `RetryMiddleware` | Agent | Retry mit Backoff |
| `TimeoutMiddleware` | Agent | CancellationToken nach Timeout |
| `GuardrailMiddleware` | Agent | Input/Output Guards |
| `ToolAuditMiddleware` | Tool | Audit Trail für Tool-Calls |
| `ToolApprovalMiddleware` | Tool | Human-in-the-Loop für destructive Tools |
| `ToolCachingMiddleware` | Tool | Cache für idempotente Tools |

## 12. Ordnerstruktur

```
Nexus.Orchestration/
├── IAgentPool.cs, IOrchestrator.cs, ITaskGraph.cs, IScheduler.cs
├── IContextPropagator.cs, IConcurrencyLimiter.cs
├── ChatAgent.cs, ChatAgentOptions.cs
├── OrchestrationOptions.cs, OrchestrationResult.cs
├── OrchestrationEvent.cs (Hierarchie)
├── TaskErrorPolicy.cs, RetryOptions.cs, FallbackOptions.cs, CircuitBreakerOptions.cs
├── StreamMerger.cs
├── Defaults/
│   ├── DefaultAgentPool.cs, DefaultOrchestrator.cs
│   ├── DefaultTaskGraph.cs, DefaultScheduler.cs
├── Propagators/
│   ├── FullPassthroughPropagator.cs, SummarizingPropagator.cs
│   ├── StructuredOnlyPropagator.cs, SelectivePropagator.cs
├── Routing/
│   ├── FallbackRouter.cs, CostAwareRouter.cs
│   ├── LatencyRouter.cs, RoundRobinRouter.cs
└── Middleware/
    ├── LoggingMiddleware.cs, BudgetGuardMiddleware.cs
    ├── TelemetryMiddleware.cs, RetryMiddleware.cs
    └── TimeoutMiddleware.cs
```
