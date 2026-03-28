# Core Engine — Nexus.Core

> Assembly: `Nexus.Core`  
> Deps: `Microsoft.Extensions.AI.Abstractions`

Das Core-Paket definiert alle Interfaces und DTOs. Es enthält keine Geschäftslogik, nur Verträge.

## 1. IAgent

```csharp
public interface IAgent
{
    AgentId Id { get; }
    string Name { get; }
    AgentState State { get; }

    Task<AgentResult> ExecuteAsync(
        AgentTask task, IAgentContext context, CancellationToken ct = default);

    IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(
        AgentTask task, IAgentContext context,
        [EnumeratorCancellation] CancellationToken ct = default);
}
```

### AgentId

```csharp
[JsonConverter(typeof(AgentIdJsonConverter))]
public readonly record struct AgentId(Guid Value)
{
    public static AgentId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}
```

### AgentState

```csharp
public enum AgentState
{
    Created,
    Idle,
    Running,
    WaitingForApproval,
    WaitingForInput,
    WaitingForRemoteAgent,
    Paused,
    Completed,
    Failed,
    Disposed
}
```

### AgentResult

```csharp
public record AgentResult
{
    public required AgentResultStatus Status { get; init; }
    public string? Text { get; init; }
    public JsonElement? StructuredOutput { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    public TokenUsageSummary? TokenUsage { get; init; }
    public decimal? EstimatedCost { get; init; }

    public static AgentResult Success(string text) => new() { Status = AgentResultStatus.Success, Text = text };
    public static AgentResult Failed(string reason) => new() { Status = AgentResultStatus.Failed, Text = reason };
}

public enum AgentResultStatus { Success, Failed, Cancelled, Timeout, BudgetExceeded }
```

### AgentDefinition

```csharp
public record AgentDefinition
{
    public required string Name { get; init; }
    public string? Role { get; init; }
    public string? SystemPrompt { get; init; }
    public string? ModelId { get; init; }
    public string? ChatClientName { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    public IReadOnlyList<McpServerConfig> McpServers { get; init; } = [];
    public AgentBudget? Budget { get; init; }
    public TimeSpan? Timeout { get; init; }
    public TaskErrorPolicy? ErrorPolicy { get; init; }
    public ContextWindowOptions? ContextWindow { get; init; }
    public GovernancePolicy? Policy { get; init; }
}

public record AgentBudget(
    int? MaxInputTokens = null,
    int? MaxOutputTokens = null,
    decimal? MaxCostUsd = null,
    int? MaxIterations = null,
    int? MaxToolCalls = null);
```

## 2. IAgentContext

```csharp
public interface IAgentContext
{
    IAgent Agent { get; }
    IChatClient GetChatClient(string? name = null);
    IToolRegistry Tools { get; }
    IConversationStore? Conversations { get; }
    IWorkingMemory? WorkingMemory { get; }
    IMessageBus? MessageBus { get; }
    IA2AClient? RemoteAgents { get; }
    IAgUiEventEmitter? UiEvents { get; }
    IApprovalGate? ApprovalGate { get; }
    IBudgetTracker? Budget { get; }
    ISecretProvider? Secrets { get; }
    CorrelationContext Correlation { get; }
    Task<IAgent> SpawnChildAsync(AgentDefinition definition, CancellationToken ct);
}
```

Der Kontext ist **facettiert** — jede Property kann `null` sein wenn das entsprechende Modul nicht registriert wurde. Kein Feature wird erzwungen.

## 3. ITool

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolAnnotations? Annotations => null;
    JsonElement? InputSchema => null;

    Task<ToolResult> ExecuteAsync(
        JsonElement input, IToolContext context, CancellationToken ct);

    virtual async IAsyncEnumerable<ToolEvent> ExecuteStreamingAsync(
        JsonElement input, IToolContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ToolProgressEvent(Name, "Executing...", 0);
        var result = await ExecuteAsync(input, context, ct);
        yield return new ToolCompletedEvent(Name, result);
    }
}
```

### ToolAnnotations (MCP-kompatibel)

```csharp
public record ToolAnnotations
{
    public bool IsReadOnly { get; init; }
    public bool IsIdempotent { get; init; }
    public bool IsDestructive { get; init; }
    public bool IsOpenWorld { get; init; }
    public bool RequiresApproval { get; init; }
    public TimeSpan? EstimatedDuration { get; init; }
    public ToolCostCategory CostCategory { get; init; } = ToolCostCategory.Free;
}

