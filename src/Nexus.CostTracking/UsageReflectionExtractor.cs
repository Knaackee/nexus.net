using System.Reflection;
using System.Collections;

namespace Nexus.CostTracking;

internal static class UsageReflectionExtractor
{
    public static bool TryExtractResponse(object response, out string? modelId, out UsageSnapshot usage)
    {
        modelId = ReadStringProperty(response, "ModelId", "Model", "DeploymentName");
        usage = ExtractUsage(response);
        return !usage.Equals(UsageSnapshot.Empty) || !string.IsNullOrWhiteSpace(modelId);
    }

    public static bool TryExtractUpdate(object update, out string? modelId, out UsageSnapshot usage)
    {
        modelId = ReadStringProperty(update, "ModelId", "Model", "DeploymentName");
        usage = ExtractUsage(update);
        return !usage.Equals(UsageSnapshot.Empty) || !string.IsNullOrWhiteSpace(modelId);
    }

    private static UsageSnapshot ExtractUsage(object source)
    {
        var usageObject = ReadProperty(source, "Usage", "UsageDetails", "TokenUsage")
            ?? ReadUsageFromAdditionalProperties(source);
        if (usageObject is null)
            return UsageSnapshot.Empty;

        var snapshot = new UsageSnapshot(
            InputTokens: ReadIntProperty(usageObject, "InputTokenCount", "InputTokens", "PromptTokenCount", "PromptTokens"),
            OutputTokens: ReadIntProperty(usageObject, "OutputTokenCount", "OutputTokens", "CompletionTokenCount", "CompletionTokens"),
            CacheReadInputTokens: ReadIntProperty(usageObject, "CacheReadInputTokenCount", "CacheReadInputTokens", "CachedInputTokenCount"),
            CacheWriteInputTokens: ReadIntProperty(usageObject, "CacheWriteInputTokenCount", "CacheWriteInputTokens", "CacheCreationInputTokenCount", "CacheCreationInputTokens"),
            TotalTokens: ReadIntProperty(usageObject, "TotalTokenCount", "TotalTokens"));

        return snapshot.NormalizeTotal();
    }

    private static string? ReadStringProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (ReadProperty(source, propertyName) is string value && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static int ReadIntProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = ReadProperty(source, propertyName);
            if (value is null)
                continue;

            return value switch
            {
                int i => i,
                long l => (int)l,
                short s => s,
                _ when int.TryParse(value.ToString(), out var parsed) => parsed,
                _ => 0,
            };
        }

        return 0;
    }

    private static object? ReadProperty(object source, params string[] propertyNames)
    {
        var type = source.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null)
                continue;

            return property.GetValue(source);
        }

        return null;
    }

    private static object? ReadUsageFromAdditionalProperties(object source)
    {
        var additionalProperties = ReadProperty(source, "AdditionalProperties");
        if (additionalProperties is not IEnumerable entries)
            return null;

        foreach (var entry in entries)
        {
            var key = ReadProperty(entry, "Key")?.ToString();
            if (!string.Equals(key, "Usage", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "UsageDetails", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "TokenUsage", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ReadProperty(entry, "Value");
        }

        return null;
    }
}