using System.Text.Json;

namespace Nexus.Tools.Standard;

internal static class ToolJson
{
    public static string GetRequiredString(JsonElement input, string propertyName)
    {
        if (!input.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Property '{propertyName}' is required.");

        return property.GetString()!;
    }

    public static string? GetOptionalString(JsonElement input, string propertyName)
        => input.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    public static int? GetOptionalInt(JsonElement input, string propertyName)
        => input.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    public static bool GetOptionalBool(JsonElement input, string propertyName, bool defaultValue = false)
        => input.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : defaultValue;

    public static IReadOnlyList<string> GetOptionalStringArray(JsonElement input, string propertyName)
    {
        if (!input.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }
}