# Nexus.Core API Reference

## Namespace: Nexus.Core.Agents

### IAgent

```csharp
public interface IAgent
{
    AgentId Id { get; }
    string Name { get; }
    AgentState State { get; }
    Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(AgentTask task, IAgentContext context, CancellationToken ct = default);
}
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
    public IReadOnlyList<string> ToolNames { get; init; }
    public IReadOnlyList<McpServerConfig> McpServers { get; init; }
    public AgentBudget? Budget { get; init; }
    public TimeSpan? Timeout { get; init; }
    public TaskErrorPolicy? ErrorPolicy { get; init; }
    public ContextWindowOptions? ContextWindow { get; init; }
}
```

### AgentResult

```csharp
public record AgentResult
{
    public required AgentResultStatus Status { get; init; }
    public string? Text { get; init; }
    public JsonElement? StructuredOutput { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; }
    public TokenUsageSummary? TokenUsage { get; init; }
    public decimal? EstimatedCost { get; init; }

    public static AgentResult Success(string text, TokenUsageSummary? tokenUsage = null, decimal? estimatedCost = null);
    public static AgentResult Failed(string reason, TokenUsageSummary? tokenUsage = null, decimal? estimatedCost = null);
    public static AgentResult Cancelled();
    public static AgentResult Timeout(string reason, TokenUsageSummary? tokenUsage = null, decimal? estimatedCost = null);
    public static AgentResult BudgetExceeded(string reason, TokenUsageSummary? tokenUsage = null, decimal? estimatedCost = null);
}
```

### AgentResultStatus

```csharp
public enum AgentResultStatus { Success, Failed, Cancelled, Timeout, BudgetExceeded }
```

### AgentState

```csharp
public enum AgentState { Idle, Running, Paused, Stopped }
```

### AgentId

Strongly-typed `Guid` wrapper with `AgentId.New()` factory.

### AgentTask

```csharp
public record AgentTask
{
    public required TaskId Id { get; init; }
    public required string Description { get; init; }
    public AgentId? AssignedAgent { get; init; }
}
```

### TokenUsageSummary

```csharp
public record TokenUsageSummary(int TotalInputTokens, int TotalOutputTokens, int TotalTokens);
```

### McpServerConfig

```csharp
public record McpServerConfig
{
    public required string Name { get; init; }
    public string? Command { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }
    public Uri? Endpoint { get; init; }
    public ToolFilter? AllowedTools { get; init; }
}
```

### ContextWindowOptions

```csharp
public record ContextWindowOptions
{
    public int MaxTokens { get; init; }              // Default: 128_000
    public int TargetTokens { get; init; }           // Default: 100_000
    public ContextTrimStrategy TrimStrategy { get; init; }  // Default: SlidingWindow
    public int ReservedForOutput { get; init; }      // Default: 8_000
    public int ReservedForTools { get; init; }       // Default: 4_000
}
```

### ContextTrimStrategy

```csharp
public enum ContextTrimStrategy { SlidingWindow, SummarizeAndTruncate, KeepFirstAndLast, TokenBudget }
```

## Namespace: Nexus.Core.Tools

### ITool

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolAnnotations? Annotations => null;
    JsonElement? InputSchema => null;
    Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default);
    IAsyncEnumerable<ToolEvent> ExecuteStreamingAsync(JsonElement input, IToolContext context, CancellationToken ct = default);
}
```

### IToolRegistry

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

### LambdaTool

```csharp
public class LambdaTool : ITool
{
    public LambdaTool(string name, string description,
        Func<JsonElement, IToolContext, CancellationToken, Task<ToolResult>> execute);
    public ToolAnnotations? Annotations { get; init; }
    public JsonElement? InputSchema { get; init; }
}
```

## Namespace: Nexus.Core.Pipeline

### IAgentMiddleware

```csharp
public delegate Task<AgentResult> AgentExecutionDelegate(AgentTask task, IAgentContext ctx, CancellationToken ct);
public delegate IAsyncEnumerable<AgentEvent> StreamingAgentExecutionDelegate(AgentTask task, IAgentContext ctx, CancellationToken ct);

public interface IAgentMiddleware
{
    Task<AgentResult> InvokeAsync(AgentTask task, IAgentContext ctx, AgentExecutionDelegate next, CancellationToken ct);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(AgentTask task, IAgentContext ctx, StreamingAgentExecutionDelegate next, CancellationToken ct = default);
}
```

### IToolMiddleware

```csharp
public delegate Task<ToolResult> ToolExecutionDelegate(ITool tool, JsonElement input, IToolContext ctx, CancellationToken ct);
public delegate IAsyncEnumerable<ToolEvent> StreamingToolExecutionDelegate(ITool tool, JsonElement input, IToolContext ctx, CancellationToken ct);

