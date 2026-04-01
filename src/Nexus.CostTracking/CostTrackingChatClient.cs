using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Nexus.CostTracking;

public sealed class CostTrackingChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ICostTracker _tracker;
    private readonly IModelPricingProvider _pricingProvider;
    private readonly string? _configuredModelId;

    public CostTrackingChatClient(
        IChatClient inner,
        ICostTracker tracker,
        IModelPricingProvider pricingProvider,
        string? configuredModelId = null)
    {
        _inner = inner;
        _tracker = tracker;
        _pricingProvider = pricingProvider;
        _configuredModelId = configuredModelId;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _inner.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (UsageReflectionExtractor.TryExtractResponse(response, out var modelId, out var usage) && !usage.Equals(UsageSnapshot.Empty))
        {
            var resolvedModelId = ResolveModelId(modelId, options);
            await _tracker.RecordUsageAsync(resolvedModelId, usage, cancellationToken).ConfigureAwait(false);
            AttachTrackingMetadata(response, usage, resolvedModelId);
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? modelId = null;
        var usage = UsageSnapshot.Empty;

        await foreach (var update in _inner.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (UsageReflectionExtractor.TryExtractUpdate(update, out var updateModelId, out var updateUsage))
            {
                modelId ??= updateModelId;
                usage = usage.MergePreferLargest(updateUsage);
                AttachTrackingMetadata(update, updateUsage, ResolveModelId(modelId, options));
            }

            yield return update;
        }

        if (!usage.Equals(UsageSnapshot.Empty))
        {
            var resolvedModelId = ResolveModelId(modelId, options);
            await _tracker.RecordUsageAsync(resolvedModelId, usage, cancellationToken).ConfigureAwait(false);

            var finalUpdate = new ChatResponseUpdate { ModelId = resolvedModelId };
            AttachTrackingMetadata(finalUpdate, usage, resolvedModelId);
            yield return finalUpdate;
        }
    }

    public void Dispose() => _inner.Dispose();

    public object? GetService(Type serviceType, object? serviceKey = null) => _inner.GetService(serviceType, serviceKey);

    private string ResolveModelId(string? observedModelId, ChatOptions? options)
        => observedModelId
            ?? ReadModelId(options)
            ?? _configuredModelId
            ?? "unknown";

    private static string? ReadModelId(ChatOptions? options)
        => options?.GetType().GetProperty("ModelId")?.GetValue(options) as string;

    private void AttachTrackingMetadata(object target, UsageSnapshot usage, string modelId)
    {
        SetAdditionalProperty(target, "Usage", new UsageDetails
        {
            InputTokenCount = usage.InputTokens,
            OutputTokenCount = usage.OutputTokens,
            TotalTokenCount = usage.NormalizeTotal().TotalTokens,
        });

        if (_pricingProvider.TryGetPricing(modelId, out var pricing))
            SetAdditionalProperty(target, "NexusEstimatedCost", pricing.Calculate(usage));
    }

    private static void SetAdditionalProperty(object target, string key, object value)
    {
        var additionalPropertiesProperty = target.GetType().GetProperty("AdditionalProperties");
        if (additionalPropertiesProperty is null)
            return;

        var dictionary = additionalPropertiesProperty.GetValue(target);
        if (dictionary is null && additionalPropertiesProperty.CanWrite)
        {
            dictionary = Activator.CreateInstance(additionalPropertiesProperty.PropertyType);
            additionalPropertiesProperty.SetValue(target, dictionary);
        }

        var indexer = dictionary?.GetType().GetProperty("Item");
        indexer?.SetValue(dictionary, value, [key]);
    }
}