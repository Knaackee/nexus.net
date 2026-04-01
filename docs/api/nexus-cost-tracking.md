# Nexus.CostTracking API Reference

## Namespace: Nexus.CostTracking

### ICostTracker

```csharp
public interface ICostTracker
{
    Task RecordUsageAsync(string modelId, UsageSnapshot usage, CancellationToken ct = default);
    Task<CostTrackingSnapshot> GetSnapshotAsync(CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}
```

### IModelPricingProvider

```csharp
public interface IModelPricingProvider
{
    bool TryGetPricing(string? modelId, out ModelPricing pricing);
}
```

### CostTrackingOptions

```csharp
public sealed class CostTrackingOptions
{
    public Dictionary<string, ModelPricing> ModelPricing { get; }
    public CostTrackingOptions AddModel(string modelId, decimal input, decimal output, decimal cacheRead = 0, decimal cacheWrite = 0);
}
```

### ModelPricing

```csharp
public sealed record ModelPricing(
    decimal InputPerMillionTokens,
    decimal OutputPerMillionTokens,
    decimal CacheReadPerMillionTokens = 0,
    decimal CacheWritePerMillionTokens = 0);
```

### UsageSnapshot

```csharp
public sealed record UsageSnapshot(
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens = 0,
    int CacheWriteInputTokens = 0,
    int TotalTokens = 0);
```

### CostTrackingSnapshot

```csharp
public sealed record CostTrackingSnapshot(
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalCacheReadInputTokens,
    int TotalCacheWriteInputTokens,
    int TotalTokens,
    decimal TotalCost,
    bool HasUnknownPricing,
    IReadOnlyDictionary<string, ModelUsageSnapshot> Models);
```

### ModelUsageSnapshot

```csharp
public sealed record ModelUsageSnapshot(
    string ModelId,
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens,
    int CacheWriteInputTokens,
    int TotalTokens,
    decimal TotalCost,
    int Requests);
```

### CostTrackingChatClient

```csharp
public sealed class CostTrackingChatClient : IChatClient
```

Wraps an existing `IChatClient`, extracts usage metadata from normal and streaming responses, and forwards aggregated totals to `ICostTracker`.

When used together with `AddOrchestration(o => o.UseDefaults())`, the tracked usage also feeds the default `IBudgetTracker`, enabling `AgentBudget.MaxCostUsd`, `MaxInputTokens`, and `MaxOutputTokens` enforcement.

### Builder Extensions

```csharp
services.AddNexus(nexus =>
{
    nexus.AddCostTracking(c => c.AddModel("gpt-4o", input: 2.50m, output: 10.00m));
});
```