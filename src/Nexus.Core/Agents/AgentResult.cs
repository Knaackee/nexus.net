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

    public static AgentResult Success(string text) => new()
    {
        Status = AgentResultStatus.Success,
        Text = text
    };

    public static AgentResult Failed(string reason) => new()
    {
        Status = AgentResultStatus.Failed,
        Text = reason
    };

    public static AgentResult Cancelled() => new()
    {
        Status = AgentResultStatus.Cancelled
    };

    public static AgentResult Timeout(string reason) => new()
    {
        Status = AgentResultStatus.Timeout,
        Text = reason
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
