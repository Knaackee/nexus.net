# Middleware

Agent and tool middleware enable cross-cutting concerns like logging, telemetry, retry, and guardrails through composable pipeline interception.

## Agent Middleware

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

The `next` delegate calls the next middleware in the chain (or the agent itself at the end).

### Example: Retry Middleware

```csharp
public class RetryMiddleware : IAgentMiddleware
{
    private readonly int _maxRetries;

    public RetryMiddleware(int maxRetries = 3) => _maxRetries = maxRetries;

    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx,
        AgentExecutionDelegate next, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            var result = await next(task, ctx, ct);
            if (result.Status == AgentResultStatus.Success || attempt == _maxRetries)
                return result;

            await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)), ct);
        }

        return AgentResult.Failed("Max retries exceeded");
    }
}
```

### Example: Telemetry Middleware

```csharp
public class TelemetryMiddleware : IAgentMiddleware
{
    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx,
        AgentExecutionDelegate next, CancellationToken ct)
    {
        using var activity = NexusTelemetry.ActivitySource.StartActivity("agent.execute");
        activity?.SetTag("agent.name", ctx.Agent.Name);
        activity?.SetTag("task.id", task.Id.ToString());

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await next(task, ctx, ct);
            activity?.SetTag("result.status", result.Status.ToString());
            NexusTelemetry.AgentExecutions.Add(1);
            NexusTelemetry.AgentLatencyMs.Record(sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            NexusTelemetry.AgentErrors.Add(1);
            throw;
        }
    }
}
```

## Tool Middleware

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

### Example: Tool Logging Middleware

```csharp
public class ToolLoggingMiddleware : IToolMiddleware
{
    private readonly ILogger _logger;

    public ToolLoggingMiddleware(ILogger<ToolLoggingMiddleware> logger) => _logger = logger;

    public async Task<ToolResult> InvokeAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        ToolExecutionDelegate next, CancellationToken ct)
    {
        _logger.LogInformation("Tool {Name} called with {Input}", tool.Name, input);
        var result = await next(tool, input, ctx, ct);
        _logger.LogInformation("Tool {Name} returned {Status}", tool.Name, result.IsSuccess);
        return result;
    }
}
```

### Example: Guardrail Tool Middleware

```csharp
public class GuardrailToolMiddleware : IToolMiddleware
{
    private readonly IGuardrailPipeline _guardrails;

    public GuardrailToolMiddleware(IGuardrailPipeline guardrails) => _guardrails = guardrails;

    public async Task<ToolResult> InvokeAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        ToolExecutionDelegate next, CancellationToken ct)
    {
        // Check tool call before execution
        var check = await _guardrails.EvaluateToolCallAsync(tool.Name, input, ct);
        if (!check.IsAllowed)
            return ToolResult.Failed($"Blocked: {check.Reason}");

        var result = await next(tool, input, ctx, ct);

        // Check tool result after execution
        var resultCheck = await _guardrails.EvaluateToolResultAsync(tool.Name, result, ct);
        if (!resultCheck.IsAllowed)
            return ToolResult.Failed($"Result blocked: {resultCheck.Reason}");

        return result;
    }
}
```

## Pipeline Composition

Middleware is registered in order. The first registered middleware is the outermost wrapper:

```
Request → Telemetry → Retry → Guardrail → Agent → Response
                                                 ↓
Response ← Telemetry ← Retry ← Guardrail ← Agent ← Response
```

## Streaming Support

Both middleware interfaces provide a default streaming implementation that delegates to `next`. Override `InvokeStreamingAsync` for streaming-aware middleware:

```csharp
public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
    AgentTask task, IAgentContext ctx,
    StreamingAgentExecutionDelegate next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    using var activity = NexusTelemetry.ActivitySource.StartActivity("agent.stream");

    await foreach (var evt in next(task, ctx, ct))
    {
        // Inspect or transform events
        yield return evt;
    }
}
```
