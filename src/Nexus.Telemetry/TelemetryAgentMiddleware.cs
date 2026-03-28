using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nexus.Core.Agents;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;

namespace Nexus.Telemetry;

/// <summary>
/// Agent middleware that creates OpenTelemetry spans for agent execution.
/// </summary>
public sealed partial class TelemetryAgentMiddleware : IAgentMiddleware
{
    private readonly ILogger<TelemetryAgentMiddleware> _logger;

    public TelemetryAgentMiddleware(ILogger<TelemetryAgentMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task<AgentResult> InvokeAsync(
        AgentTask task,
        IAgentContext ctx,
        AgentExecutionDelegate next,
        CancellationToken ct = default)
    {
        using var activity = NexusTelemetry.ActivitySource.StartActivity("nexus.agent.execute");
        activity?.SetTag("agent.task_id", task.Id.Value.ToString());
        activity?.SetTag("agent.description", task.Description);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        NexusTelemetry.AgentExecutions.Add(1);

        try
        {
            var result = await next(task, ctx, ct);
            sw.Stop();

            activity?.SetTag("agent.status", result.Status.ToString());
            NexusTelemetry.AgentLatencyMs.Record(sw.Elapsed.TotalMilliseconds);

            if (result.TokenUsage is not null)
            {
                NexusTelemetry.InputTokens.Record(result.TokenUsage.TotalInputTokens);
                NexusTelemetry.OutputTokens.Record(result.TokenUsage.TotalOutputTokens);
            }

            if (result.EstimatedCost.HasValue)
            {
                NexusTelemetry.CostPerRequest.Record((double)result.EstimatedCost.Value);
            }

            var status = result.Status.ToString();
            LogAgentCompleted(_logger, task.Id.Value, sw.Elapsed.TotalMilliseconds, status);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            NexusTelemetry.AgentErrors.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogAgentFailed(_logger, task.Id.Value, ex.Message);
            throw;
        }
    }

    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task,
        IAgentContext ctx,
        StreamingAgentExecutionDelegate next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = NexusTelemetry.ActivitySource.StartActivity("nexus.agent.execute.streaming");
        activity?.SetTag("agent.task_id", task.Id.Value.ToString());

        NexusTelemetry.AgentExecutions.Add(1);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await foreach (var evt in next(task, ctx, ct))
        {
            if (evt is AgentCompletedEvent completed)
            {
                sw.Stop();
                NexusTelemetry.AgentLatencyMs.Record(sw.Elapsed.TotalMilliseconds);
                activity?.SetTag("agent.status", completed.Result.Status.ToString());
            }

            yield return evt;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent task {TaskId} completed in {DurationMs:F1}ms with status {Status}")]
    private static partial void LogAgentCompleted(ILogger logger, Guid taskId, double durationMs, string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent task {TaskId} failed: {Error}")]
    private static partial void LogAgentFailed(ILogger logger, Guid taskId, string error);
}
