namespace Nexus.CostTracking;

public sealed record UsageSnapshot(
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens = 0,
    int CacheWriteInputTokens = 0,
    int TotalTokens = 0)
{
    public static UsageSnapshot Empty { get; } = new(0, 0, 0, 0, 0);

    public UsageSnapshot NormalizeTotal()
        => this with { TotalTokens = TotalTokens > 0 ? TotalTokens : InputTokens + OutputTokens + CacheReadInputTokens + CacheWriteInputTokens };

    public UsageSnapshot MergePreferLargest(UsageSnapshot other)
        => new UsageSnapshot(
            Math.Max(InputTokens, other.InputTokens),
            Math.Max(OutputTokens, other.OutputTokens),
            Math.Max(CacheReadInputTokens, other.CacheReadInputTokens),
            Math.Max(CacheWriteInputTokens, other.CacheWriteInputTokens),
            Math.Max(TotalTokens, other.TotalTokens)).NormalizeTotal();
}