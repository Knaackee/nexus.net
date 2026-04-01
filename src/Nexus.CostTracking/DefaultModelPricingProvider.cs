namespace Nexus.CostTracking;

public sealed class DefaultModelPricingProvider : IModelPricingProvider
{
    private readonly Dictionary<string, ModelPricing> _pricing;

    public DefaultModelPricingProvider(CostTrackingOptions options)
    {
        _pricing = new Dictionary<string, ModelPricing>(options.ModelPricing, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetPricing(string? modelId, out ModelPricing pricing)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            pricing = default!;
            return false;
        }

        return _pricing.TryGetValue(modelId, out pricing!);
    }
}