public interface IToolMiddleware
{
    Task<ToolResult> InvokeAsync(ITool tool, JsonElement input, IToolContext ctx, ToolExecutionDelegate next, CancellationToken ct);
    IAsyncEnumerable<ToolEvent> InvokeStreamingAsync(ITool tool, JsonElement input, IToolContext ctx, StreamingToolExecutionDelegate next, CancellationToken ct = default);
}
```

## Namespace: Nexus.Core.Configuration

### NexusBuilder

```csharp
public class NexusBuilder
{
    public NexusBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory);
    public NexusBuilder UseChatClient(string name, Func<IServiceProvider, IChatClient> factory);
    public NexusBuilder UseRouter<TRouter>() where TRouter : class, IChatClientRouter;
    public NexusBuilder AddOrchestration(Action<OrchestrationBuilder>? configure = null);
    public NexusBuilder AddMessaging(Action<MessagingBuilder>? configure = null);
    public NexusBuilder AddGuardrails(Action<GuardrailBuilder>? configure = null);
    public NexusBuilder AddPermissions(Action<PermissionBuilder>? configure = null);
    public NexusBuilder AddCostTracking(Action<CostTrackingBuilder>? configure = null);
    public NexusBuilder AddMemory(Action<MemoryBuilder>? configure = null);
    public NexusBuilder AddCheckpointing(Action<CheckpointBuilder>? configure = null);
    public NexusBuilder AddMcp(Action<McpBuilder>? configure = null);
    public NexusBuilder AddA2A(Action<A2ABuilder>? configure = null);
    public NexusBuilder AddAgUi(Action<AgUiBuilder>? configure = null);
    public NexusBuilder AddTools(Action<ToolBuilder>? configure = null);
    public NexusBuilder AddSecrets(Action<SecretBuilder>? configure = null);
    public NexusBuilder AddRateLimiting(Action<RateLimitBuilder>? configure = null);
    public NexusBuilder AddTelemetry(Action<TelemetryBuilder>? configure = null);
    public NexusBuilder AddAuditLog<TAuditLog>() where TAuditLog : class, IAuditLog;
    public NexusBuilder AddApprovalGate<TGate>() where TGate : class, IApprovalGate;
}
```

### Extension Method

```csharp
public static IServiceCollection AddNexus(this IServiceCollection services, Action<NexusBuilder> configure);
```

## Namespace: Nexus.Core.Contracts

### IConversationStore

```csharp
public interface IConversationStore
{
    Task<ConversationId> CreateAsync(string? threadId = null, CancellationToken ct = default);
    Task AppendAsync(ConversationId id, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ConversationId id, int? maxMessages = null, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetWindowAsync(ConversationId id, int maxTokens, ContextTrimStrategy strategy, CancellationToken ct = default);
    Task<ConversationId> ForkAsync(ConversationId parentId, Func<ChatMessage, bool>? filter = null, CancellationToken ct = default);
}
```

### IWorkingMemory

```csharp
public interface IWorkingMemory
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
```

### IMessageBus

```csharp
public interface IMessageBus
{
    Task SendAsync(AgentId target, AgentMessage message, CancellationToken ct = default);
    Task PublishAsync(string topic, AgentMessage message, CancellationToken ct = default);
    Task<AgentMessage> RequestAsync(AgentId target, AgentMessage request, TimeSpan timeout, CancellationToken ct = default);
    IDisposable Subscribe(AgentId subscriber, string topic, Func<AgentMessage, Task> handler);
    Task BroadcastAsync(AgentMessage message, CancellationToken ct = default);
}
```

## Namespace: Nexus.Core.Auth

### IAuthStrategy

```csharp
public interface IAuthStrategy
{
    AuthMethod Method { get; }
    Task<AuthToken> AcquireTokenAsync(CancellationToken ct = default);
    Task<AuthToken> RefreshTokenAsync(AuthToken expired, CancellationToken ct = default);
    Task RevokeAsync(AuthToken token, CancellationToken ct = default);
}
```

### AuthMethod

```csharp
public enum AuthMethod { ApiKey, OAuth2ClientCredentials, OAuth2AuthorizationCode, OAuth2DeviceFlow, OpenIdConnect, MutualTls, Custom }
```

### AuthToken

```csharp
public record AuthToken
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlySet<string> Scopes { get; init; }
    public string? ResourceIndicator { get; init; }
    public bool IsExpired { get; }
    public bool IsExpiringSoon(TimeSpan threshold);
}
```
