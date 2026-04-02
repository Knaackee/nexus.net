# Nexus.Telemetry API Reference

`Nexus.Telemetry` provides tracing and metrics instrumentation for Nexus runtimes.

Use it when you need visibility into agent executions, tool calls, token flow, latency, cost, and checkpoint activity.

## Key Types

### `NexusTelemetry`

Central definitions for the telemetry surface.

It exposes:

- `ActivitySource`
- `Meter`
- counters such as `AgentExecutions`, `AgentErrors`, `ToolCalls`, `ToolErrors`, `GuardrailViolations`, `CheckpointsSaved`, `CheckpointsLoaded`
- histograms such as `AgentLatencyMs`, `ToolLatencyMs`, `LlmLatencyMs`, `InputTokens`, `OutputTokens`, `CostPerRequest`

### `TelemetryAgentMiddleware`

Agent pipeline instrumentation.

### `TelemetryToolMiddleware`

Tool execution instrumentation.

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddTelemetry(telemetry =>
    {
        telemetry.UseOpenTelemetry();
    });
});
```

## When To Use It

- production hosts need traces and metrics
- benchmarks and runtime behavior should be correlated with telemetry
- token and cost visibility should feed operational monitoring

## Related Docs

- [Telemetry Guide](../guides/telemetry.md)
- [Performance And Benchmarking](../guides/performance-and-benchmarking.md)