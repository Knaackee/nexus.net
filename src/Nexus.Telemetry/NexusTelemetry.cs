using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Nexus.Telemetry;

/// <summary>
/// Central telemetry definitions for the Nexus framework.
/// Provides ActivitySource for tracing and Meter for metrics.
/// </summary>
public static class NexusTelemetry
{
    public const string ServiceName = "Nexus";
    public const string Version = "0.1.0";

    public static readonly ActivitySource ActivitySource = new(ServiceName, Version);

    public static readonly Meter Meter = new(ServiceName, Version);

    // ── Counters ──
    public static readonly Counter<long> AgentExecutions =
        Meter.CreateCounter<long>("nexus.agent.executions", "executions", "Total agent executions");

    public static readonly Counter<long> AgentErrors =
        Meter.CreateCounter<long>("nexus.agent.errors", "errors", "Total agent errors");

    public static readonly Counter<long> ToolCalls =
        Meter.CreateCounter<long>("nexus.tool.calls", "calls", "Total tool calls");

    public static readonly Counter<long> ToolErrors =
        Meter.CreateCounter<long>("nexus.tool.errors", "errors", "Total tool errors");

    public static readonly Counter<long> GuardrailViolations =
        Meter.CreateCounter<long>("nexus.guardrail.violations", "violations", "Total guardrail violations");

    public static readonly Counter<long> CheckpointsSaved =
        Meter.CreateCounter<long>("nexus.checkpoint.saved", "checkpoints", "Total checkpoints saved");

    public static readonly Counter<long> CheckpointsLoaded =
        Meter.CreateCounter<long>("nexus.checkpoint.loaded", "checkpoints", "Total checkpoints loaded");

    // ── Histograms ──
    public static readonly Histogram<double> AgentLatencyMs =
        Meter.CreateHistogram<double>("nexus.agent.latency", "ms", "Agent execution latency");

    public static readonly Histogram<double> ToolLatencyMs =
        Meter.CreateHistogram<double>("nexus.tool.latency", "ms", "Tool execution latency");

    public static readonly Histogram<double> LlmLatencyMs =
        Meter.CreateHistogram<double>("nexus.llm.latency", "ms", "LLM call latency");

    public static readonly Histogram<int> InputTokens =
        Meter.CreateHistogram<int>("nexus.llm.input_tokens", "tokens", "Input tokens per call");

    public static readonly Histogram<int> OutputTokens =
        Meter.CreateHistogram<int>("nexus.llm.output_tokens", "tokens", "Output tokens per call");

    public static readonly Histogram<double> CostPerRequest =
        Meter.CreateHistogram<double>("nexus.cost.per_request", "USD", "Cost per request");
}
