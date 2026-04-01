namespace Nexus.CostTracking;

public sealed class CostTrackingOptions
{
    public Dictionary<string, ModelPricing> ModelPricing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public CostTrackingOptions AddModel(
        string modelId,
        decimal input,
        decimal output,
        decimal cacheRead = 0,
        decimal cacheWrite = 0)
    {
        ModelPricing[modelId] = new ModelPricing(input, output, cacheRead, cacheWrite);
        return this;
    }
}