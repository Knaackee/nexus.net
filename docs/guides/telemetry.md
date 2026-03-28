# Telemetry

OpenTelemetry-based tracing and metrics for agent execution, tool calls, guardrails, and LLM interactions.

## NexusTelemetry

The `Nexus.Telemetry` package provides pre-defined `ActivitySource` and `Meter` instances:

```csharp
public static class NexusTelemetry
{
    public const string ServiceName = "Nexus";

    public static readonly ActivitySource ActivitySource = new(ServiceName, "0.1.0");
    public static readonly Meter Meter = new(ServiceName, "0.1.0");
}
```

## Metrics

### Counters

| Metric | Unit | Description |
|--------|------|-------------|
| `nexus.agent.executions` | executions | Total agent executions |
| `nexus.agent.errors` | errors | Total agent errors |
| `nexus.tool.calls` | calls | Total tool calls |
| `nexus.tool.errors` | errors | Total tool errors |
| `nexus.guardrail.violations` | violations | Total guardrail violations |
| `nexus.checkpoint.saved` | checkpoints | Total checkpoints saved |
| `nexus.checkpoint.loaded` | checkpoints | Total checkpoints loaded |

### Histograms

| Metric | Unit | Description |
|--------|------|-------------|
| `nexus.agent.latency` | ms | Agent execution latency |
| `nexus.tool.latency` | ms | Tool execution latency |
| `nexus.llm.latency` | ms | LLM call latency |
| `nexus.llm.input_tokens` | tokens | Input tokens per call |
| `nexus.llm.output_tokens` | tokens | Output tokens per call |
| `nexus.cost.per_request` | USD | Cost per request |

## Tracing

The `ActivitySource` creates spans for:

- Agent execution (parent span)
- Individual LLM calls (child spans)
- Tool invocations (child spans)
- Guardrail evaluations
- Checkpoint operations
- Orchestration graph traversal

## Configuration

```csharp
services.AddNexus(nexus =>
{
    nexus.AddTelemetry(t =>
    {
        // Configure telemetry export, sampling, etc.
    });
});
```

### OpenTelemetry Integration

Wire up with the standard OpenTelemetry SDK:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(NexusTelemetry.ServiceName);
        tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(NexusTelemetry.ServiceName);
        metrics.AddOtlpExporter();
    });
```

### Console Export (Development)

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource("Nexus")
        .AddConsoleExporter())
    .WithMetrics(m => m
        .AddMeter("Nexus")
        .AddConsoleExporter());
```

## Audit Logging

The `IAuditLog` interface provides structured audit trails:

```csharp
services.AddNexus(nexus =>
{
    nexus.AddAuditLog<FileAuditLog>();
});
```

Implement `IAuditLog` for custom audit backends:

```csharp
public class FileAuditLog : IAuditLog
{
    public Task LogAsync(AuditEntry entry, CancellationToken ct)
    {
        // Write to file, database, etc.
    }
}
```

## Using Metrics Programmatically

```csharp
// Record agent execution
NexusTelemetry.AgentExecutions.Add(1, new KeyValuePair<string, object?>("agent.name", "researcher"));
NexusTelemetry.AgentLatencyMs.Record(elapsed.TotalMilliseconds);

// Record tool call
NexusTelemetry.ToolCalls.Add(1, new KeyValuePair<string, object?>("tool.name", "web_search"));

// Record LLM usage
NexusTelemetry.InputTokens.Record(promptTokens);
NexusTelemetry.OutputTokens.Record(completionTokens);
NexusTelemetry.CostPerRequest.Record(0.003);
```
