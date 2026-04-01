namespace Nexus.CostTracking;

public interface IModelPricingProvider
{
    bool TryGetPricing(string? modelId, out ModelPricing pricing);
}