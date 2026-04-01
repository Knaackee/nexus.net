namespace Nexus.CostTracking;

public sealed record ModelPricing(
    decimal InputPerMillionTokens,
    decimal OutputPerMillionTokens,
    decimal CacheReadPerMillionTokens = 0,
    decimal CacheWritePerMillionTokens = 0)
{
    public decimal Calculate(UsageSnapshot usage)
    {
        var normalized = usage.NormalizeTotal();
        return ((normalized.InputTokens / 1_000_000m) * InputPerMillionTokens)
            + ((normalized.OutputTokens / 1_000_000m) * OutputPerMillionTokens)
            + ((normalized.CacheReadInputTokens / 1_000_000m) * CacheReadPerMillionTokens)
            + ((normalized.CacheWriteInputTokens / 1_000_000m) * CacheWritePerMillionTokens);
    }
}