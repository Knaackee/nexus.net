# Core Engine

The core engine (`Nexus.Core`) provides the foundational abstractions for the entire framework.

## Agents

### IAgent

The primary abstraction for an AI agent:

```csharp
public interface IAgent
{
    AgentId Id { get; }
    string Name { get; }
    AgentState State { get; }

    Task<AgentResult> ExecuteAsync(
        AgentTask task, IAgentContext context, CancellationToken ct = default);

    IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(
        AgentTask task, IAgentContext context, CancellationToken ct = default);
}
```

Every agent has a unique `AgentId`, a human-readable `Name`, and a `State` that tracks its lifecycle (`Idle`, `Running`, `Paused`, `Stopped`).

### AgentDefinition

A declarative description of an agent's configuration:

```csharp
var definition = new AgentDefinition
{
    Name = "Researcher",
    Role = "Research and analysis",
    SystemPrompt = "You are a research assistant...",
    ModelId = "gpt-4o",
    ChatClientName = "openai",           // Named IChatClient
    ToolNames = ["web_search", "read_file"],
    McpServers = [new McpServerConfig { Name = "filesystem", Command = "npx", Arguments = ["-y", "@modelcontextprotocol/server-filesystem"] }],
    Budget = new AgentBudget { MaxInputTokens = 50_000, MaxCostUsd = 2.0m },
    Timeout = TimeSpan.FromMinutes(5),
    ContextWindow = new ContextWindowOptions
    {
        MaxTokens = 128_000,
        TargetTokens = 100_000,
        TrimStrategy = ContextTrimStrategy.SlidingWindow,
        ReservedForOutput = 8_000,
    },
};
```

### AgentResult

Returned from agent execution:

```csharp
public record AgentResult
{
    public required AgentResultStatus Status { get; init; }  // Success, Failed, Cancelled, Timeout, BudgetExceeded
    public string? Text { get; init; }
    public JsonElement? StructuredOutput { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; }
    public TokenUsageSummary? TokenUsage { get; init; }
    public decimal? EstimatedCost { get; init; }
}
```

Factory methods for common cases:

```csharp
AgentResult.Success("The answer is 42");
AgentResult.Failed("Model returned an error");
AgentResult.Cancelled();
AgentResult.Timeout("Exceeded 5 minute limit");
AgentResult.BudgetExceeded("Cost limit reached");
```

At runtime, `ChatAgent` can populate `TokenUsage` and `EstimatedCost` automatically when the underlying chat client exposes usage metadata.

### AgentTask

A unit of work assigned to an agent:

```csharp
var task = new AgentTask
{
    Id = TaskId.New(),
    Description = "Analyze the quarterly report and summarize key findings",
    AssignedAgent = agent.Id,
};
```

## Tools

### ITool

