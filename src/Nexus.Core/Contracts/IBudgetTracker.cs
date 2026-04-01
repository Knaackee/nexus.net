using Nexus.Core.Agents;

namespace Nexus.Core.Contracts;

public interface IBudgetTracker
{
    Task TrackUsageAsync(AgentId agentId, int inputTokens, int outputTokens,
        decimal? cost, CancellationToken ct = default);
    Task<BudgetStatus> GetStatusAsync(AgentId agentId, CancellationToken ct = default);
    Task<bool> HasBudgetAsync(AgentId agentId, CancellationToken ct = default);
    Task SetLimitAsync(AgentId agentId, AgentBudget? limit, CancellationToken ct = default) => Task.CompletedTask;
    Task ClearAsync(AgentId agentId, CancellationToken ct = default) => Task.CompletedTask;
}

public record BudgetStatus(
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalCost,
    AgentBudget? Limit,
    bool IsExhausted);
