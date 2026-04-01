using System.Text.Json;

namespace Nexus.Core.Agents;

public record AgentResult
{
    public required AgentResultStatus Status { get; init; }
    public string? Text { get; init; }
    public JsonElement? StructuredOutput { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    public TokenUsageSummary? TokenUsage { get; init; }
    public decimal? EstimatedCost { get; init; }

    public static AgentResult Success(
        string text,
        TokenUsageSummary? tokenUsage = null,
        decimal? estimatedCost = null) => new()
    {
        Status = AgentResultStatus.Success,
        Text = text,
        TokenUsage = tokenUsage,
        EstimatedCost = estimatedCost,
    };

    public static AgentResult Failed(
        string reason,
        TokenUsageSummary? tokenUsage = null,
        decimal? estimatedCost = null) => new()
    {
        Status = AgentResultStatus.Failed,
        Text = reason,
        TokenUsage = tokenUsage,
        EstimatedCost = estimatedCost,
    };

    public static AgentResult Cancelled() => new()
    {
        Status = AgentResultStatus.Cancelled
    };

    public static AgentResult Timeout(
        string reason,
        TokenUsageSummary? tokenUsage = null,
        decimal? estimatedCost = null) => new()
    {
        Status = AgentResultStatus.Timeout,
        Text = reason,
        TokenUsage = tokenUsage,
        EstimatedCost = estimatedCost,
    };

    public static AgentResult BudgetExceeded(
        string reason,
        TokenUsageSummary? tokenUsage = null,
        decimal? estimatedCost = null) => new()
    {
        Status = AgentResultStatus.BudgetExceeded,
        Text = reason,
        TokenUsage = tokenUsage,
        EstimatedCost = estimatedCost,
    };
}

public enum AgentResultStatus
{
    Success,
    Failed,
    Cancelled,
    Timeout,
    BudgetExceeded
}

public record TokenUsageSummary(int TotalInputTokens, int TotalOutputTokens, int TotalTokens)
{
    public TokenUsageSummary() : this(0, 0, 0) { }
}