public enum ToolCostCategory { Free, Low, Medium, High, RequiresBudgetApproval }
```

### ToolResult

```csharp
public record ToolResult
{
    public required bool IsSuccess { get; init; }
    public object? Value { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    public static ToolResult Success(object value) => new() { IsSuccess = true, Value = value };
    public static ToolResult Failure(string error) => new() { IsSuccess = false, Error = error };
    public static ToolResult Denied(string reason) => new() { IsSuccess = false, Error = $"DENIED: {reason}" };
}
```

### LambdaTool — Inline-Tool per Delegate

```csharp
public class LambdaTool : ITool
{
    public string Name { get; }
    public string Description { get; }
    private readonly Func<JsonElement, IToolContext, CancellationToken, Task<ToolResult>> _execute;

    public LambdaTool(string name, string description,
        Func<JsonElement, IToolContext, CancellationToken, Task<ToolResult>> execute)
    {
        Name = name; Description = description; _execute = execute;
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext ctx, CancellationToken ct)
        => _execute(input, ctx, ct);
}
```

### AIFunction ↔ ITool Bridge

```csharp
public static class ToolExtensions
{
    public static ITool AsNexusTool(this AIFunction function)
        => new AIFunctionToolAdapter(function);

    public static AIFunction AsAIFunction(this ITool tool)
        => new NexusToolAIFunctionAdapter(tool);
}
```

## 4. IToolRegistry

```csharp
public interface IToolRegistry
{
    void Register(ITool tool);
    void Register(AIFunction function);
    ITool? Resolve(string name);
    IReadOnlyList<ITool> ListAll();
    IReadOnlyList<ITool> ListForAgent(AgentId agentId);
    IReadOnlyList<AIFunction> AsAIFunctions();
}
```

## 5. Events

Alle Events erben von `AgentEvent` oder `ToolEvent`. Siehe [03-streaming/streaming.md](../03-streaming/streaming.md) für die vollständige Event-Hierarchie.

## 6. Pipeline-Infrastruktur

```csharp
public interface IAgentMiddleware
{
    Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx,
        AgentExecutionDelegate next, CancellationToken ct);

    virtual IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task, IAgentContext ctx,
        StreamingAgentExecutionDelegate next,
        [EnumeratorCancellation] CancellationToken ct = default)
        => next(task, ctx, ct);  // Default: Pass-through
}

public delegate Task<AgentResult> AgentExecutionDelegate(
    AgentTask task, IAgentContext ctx, CancellationToken ct);

public delegate IAsyncEnumerable<AgentEvent> StreamingAgentExecutionDelegate(
    AgentTask task, IAgentContext ctx, CancellationToken ct);
```

Analog für Tools:

```csharp
public interface IToolMiddleware
{
    Task<ToolResult> InvokeAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        ToolExecutionDelegate next, CancellationToken ct);

    virtual IAsyncEnumerable<ToolEvent> InvokeStreamingAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        StreamingToolExecutionDelegate next,
        [EnumeratorCancellation] CancellationToken ct = default)
        => next(tool, input, ctx, ct);
}
```

## 7. Contracts

### IApprovalGate

```csharp
public interface IApprovalGate
{
    Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default);
}

public record ApprovalRequest(string Description, AgentId RequestingAgent,
    string? ToolName = null, JsonElement? Context = null);

public record ApprovalResult(bool IsApproved, string? ApprovedBy = null,
    string? Comment = null, JsonElement? ModifiedContext = null);
```

### IBudgetTracker

```csharp
public interface IBudgetTracker
{
    Task TrackUsageAsync(AgentId agentId, int inputTokens, int outputTokens,
        decimal? cost, CancellationToken ct);
    Task<BudgetStatus> GetStatusAsync(AgentId agentId, CancellationToken ct);
    Task<bool> HasBudgetAsync(AgentId agentId, CancellationToken ct);
}

public record BudgetStatus(int TotalInputTokens, int TotalOutputTokens,
    decimal TotalCost, AgentBudget? Limit, bool IsExhausted);
```

### IRateLimiter

```csharp
public interface IRateLimiter
{
    Task<RateLimitLease> AcquireAsync(string resource, int tokens = 1, CancellationToken ct = default);
}

public record RateLimitLease(bool IsAcquired, TimeSpan? RetryAfter = null) : IDisposable
{
    public void Dispose() { /* Release lease */ }
}
```

### ISecretProvider

```csharp
public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
}
```

### IAuditLog

```csharp
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
    IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

public record AuditEntry(DateTimeOffset Timestamp, string Action, AgentId AgentId,
    string? UserId = null, string? CorrelationId = null,
    JsonElement? Details = null, AuditSeverity Severity = AuditSeverity.Info);

