using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Orchestration;

public interface IToolExecutor
{
    Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<ToolExecutionRequest> requests,
        IToolContext context,
        CancellationToken ct = default);
}

public sealed record ToolExecutionRequest(string CallId, ITool Tool, JsonElement Input);

public sealed record ToolExecutionResult(string CallId, string ToolName, ToolResult Result);

public sealed class ToolExecutorOptions
{
    public int MaxReadOnlyConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount);
}