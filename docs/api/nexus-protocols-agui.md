# Nexus.Protocols.AgUi API Reference

`Nexus.Protocols.AgUi` bridges Nexus runtime events into AG-UI frontend events.

Use it when a frontend needs a stable event stream for transcripts, reasoning, tool activity, user interaction requests, step progress, and final run completion.

## Key Types

### `AgUiEvent`

Base record for all AG-UI events.

### Event Shapes

- `AgUiRunStartedEvent`
- `AgUiRunFinishedEvent`
- `AgUiTextChunkEvent`
- `AgUiReasoningChunkEvent`
- `AgUiToolCallStartEvent`
- `AgUiToolCallEndEvent`
- `AgUiApprovalRequestedEvent`
- `AgUiUserInputRequestEvent`
- `AgUiStateDeltaEvent`
- `AgUiStateSnapshotEvent`
- `AgUiStepStartedEvent`
- `AgUiStepFinishedEvent`
- `AgUiCustomEvent`
- `AgUiErrorEvent`

### `AgUiEventBridge`

Converts Nexus orchestration events into AG-UI events.

```csharp
public sealed class AgUiEventBridge
{
    public static async IAsyncEnumerable<AgUiEvent> BridgeAsync(
        IAsyncEnumerable<OrchestrationEvent> events,
        CancellationToken ct = default)
}
```

## Typical Use

`Nexus.Hosting.AspNetCore` is the usual host for this package. It maps the AG-UI endpoint and streams bridged events to the frontend.

## When To Use It

- a browser UI should display live agent output and tool activity
- a browser UI should render reasoning separately from final answer text
- a browser UI should react to approval requests and ask-user prompts as structured events
- orchestration events need a frontend-friendly event contract
- SSE-based progressive rendering is part of the UX

## Related Docs

- [Protocols Guide](../guides/protocols.md)
- [Nexus.Hosting.AspNetCore](nexus-hosting-aspnetcore.md)
- [Nexus.Protocols Overview](nexus-protocols.md)