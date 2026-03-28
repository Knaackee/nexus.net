using System.Text.Json;
using Nexus.Core.Events;
using Nexus.Core.Tools;

namespace Nexus.Core.Pipeline;

public delegate Task<ToolResult> ToolExecutionDelegate(
    ITool tool, JsonElement input, IToolContext ctx, CancellationToken ct);

public delegate IAsyncEnumerable<ToolEvent> StreamingToolExecutionDelegate(
    ITool tool, JsonElement input, IToolContext ctx, CancellationToken ct);

public interface IToolMiddleware
{
    Task<ToolResult> InvokeAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        ToolExecutionDelegate next, CancellationToken ct);

    IAsyncEnumerable<ToolEvent> InvokeStreamingAsync(
        ITool tool, JsonElement input, IToolContext ctx,
        StreamingToolExecutionDelegate next,
        CancellationToken ct = default)
        => next(tool, input, ctx, ct);
}
