# Nexus.AgentLoop API Reference

`Nexus.AgentLoop` is the session-aware execution loop for multi-turn agents.

Use it when one user interaction can trigger multiple model turns, tool calls, approvals, and compaction cycles before the runtime should stop.

Do not use it as your first abstraction if you only need one direct agent call or a purely structural DAG. In those cases start with `Nexus.Orchestration`, and add the loop only when turn-by-turn runtime behavior matters.

## Key Types

### `IAgentLoop`

The main execution surface.

```csharp
public interface IAgentLoop
{
    IAsyncEnumerable<AgentLoopEvent> RunAsync(AgentLoopOptions options, CancellationToken ct = default);
}
```

### `AgentLoopOptions`

The runtime contract for a loop invocation.

Important fields:

- `Agent` or `AgentDefinition`
- `RoutingStrategy`
- `Messages` and `UserInput`
- `Budget`
- `MaxTurns`
- `ContextWindow`
- `StopWhen`
- `SessionId`, `ResumeLastSession`, `SessionTitle`, `SessionMetadata`

### `AgentLoopEvent`

The event stream emitted during execution.

Notable events:

- `LoopStartedEvent`
- `TextChunkLoopEvent`
- `ReasoningChunkLoopEvent`
- `ToolCallStartedLoopEvent`
- `ToolCallProgressLoopEvent`
- `ToolCallCompletedLoopEvent`
- `ApprovalRequestedLoopEvent`
- `UserInputRequestedLoopEvent`
- `CompactionTriggeredLoopEvent`
- `TokenUsageLoopEvent`
- `TurnCompletedLoopEvent`
- `LoopCompletedEvent`
- `LoopErrorEvent`

### `LoopStopReason`

Why the loop stopped:

- `AgentCompleted`
- `MaxTurnsReached`
- `BudgetExhausted`
- `UserCancelled`
- `StepRejected`
- `StopConditionMet`
- `CompactionFailed`
- `Error`

### `IRoutingStrategy`

Controls how the loop selects the next step or task shape across turns.

`WorkflowRoutingStrategy` is the main built-in bridge when loop execution is used for staged workflow-style interaction.

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddAgentLoop(agentLoop =>
    {
        agentLoop.UseDefaults();
    });
});
```

`Nexus.Defaults` already includes this registration.

## Typical Use

Use `Nexus.AgentLoop` for:

- multi-turn chat sessions
- tool-using agents that may call several tools before responding
- interactive approvals inside one request lifecycle
- loop execution that must compact history and continue safely

## Related Packages

- `Nexus.Sessions` for transcript persistence
- `Nexus.Compaction` for context-window management
- `Nexus.Tools.Standard` for tool execution
- `Nexus.Permissions` for approvals
- `Nexus.Skills` for prompt/tool injection

## Related Docs

- [Agent Loop](../llms/agent-loop.md)
- [Sub-Agents Guide](../guides/sub-agents.md)
- [Memory Guide](../guides/memory.md)