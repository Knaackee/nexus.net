using System.Collections.Concurrent;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;

namespace Nexus.CostTracking;

public sealed class DefaultBudgetTracker : IBudgetTracker
{
    private readonly ConcurrentDictionary<AgentId, BudgetEntry> _entries = new();

    public Task TrackUsageAsync(AgentId agentId, int inputTokens, int outputTokens, decimal? cost, CancellationToken ct = default)
    {
        _entries.AddOrUpdate(
            agentId,
            _ => new BudgetEntry(inputTokens, outputTokens, cost ?? 0m, null),
            (_, existing) => existing with
            {
                TotalInputTokens = existing.TotalInputTokens + inputTokens,
                TotalOutputTokens = existing.TotalOutputTokens + outputTokens,
                TotalCost = existing.TotalCost + (cost ?? 0m),
            });

        return Task.CompletedTask;
    }

    public Task<BudgetStatus> GetStatusAsync(AgentId agentId, CancellationToken ct = default)
    {
        _entries.TryGetValue(agentId, out var entry);
        entry ??= new BudgetEntry(0, 0, 0m, null);

        return Task.FromResult(new BudgetStatus(
            entry.TotalInputTokens,
            entry.TotalOutputTokens,
            entry.TotalCost,
            entry.Limit,
            IsExhausted(entry)));
    }

    public async Task<bool> HasBudgetAsync(AgentId agentId, CancellationToken ct = default)
        => !(await GetStatusAsync(agentId, ct).ConfigureAwait(false)).IsExhausted;

    public Task SetLimitAsync(AgentId agentId, AgentBudget? limit, CancellationToken ct = default)
    {
        _entries.AddOrUpdate(
            agentId,
            _ => new BudgetEntry(0, 0, 0m, limit),
            (_, existing) => existing with { Limit = limit });

        return Task.CompletedTask;
    }

    public Task ClearAsync(AgentId agentId, CancellationToken ct = default)
    {
        _entries.TryRemove(agentId, out _);
        return Task.CompletedTask;
    }

    private static bool IsExhausted(BudgetEntry entry)
    {
        var limit = entry.Limit;
        if (limit is null)
            return false;

        if (limit.MaxInputTokens is int maxInput && entry.TotalInputTokens >= maxInput)
            return true;

        if (limit.MaxOutputTokens is int maxOutput && entry.TotalOutputTokens >= maxOutput)
            return true;

        if (limit.MaxCostUsd is decimal maxCost && entry.TotalCost >= maxCost)
            return true;

        return false;
    }

    private sealed record BudgetEntry(
        int TotalInputTokens,
        int TotalOutputTokens,
        decimal TotalCost,
        AgentBudget? Limit);
}