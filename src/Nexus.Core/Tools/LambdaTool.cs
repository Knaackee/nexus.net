using System.Text.Json;

namespace Nexus.Core.Tools;

public class LambdaTool : ITool
{
    public string Name { get; }
    public string Description { get; }
    public ToolAnnotations? Annotations { get; init; }
    public JsonElement? InputSchema { get; init; }

    private readonly Func<JsonElement, IToolContext, CancellationToken, Task<ToolResult>> _execute;

    public LambdaTool(
        string name,
        string description,
        Func<JsonElement, IToolContext, CancellationToken, Task<ToolResult>> execute)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct)
        => _execute(input, context, ct);
}
