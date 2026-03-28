# Observability — Nexus.Telemetry

> Assembly: `Nexus.Telemetry`  
> Deps: `Nexus.Core`, `OpenTelemetry`

## 1. Drei Säulen

| Säule | Technologie | Was wird erfasst |
|-------|------------|------------------|
| **Tracing** | `System.Diagnostics.Activity` + OpenTelemetry | Spans für Agent/Tool/MCP/A2A Execution |
| **Metrics** | `System.Diagnostics.Metrics` + OTel | Counters, Histograms, Gauges |
| **Logging** | `Microsoft.Extensions.Logging` + ILogger | Structured Logs mit Correlation |

## 2. Distributed Tracing

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

Fließt automatisch durch die gesamte Pipeline: Agent → Tools → Sub-Agents → MCP Calls → A2A Tasks.

### Automatische Spans

| Span | Tags | Beschreibung |
|------|------|-------------|
| `nexus.agent.execute` | agent.name, agent.id, model.id | Gesamte Agent-Ausführung |
| `nexus.agent.llm_call` | provider, model, input_tokens, output_tokens, cost | Einzelner LLM-Call |
| `nexus.agent.iteration` | iteration, max_iterations | Agent-Loop Iteration |
| `nexus.tool.execute` | tool.name, tool.is_destructive, duration_ms | Tool-Ausführung |
| `nexus.mcp.call` | server.name, tool.name | MCP Server Tool Call |
| `nexus.a2a.task` | remote.agent, task.status | A2A Task Lifecycle |
| `nexus.orchestration.node` | graph.id, node.id, agent.name | DAG Node Execution |
| `nexus.guardrail.evaluate` | guard.name, phase, is_allowed | Guardrail Evaluation |
| `nexus.checkpoint.save` | checkpoint.id, thread.id | Checkpoint speichern |

### Trace-Ansicht Beispiel

```
[nexus.orchestration.graph] graph=abc, duration=12.5s
  ├── [nexus.orchestration.node] node=research, agent=Researcher
  │     ├── [nexus.agent.execute] duration=8.2s
  │     │     ├── [nexus.agent.llm_call] model=claude-sonnet, tokens=1200, cost=$0.012
  │     │     ├── [nexus.tool.execute] tool=web_search, duration=2.1s
  │     │     ├── [nexus.agent.llm_call] model=claude-sonnet, tokens=800, cost=$0.008
  │     │     └── [nexus.guardrail.evaluate] guard=pii_redactor, allowed=true
  │     └── [nexus.checkpoint.save] checkpoint=cp-001
  └── [nexus.orchestration.node] node=review, agent=Reviewer
        └── [nexus.agent.execute] duration=4.1s
              └── [nexus.agent.llm_call] model=gpt-4o, tokens=600, cost=$0.006
```

## 3. Metrics

```csharp
public static class NexusMetrics
{
    // Counters
    public static readonly Counter<long> AgentExecutions;
    public static readonly Counter<long> AgentErrors;
    public static readonly Counter<long> ToolCalls;
    public static readonly Counter<long> ToolErrors;
    public static readonly Counter<long> GuardrailViolations;
    public static readonly Counter<long> CheckpointsSaved;
    public static readonly Counter<long> CheckpointsLoaded;

    // Histograms
    public static readonly Histogram<double> AgentLatencyMs;
    public static readonly Histogram<double> ToolLatencyMs;
    public static readonly Histogram<double> LlmLatencyMs;
    public static readonly Histogram<int> InputTokens;
    public static readonly Histogram<int> OutputTokens;
    public static readonly Histogram<double> CostPerRequest;

    // Gauges
    public static readonly ObservableGauge<int> ActiveAgents;
    public static readonly ObservableGauge<int> PendingTasks;
    public static readonly ObservableGauge<decimal> TotalBudgetRemaining;
    public static readonly ObservableGauge<int> McpConnections;
}
```

### Dashboards

Empfohlene Grafana-Panels:
- Agent Throughput (executions/min)
- P50/P95/P99 Agent Latency
- Token Usage by Provider
- Cost per Provider / per Agent
- Error Rate by Agent
- Guardrail Violation Rate
- Active Agents Gauge
- Budget Burn Rate

## 4. Structured Logging

```csharp
// Automatisch durch Middleware:
_logger.LogInformation(
    "Agent {AgentName} completed task {TaskId} in {DurationMs}ms " +
    "using {InputTokens} input + {OutputTokens} output tokens " +
    "with estimated cost ${Cost:F4}",
    agent.Name, task.Id, duration.TotalMilliseconds,
    usage.InputTokens, usage.OutputTokens, usage.EstimatedCost);

// Guardrail Violations:
_logger.LogWarning(
    "Guardrail {GuardName} blocked input for agent {AgentId}: {Reason}",
    guard.Name, agentId, result.Reason);
```

### Log Levels

| Level | Use Case |
|-------|----------|
| Trace | Token-by-Token streaming, raw prompts |
| Debug | Tool arguments, intermediate results |
| Information | Agent start/complete, tool calls, checkpoints |
| Warning | Guardrail violations, budget warnings (80%), retries |
| Error | Agent failures, tool errors, timeout |
| Critical | Budget exhausted, circuit breaker open, system-level failures |

## 5. IAuditLog

```csharp
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
    IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

public record AuditEntry(
    DateTimeOffset Timestamp, string Action, AgentId AgentId,
    string? UserId = null, string? CorrelationId = null,
    JsonElement? Details = null, AuditSeverity Severity = AuditSeverity.Info);
```

Audit-relevante Actions: `agent_spawned`, `agent_killed`, `tool_called`, `tool_blocked`, `approval_granted`, `approval_denied`, `budget_exceeded`, `checkpoint_saved`, `guardrail_violation`, `a2a_task_sent`, `secret_accessed`.

## 6. Registrierung

```csharp
n.AddTelemetry(t =>
{
    t.UseOpenTelemetry();
    t.ExportToOtlp("http://collector:4317");  // oder Jaeger, Zipkin
    t.ExportMetricsToPrometheus();
    t.EnableAgentTracing = true;
    t.EnableToolTracing = true;
    t.EnableMcpTracing = true;
    t.RedactPiiInTraces = true;  // PII aus Span-Attributen entfernen
});

n.AddAuditLog<PostgresAuditLog>();
```

## 7. Health Checks (Nexus.Hosting.AspNetCore)

```csharp
services.AddHealthChecks()
    .AddCheck<AgentPoolHealthCheck>("nexus-agents")
    .AddCheck<McpConnectionHealthCheck>("nexus-mcp")
    .AddCheck<BudgetHealthCheck>("nexus-budget")
    .AddCheck<CheckpointStoreHealthCheck>("nexus-checkpoints");

app.MapHealthChecks("/health");
```
