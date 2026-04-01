using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Nexus.Core.Agents;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;

namespace Nexus.Orchestration.Middleware;

public sealed partial class LoggingMiddleware : IAgentMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger) => _logger = logger;

    [LoggerMessage(1, LogLevel.Information, "Agent {AgentName} starting task {TaskId}")]
    private partial void LogStarting(string agentName, TaskId taskId);

    [LoggerMessage(2, LogLevel.Information, "Agent {AgentName} completed task {TaskId} in {ElapsedMs}ms with status {Status}")]
    private partial void LogCompleted(string agentName, TaskId taskId, long elapsedMs, AgentResultStatus status);

    [LoggerMessage(3, LogLevel.Error, "Agent {AgentName} failed task {TaskId} after {ElapsedMs}ms")]
    private partial void LogFailed(Exception ex, string agentName, TaskId taskId, long elapsedMs);

    [LoggerMessage(4, LogLevel.Information, "Agent {AgentName} starting streaming task {TaskId}")]
    private partial void LogStreamingStarting(string agentName, TaskId taskId);

    [LoggerMessage(5, LogLevel.Information, "Agent {AgentName} streaming completed {TaskId} in {ElapsedMs}ms")]
    private partial void LogStreamingCompleted(string agentName, TaskId taskId, long elapsedMs);

    [LoggerMessage(6, LogLevel.Error, "Agent {AgentName} streaming failed {TaskId} after {ElapsedMs}ms")]
    private partial void LogStreamingFailed(Exception ex, string agentName, TaskId taskId, long elapsedMs);

    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx, AgentExecutionDelegate next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        LogStarting(ctx.Agent.Name, task.Id);

        try
        {
            var result = await next(task, ctx, ct).ConfigureAwait(false);
            sw.Stop();
            LogCompleted(ctx.Agent.Name, task.Id, sw.ElapsedMilliseconds, result.Status);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogFailed(ex, ctx.Agent.Name, task.Id, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task, IAgentContext ctx, StreamingAgentExecutionDelegate next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        LogStreamingStarting(ctx.Agent.Name, task.Id);

        await foreach (var evt in next(task, ctx, ct))
        {
            if (evt is AgentCompletedEvent)
            {
                sw.Stop();
                LogStreamingCompleted(ctx.Agent.Name, task.Id, sw.ElapsedMilliseconds);
            }
            else if (evt is AgentFailedEvent failed)
            {
                sw.Stop();
                LogStreamingFailed(failed.Error, ctx.Agent.Name, task.Id, sw.ElapsedMilliseconds);
            }

            yield return evt;
        }
    }
}

public sealed class TimeoutMiddleware : IAgentMiddleware
{
    private readonly TimeSpan _timeout;

    public TimeoutMiddleware(TimeSpan timeout) => _timeout = timeout;

    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx, AgentExecutionDelegate next, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        try
        {
            return await next(task, ctx, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AgentResult.Timeout($"Task timed out after {_timeout.TotalSeconds}s");
        }
    }
}

public sealed class RetryMiddleware : IAgentMiddleware
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;

    public RetryMiddleware(int maxRetries = 3, TimeSpan? initialDelay = null)
    {
        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
    }

    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx, AgentExecutionDelegate next, CancellationToken ct)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var result = await next(task, ctx, ct).ConfigureAwait(false);
                if (result.Status == AgentResultStatus.Success || attempt == _maxRetries)
                    return result;

                lastException = new InvalidOperationException($"Agent returned status: {result.Status}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
            }

            if (attempt < _maxRetries)
            {
                var delay = _initialDelay * Math.Pow(2, attempt);
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)delay.TotalMilliseconds / 2));
                await Task.Delay(delay + jitter, ct).ConfigureAwait(false);
            }
        }

        return AgentResult.Failed($"All {_maxRetries + 1} attempts failed: {lastException?.Message}");
    }
}

public sealed class BudgetGuardMiddleware : IAgentMiddleware
{
    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx, AgentExecutionDelegate next, CancellationToken ct)
    {
        if (ctx.Budget is not null && !await ctx.Budget.HasBudgetAsync(ctx.Agent.Id, ct).ConfigureAwait(false))
            return AgentResult.BudgetExceeded("Budget exhausted");

        return await next(task, ctx, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task, IAgentContext ctx, StreamingAgentExecutionDelegate next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (ctx.Budget is not null && !await ctx.Budget.HasBudgetAsync(ctx.Agent.Id, ct).ConfigureAwait(false))
        {
            yield return new AgentCompletedEvent(ctx.Agent.Id,
                AgentResult.BudgetExceeded("Budget exhausted"));
            yield break;
        }

        await foreach (var evt in next(task, ctx, ct))
        {
            yield return evt;

            if (evt is TokenUsageEvent usage && ctx.Budget is not null)
            {
                await ctx.Budget.TrackUsageAsync(
                    ctx.Agent.Id, usage.InputTokens, usage.OutputTokens, usage.EstimatedCost, ct).ConfigureAwait(false);

                if (!await ctx.Budget.HasBudgetAsync(ctx.Agent.Id, ct).ConfigureAwait(false))
                {
                    var status = await ctx.Budget.GetStatusAsync(ctx.Agent.Id, ct).ConfigureAwait(false);
                    yield return new AgentCompletedEvent(ctx.Agent.Id,
                        AgentResult.BudgetExceeded(
                            "Budget exceeded during streaming",
                            new TokenUsageSummary(status.TotalInputTokens, status.TotalOutputTokens, status.TotalInputTokens + status.TotalOutputTokens),
                            status.TotalCost));
                    yield break;
                }
            }
        }
    }
}