public enum AuditSeverity { Debug, Info, Warning, Error, Critical }
```

### CorrelationContext

```csharp
public record CorrelationContext
{
    public required string TraceId { get; init; }
    public required string SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string? ThreadId { get; init; }
    public string? UserId { get; init; }
    public IDictionary<string, string> Baggage { get; init; } = new Dictionary<string, string>();
}
```

## 8. NexusBuilder — DI Registration

```csharp
public class NexusBuilder
{
    internal IServiceCollection Services { get; }

    public NexusBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory) { ... }
    public NexusBuilder UseChatClient(string name, Func<IServiceProvider, IChatClient> factory) { ... }
    public NexusBuilder UseRouter<TRouter>() where TRouter : class, IChatClientRouter { ... }
    public NexusBuilder AddOrchestration(Action<OrchestrationBuilder>? configure = null) { ... }
    public NexusBuilder AddMessaging(Action<MessagingBuilder>? configure = null) { ... }
    public NexusBuilder AddGuardrails(Action<GuardrailBuilder>? configure = null) { ... }
    public NexusBuilder AddMemory(Action<MemoryBuilder>? configure = null) { ... }
    public NexusBuilder AddCheckpointing(Action<CheckpointBuilder>? configure = null) { ... }
    public NexusBuilder AddMcp(Action<McpBuilder>? configure = null) { ... }
    public NexusBuilder AddA2A(Action<A2ABuilder>? configure = null) { ... }
    public NexusBuilder AddAgUi(Action<AgUiBuilder>? configure = null) { ... }
    public NexusBuilder AddTools(Action<ToolBuilder>? configure = null) { ... }
    public NexusBuilder AddSecrets(Action<SecretBuilder>? configure = null) { ... }
    public NexusBuilder AddRateLimiting(Action<RateLimitBuilder>? configure = null) { ... }
    public NexusBuilder AddTelemetry(Action<TelemetryBuilder>? configure = null) { ... }
    public NexusBuilder AddAuditLog<TAuditLog>() where TAuditLog : class, IAuditLog { ... }
    public NexusBuilder AddApprovalGate<TGate>() where TGate : class, IApprovalGate { ... }
}

// Einstiegspunkt:
public static class NexusServiceCollectionExtensions
{
    public static IServiceCollection AddNexus(
        this IServiceCollection services, Action<NexusBuilder> configure)
    {
        var builder = new NexusBuilder(services);
        configure(builder);
        return services;
    }
}
```

## 9. Ordnerstruktur

```
Nexus.Core/
├── Agents/
│   ├── IAgent.cs, IAgentContext.cs, AgentId.cs, AgentState.cs
│   ├── AgentDefinition.cs, AgentResult.cs, AgentBudget.cs
│   └── AgentTask.cs
├── Events/
│   ├── AgentEvent.cs (abstract base)
│   ├── TextChunkEvent.cs, ReasoningChunkEvent.cs
│   ├── ToolCallStartedEvent.cs, ToolCallProgressEvent.cs, ToolCallCompletedEvent.cs
│   ├── AgentStateChangedEvent.cs, AgentIterationEvent.cs
│   ├── ApprovalRequestedEvent.cs, SubAgentSpawnedEvent.cs
│   ├── TokenUsageEvent.cs
│   └── AgentCompletedEvent.cs, AgentFailedEvent.cs
├── Tools/
│   ├── ITool.cs, IToolRegistry.cs, IToolContext.cs
│   ├── ToolResult.cs, ToolAnnotations.cs, ToolEvent.cs
│   ├── LambdaTool.cs, AIFunctionToolAdapter.cs
│   └── ToolInputAttribute.cs (Source Gen marker)
├── Pipeline/
│   ├── IAgentMiddleware.cs, IToolMiddleware.cs, IMessageMiddleware.cs
│   └── PipelineBuilder.cs
├── Auth/
│   ├── IAuthStrategy.cs, AuthMethod.cs, AuthToken.cs
├── Routing/
│   ├── IChatClientRouter.cs, RoutingStrategy.cs
├── Contracts/
│   ├── IApprovalGate.cs, IBudgetTracker.cs, IRateLimiter.cs
│   ├── ISecretProvider.cs, IAuditLog.cs, IContextWindowManager.cs
│   └── CorrelationContext.cs
├── Configuration/
│   ├── NexusBuilder.cs, NexusOptions.cs
└── Extensions/
    ├── AgentExtensions.cs, StreamingExtensions.cs, ToolExtensions.cs
```