Tools are functions that agents can call during execution:

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolAnnotations? Annotations => null;
    JsonElement? InputSchema => null;

    Task<ToolResult> ExecuteAsync(
        JsonElement input, IToolContext context, CancellationToken ct = default);

    IAsyncEnumerable<ToolEvent> ExecuteStreamingAsync(
        JsonElement input, IToolContext context, CancellationToken ct = default);
}
```

### LambdaTool

Inline tool definitions without implementing `ITool`:

```csharp
var tool = new LambdaTool(
    "get_weather",
    "Get current weather for a city",
    async (input, context, ct) =>
    {
        var city = input.GetProperty("city").GetString()!;
        var weather = await weatherService.GetAsync(city, ct);
        return ToolResult.Success(weather.ToString());
    }
)
{
    InputSchema = JsonDocument.Parse("""
        { "type": "object", "properties": { "city": { "type": "string" } }, "required": ["city"] }
    """).RootElement,
    Annotations = new ToolAnnotations { ReadOnly = true },
};
```

### IToolRegistry

Central registry for tool discovery and resolution:

```csharp
public interface IToolRegistry
{
    void Register(ITool tool);
    void Register(AIFunction function);           // Microsoft.Extensions.AI interop
    ITool? Resolve(string name);
    IReadOnlyList<ITool> ListAll();
    IReadOnlyList<ITool> ListForAgent(AgentId agentId);
    IReadOnlyList<AIFunction> AsAIFunctions();   // Export as AIFunction for IChatClient
}
```

## Pipeline

The pipeline intercepts agent and tool execution, enabling cross-cutting concerns like logging, telemetry, guardrails, and retry logic.

### IAgentMiddleware

```csharp
public interface IAgentMiddleware
{
    Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx,
        AgentExecutionDelegate next, CancellationToken ct);

    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task, IAgentContext ctx,
        StreamingAgentExecutionDelegate next, CancellationToken ct = default);
}
```

### IToolMiddleware

```csharp
public interface IToolMiddleware
{
    Task<ToolResult> InvokeAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        ToolExecutionDelegate next, CancellationToken ct);

    IAsyncEnumerable<ToolEvent> InvokeStreamingAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        StreamingToolExecutionDelegate next, CancellationToken ct = default);
}
```

Both middleware types follow the same pattern: receive the request, optionally modify it, call `next(...)`, optionally modify the response, and return.

### Example: Logging Middleware

```csharp
public class LoggingMiddleware : IAgentMiddleware
{
    private readonly ILogger _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger) => _logger = logger;

    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx,
        AgentExecutionDelegate next, CancellationToken ct)
    {
        _logger.LogInformation("Agent {Name} starting task {TaskId}", ctx.Agent.Name, task.Id);
        var sw = Stopwatch.StartNew();
        var result = await next(task, ctx, ct);
        _logger.LogInformation("Agent {Name} completed in {Elapsed}ms: {Status}",
            ctx.Agent.Name, sw.ElapsedMilliseconds, result.Status);
        return result;
    }
}
```

## Configuration

### NexusBuilder

All services are registered through a fluent builder:

```csharp
services.AddNexus(nexus =>
{
    // LLM clients
    nexus.UseChatClient(_ => myClient);
    nexus.UseChatClient("fast", _ => cheapClient);

    // Model routing
    nexus.UseRouter<CostAwareRouter>();

    // Feature modules
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddMemory(m => m.UseInMemory());
    nexus.AddMessaging();
    nexus.AddGuardrails(g => g.AddPromptInjectionDetector());
    nexus.AddCheckpointing();
    nexus.AddTelemetry();
    nexus.AddRateLimiting();
    nexus.AddSecrets();

    // Protocol adapters
    nexus.AddMcp();
    nexus.AddA2A();
    nexus.AddAgUi();

    // Tools
    nexus.AddTools(t => t.RegisterAssemblyTools());

    // Cross-cutting
    nexus.AddAuditLog<FileAuditLog>();
    nexus.AddApprovalGate<HumanInTheLoopGate>();
});
```

### Sub-Builders

Each `Add*` method accepts an optional `Action<*Builder>` delegate for fine-grained configuration. The sub-builder classes are:

| Builder | Purpose |
|---------|---------|
| `OrchestrationBuilder` | Agent pool size, default timeout, parallelism limits |
| `MemoryBuilder` | Store implementation, context window defaults |
| `MessagingBuilder` | Bus implementation, DLQ policy |
| `GuardrailBuilder` | Register guardrails, set default phase |
| `CheckpointBuilder` | Store implementation, serialization format |
| `TelemetryBuilder` | Activity source, meter, export config |
| `McpBuilder` | MCP server configs, transport options |
| `A2ABuilder` | A2A endpoint URLs, auth settings |
| `AgUiBuilder` | SSE configuration, event filtering |
| `ToolBuilder` | Scan assemblies, register tool factories |
| `SecretBuilder` | Secret provider backends |
| `RateLimitBuilder` | Rate limit policies, token bucket config |

## Events

Agent execution emits a stream of events for real-time monitoring:

| Event | Description |
|-------|-------------|
| `TextChunkEvent` | A text token from the LLM |
| `ToolCallEvent` | Agent is calling a tool |
| `ToolResultEvent` | Tool returned a result |
| `ToolProgressEvent` | Tool progress indicator |
| `ToolCompletedEvent` | Tool execution finished |
| `AgentCompletedEvent` | Agent finished its task |
| `AgentErrorEvent` | Agent encountered an error |

Events are yielded from `ExecuteStreamingAsync` and can be consumed via `await foreach`.
