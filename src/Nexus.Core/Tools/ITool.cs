using System.Runtime.CompilerServices;
using System.Text.Json;
using Nexus.Core.Events;

namespace Nexus.Core.Tools;

/// <summary>
/// Represents a tool that agents can invoke to perform actions or retrieve information.
/// </summary>
public interface ITool
{
    /// <summary>Unique name identifying this tool.</summary>
    string Name { get; }
    /// <summary>Human-readable description of what this tool does.</summary>
    string Description { get; }
    ToolAnnotations? Annotations => null;
    JsonElement? InputSchema => null;

    Task<ToolResult> ExecuteAsync(
        JsonElement input, IToolContext context, CancellationToken ct = default);

    async IAsyncEnumerable<ToolEvent> ExecuteStreamingAsync(
        JsonElement input, IToolContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ToolProgressEvent(Name, "Executing...", 0);
        var result = await ExecuteAsync(input, context, ct);
        yield return new ToolCompletedEvent(Name, result);
    }
}
