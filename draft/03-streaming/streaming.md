# Streaming Architecture — Nexus.Core + Nexus.Orchestration

## 1. Prinzip

**Buffered ist ein Wrapper um Streaming. Nie umgekehrt.**

Jedes produktive Interface bietet eine Dual-API nach dem `IChatClient`-Vorbild von Microsoft.Extensions.AI:

```csharp
// Pattern überall gleich:
Task<TResult> ExecuteAsync(...);                    // Buffered — Convenience
IAsyncEnumerable<TEvent> ExecuteStreamingAsync(...); // Streaming — Primärer Pfad
```

## 2. Event-Hierarchie

### AgentEvents

```csharp
public abstract record AgentEvent(AgentId AgentId, DateTimeOffset Timestamp);

// LLM Streaming
public record TextChunkEvent(AgentId AgentId, string Text) : AgentEvent(...);
public record ReasoningChunkEvent(AgentId AgentId, string Text) : AgentEvent(...);

// Tool Lifecycle
public record ToolCallStartedEvent(AgentId AgentId, string ToolCallId, string ToolName, JsonElement Arguments) : AgentEvent(...);
public record ToolCallProgressEvent(AgentId AgentId, string ToolCallId, string Message, double? Progress) : AgentEvent(...);
public record ToolCallCompletedEvent(AgentId AgentId, string ToolCallId, ToolResult Result) : AgentEvent(...);

// Agent Lifecycle
public record AgentStateChangedEvent(AgentId AgentId, AgentState OldState, AgentState NewState) : AgentEvent(...);
public record AgentIterationEvent(AgentId AgentId, int Iteration, int MaxIterations) : AgentEvent(...);

// Human-in-the-Loop
public record ApprovalRequestedEvent(AgentId AgentId, string ApprovalId, string Description) : AgentEvent(...);

// Sub-Agents
public record SubAgentSpawnedEvent(AgentId AgentId, AgentId ChildAgentId, string ChildName) : AgentEvent(...);

// Cost
public record TokenUsageEvent(AgentId AgentId, int InputTokens, int OutputTokens, decimal? EstimatedCost) : AgentEvent(...);

// Completion
public record AgentCompletedEvent(AgentId AgentId, AgentResult Result) : AgentEvent(...);
public record AgentFailedEvent(AgentId AgentId, Exception Error) : AgentEvent(...);
```

### ToolEvents

```csharp
public abstract record ToolEvent(string ToolName, DateTimeOffset Timestamp);

public record ToolProgressEvent(string ToolName, string Message, double? ProgressPercent) : ToolEvent(...);
public record ToolLogEvent(string ToolName, string LogLine, LogLevel Level) : ToolEvent(...);
public record ToolPartialResultEvent(string ToolName, JsonElement PartialResult) : ToolEvent(...);
public record ToolCompletedEvent(string ToolName, ToolResult Result) : ToolEvent(...);
```

### OrchestrationEvents

```csharp
public abstract record OrchestrationEvent(TaskGraphId GraphId, DateTimeOffset Timestamp);

public record NodeStartedEvent(TaskGraphId GraphId, TaskId NodeId, AgentId AgentId) : OrchestrationEvent(...);
public record NodeCompletedEvent(TaskGraphId GraphId, TaskId NodeId, AgentResult Result) : OrchestrationEvent(...);
public record NodeFailedEvent(TaskGraphId GraphId, TaskId NodeId, Exception Error) : OrchestrationEvent(...);
public record NodeSkippedEvent(TaskGraphId GraphId, TaskId NodeId, string Reason) : OrchestrationEvent(...);
public record AgentEventInGraph(TaskGraphId GraphId, TaskId NodeId, AgentEvent InnerEvent) : OrchestrationEvent(...);
public record CheckpointCreatedEvent(TaskGraphId GraphId, CheckpointId CheckpointId) : OrchestrationEvent(...);
public record OrchestrationCompletedEvent(TaskGraphId GraphId, OrchestrationResult Result) : OrchestrationEvent(...);
```

## 3. Streaming Middleware

```csharp
public interface IAgentMiddleware
{
    // Buffered
    Task<AgentResult> InvokeAsync(AgentTask task, IAgentContext ctx, AgentExecutionDelegate next, CancellationToken ct);

    // Streaming — Default: Pass-through
    virtual IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task, IAgentContext ctx, StreamingAgentExecutionDelegate next, CancellationToken ct)
        => next(task, ctx, ct);
}
```

### Beispiel: Budget Guard mit Stream-Abbruch

```csharp
public class BudgetGuardMiddleware : IAgentMiddleware
{
    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task, IAgentContext ctx, StreamingAgentExecutionDelegate next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        decimal spent = 0;
        var budget = ctx.Budget?.MaxCostUsd ?? decimal.MaxValue;

        await foreach (var evt in next(task, ctx, ct))
        {
            if (evt is TokenUsageEvent usage && usage.EstimatedCost.HasValue)
            {
                spent += usage.EstimatedCost.Value;
                if (spent > budget)
                {
                    yield return new AgentFailedEvent(ctx.Agent.Id,
                        new BudgetExceededException($"Budget ${budget} exceeded"));
                    yield break;
                }
            }
            yield return evt;
        }
    }
}
```

## 4. Fan-In: Parallele Agents mergen

```csharp
public static class StreamMerger
{
    public static async IAsyncEnumerable<OrchestrationEvent> MergeParallelAsync(
        TaskGraphId graphId,
        IReadOnlyList<(TaskId NodeId, IAgent Agent, AgentTask Task, IAgentContext Context)> work,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<OrchestrationEvent>(
            new UnboundedChannelOptions { SingleReader = true });

        var tasks = work.Select(async w =>
        {
            try
            {
                await foreach (var evt in w.Agent.ExecuteStreamingAsync(w.Task, w.Context, ct))
                    await channel.Writer.WriteAsync(new AgentEventInGraph(graphId, w.NodeId, evt), ct);
            }
            catch (Exception ex)
            {
                await channel.Writer.WriteAsync(new NodeFailedEvent(graphId, w.NodeId, ex), ct);
            }
        }).ToArray();

        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.Complete());

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            yield return evt;
    }
}
```

## 5. Consumer Convenience Extensions

```csharp
public static class StreamingExtensions
{
    // Nur Text
    public static IAsyncEnumerable<string> TextChunksOnly(this IAsyncEnumerable<AgentEvent> events)
        => events.OfType<TextChunkEvent>().Select(e => e.Text);

    // Auf Ergebnis warten
    public static async Task<AgentResult> ToResultAsync(
        this IAsyncEnumerable<AgentEvent> events, CancellationToken ct = default) { ... }

    // Text sammeln
    public static async Task<string> CollectTextAsync(
        this IAsyncEnumerable<AgentEvent> events, CancellationToken ct = default) { ... }

    // Stream duplizieren
    public static (IAsyncEnumerable<T>, IAsyncEnumerable<T>) Tee<T>(this IAsyncEnumerable<T> source) { ... }

    // Side-Effects
    public static IAsyncEnumerable<AgentEvent> WithSideEffect(
        this IAsyncEnumerable<AgentEvent> events, Action<AgentEvent> sideEffect) { ... }
}
```

## 6. Event-Topologie

```
Agent.ExecuteStreamingAsync()
  → AgentMiddleware Pipeline (streaming)
    → IChatClient.GetStreamingResponseAsync()  → TextChunkEvents
    → ITool.ExecuteStreamingAsync()             → ToolEvents
  → IOrchestrator (mergt via Channel<T>)        → OrchestrationEvents
    → AgUiEventBridge                           → AG-UI SSE/WebSocket → Frontend
```
