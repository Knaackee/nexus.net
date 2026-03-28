using Nexus.Core.Agents;

namespace Nexus.Orchestration;

public record OrchestrationResult
{
    public required OrchestrationStatus Status { get; init; }
    public required IReadOnlyDictionary<TaskId, AgentResult> TaskResults { get; init; }
    public TimeSpan Duration { get; init; }
    public TokenUsageSummary? TokenUsage { get; init; }
    public CostSummary? Cost { get; init; }
    public int CheckpointCount { get; init; }
}

public enum OrchestrationStatus { Completed, PartiallyCompleted, Failed, Cancelled, Timeout }

public record CostSummary(decimal TotalUsd, IReadOnlyDictionary<string, decimal> PerProvider);
