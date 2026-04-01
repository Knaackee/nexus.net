namespace Nexus.CostTracking;

public sealed record ModelUsageSnapshot(
    string ModelId,
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens,
    int CacheWriteInputTokens,
    int TotalTokens,
    decimal TotalCost,
    int Requests);

public sealed record CostTrackingSnapshot(
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalCacheReadInputTokens,
    int TotalCacheWriteInputTokens,
    int TotalTokens,
    decimal TotalCost,
    bool HasUnknownPricing,
    IReadOnlyDictionary<string, ModelUsageSnapshot> Models);