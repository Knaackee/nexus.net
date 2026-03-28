namespace Nexus.Core.Tools;

public record ToolResult
{
    public required bool IsSuccess { get; init; }
    public object? Value { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    public static ToolResult Success(object value) => new() { IsSuccess = true, Value = value };
    public static ToolResult Failure(string error) => new() { IsSuccess = false, Error = error };
    public static ToolResult Denied(string reason) => new() { IsSuccess = false, Error = $"DENIED: {reason}" };
}
