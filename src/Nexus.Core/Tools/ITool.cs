using System.Runtime.CompilerServices;
using System.Text.Json;
using Nexus.Core.Events;

namespace Nexus.Core.Tools;

public interface ITool
{
    string Name { get; }
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
