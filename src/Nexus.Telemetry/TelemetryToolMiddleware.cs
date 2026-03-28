using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Telemetry;

/// <summary>
/// Tool middleware that creates OpenTelemetry spans and records metrics for tool execution.
/// </summary>
public sealed partial class TelemetryToolMiddleware : IToolMiddleware
{
    private readonly ILogger<TelemetryToolMiddleware> _logger;

    public TelemetryToolMiddleware(ILogger<TelemetryToolMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task<ToolResult> InvokeAsync(
        ITool tool,
        JsonElement input,
        IToolContext ctx,
        ToolExecutionDelegate next,
        CancellationToken ct = default)
    {
        using var activity = NexusTelemetry.ActivitySource.StartActivity("nexus.tool.execute");
        activity?.SetTag("tool.name", tool.Name);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        NexusTelemetry.ToolCalls.Add(1, new KeyValuePair<string, object?>("tool.name", tool.Name));

        try
        {
            var result = await next(tool, input, ctx, ct);
            sw.Stop();

            NexusTelemetry.ToolLatencyMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("tool.name", tool.Name));

            activity?.SetTag("tool.success", result.IsSuccess);
            if (!result.IsSuccess)
            {
                NexusTelemetry.ToolErrors.Add(1, new KeyValuePair<string, object?>("tool.name", tool.Name));
            }

            LogToolCompleted(_logger, tool.Name, sw.Elapsed.TotalMilliseconds, result.IsSuccess);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            NexusTelemetry.ToolErrors.Add(1, new KeyValuePair<string, object?>("tool.name", tool.Name));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogToolFailed(_logger, tool.Name, ex.Message);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool {ToolName} completed in {DurationMs:F1}ms (success: {IsSuccess})")]
    private static partial void LogToolCompleted(ILogger logger, string toolName, double durationMs, bool isSuccess);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {ToolName} failed: {Error}")]
    private static partial void LogToolFailed(ILogger logger, string toolName, string error);
}
