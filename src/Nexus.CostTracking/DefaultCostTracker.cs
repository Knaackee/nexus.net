using System.Collections.Concurrent;

namespace Nexus.CostTracking;

public sealed class DefaultCostTracker : ICostTracker
{
    private readonly IModelPricingProvider _pricingProvider;
    private readonly ConcurrentDictionary<string, AggregateModelUsage> _models = new(StringComparer.OrdinalIgnoreCase);
    private int _totalInputTokens;
    private int _totalOutputTokens;
    private int _totalCacheReadInputTokens;
    private int _totalCacheWriteInputTokens;
    private int _totalTokens;
    private long _totalCostMicros;
    private int _hasUnknownPricing;

    public DefaultCostTracker(IModelPricingProvider pricingProvider)
    {
        _pricingProvider = pricingProvider;
    }

    public Task RecordUsageAsync(string modelId, UsageSnapshot usage, CancellationToken ct = default)
    {
        var normalized = usage.NormalizeTotal();
        Interlocked.Add(ref _totalInputTokens, normalized.InputTokens);
        Interlocked.Add(ref _totalOutputTokens, normalized.OutputTokens);
        Interlocked.Add(ref _totalCacheReadInputTokens, normalized.CacheReadInputTokens);
        Interlocked.Add(ref _totalCacheWriteInputTokens, normalized.CacheWriteInputTokens);
        Interlocked.Add(ref _totalTokens, normalized.TotalTokens);

        decimal cost = 0;
        if (_pricingProvider.TryGetPricing(modelId, out var pricing))
        {
            cost = pricing.Calculate(normalized);
            Interlocked.Add(ref _totalCostMicros, ToMicros(cost));
        }
        else
        {
            Interlocked.Exchange(ref _hasUnknownPricing, 1);
        }

        _models.AddOrUpdate(modelId,
            _ => AggregateModelUsage.From(modelId, normalized, cost),
            (_, current) => current.Add(normalized, cost));

        return Task.CompletedTask;
    }

    public Task<CostTrackingSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var models = _models.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToSnapshot(),
            StringComparer.OrdinalIgnoreCase);

        var snapshot = new CostTrackingSnapshot(
            Volatile.Read(ref _totalInputTokens),
            Volatile.Read(ref _totalOutputTokens),
            Volatile.Read(ref _totalCacheReadInputTokens),
            Volatile.Read(ref _totalCacheWriteInputTokens),
            Volatile.Read(ref _totalTokens),
            FromMicros(Volatile.Read(ref _totalCostMicros)),
            Volatile.Read(ref _hasUnknownPricing) == 1,
            models);

        return Task.FromResult(snapshot);
    }

    public Task ResetAsync(CancellationToken ct = default)
    {
        _models.Clear();
        Interlocked.Exchange(ref _totalInputTokens, 0);
        Interlocked.Exchange(ref _totalOutputTokens, 0);
        Interlocked.Exchange(ref _totalCacheReadInputTokens, 0);
        Interlocked.Exchange(ref _totalCacheWriteInputTokens, 0);
        Interlocked.Exchange(ref _totalTokens, 0);
        Interlocked.Exchange(ref _totalCostMicros, 0);
        Interlocked.Exchange(ref _hasUnknownPricing, 0);
        return Task.CompletedTask;
    }

    private static long ToMicros(decimal value) => (long)Math.Round(value * 1_000_000m, MidpointRounding.AwayFromZero);
    private static decimal FromMicros(long value) => value / 1_000_000m;

    private sealed record AggregateModelUsage(
        string ModelId,
        int InputTokens,
        int OutputTokens,
        int CacheReadInputTokens,
        int CacheWriteInputTokens,
        int TotalTokens,
        long TotalCostMicros,
        int Requests)
    {
        public static AggregateModelUsage From(string modelId, UsageSnapshot usage, decimal cost)
            => new(modelId, usage.InputTokens, usage.OutputTokens, usage.CacheReadInputTokens, usage.CacheWriteInputTokens, usage.TotalTokens, ToMicros(cost), 1);

        public AggregateModelUsage Add(UsageSnapshot usage, decimal cost)
            => this with
            {
                InputTokens = InputTokens + usage.InputTokens,
                OutputTokens = OutputTokens + usage.OutputTokens,
                CacheReadInputTokens = CacheReadInputTokens + usage.CacheReadInputTokens,
                CacheWriteInputTokens = CacheWriteInputTokens + usage.CacheWriteInputTokens,
                TotalTokens = TotalTokens + usage.TotalTokens,
                TotalCostMicros = TotalCostMicros + ToMicros(cost),
                Requests = Requests + 1,
            };

        public ModelUsageSnapshot ToSnapshot()
            => new(ModelId, InputTokens, OutputTokens, CacheReadInputTokens, CacheWriteInputTokens, TotalTokens, FromMicros(TotalCostMicros), Requests);
    }
}