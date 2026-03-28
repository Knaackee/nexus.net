using Nexus.Core.Agents;

namespace Nexus.Core.Contracts;

public interface IBudgetTracker
{
    Task TrackUsageAsync(AgentId agentId, int inputTokens, int outputTokens,
        decimal? cost, CancellationToken ct = default);
    Task<BudgetStatus> GetStatusAsync(AgentId agentId, CancellationToken ct = default);
    Task<bool> HasBudgetAsync(AgentId agentId, CancellationToken ct = default);
}

public record BudgetStatus(
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalCost,
    AgentBudget? Limit,
    bool IsExhausted);
