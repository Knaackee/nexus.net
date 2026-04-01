using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Orchestration;

public sealed class PartitionedToolExecutor : IToolExecutor
{
    private readonly ToolExecutionDelegate _pipeline;
    private readonly int _maxReadOnlyConcurrency;

    public PartitionedToolExecutor(
        IEnumerable<IToolMiddleware> middlewares,
        ToolExecutorOptions? options = null)
    {
        var builder = new ToolPipelineBuilder();
        foreach (var middleware in middlewares)
            builder.Use(middleware);

        _pipeline = builder.BuildBuffered(static (tool, input, ctx, ct) => tool.ExecuteAsync(input, ctx, ct));
        _maxReadOnlyConcurrency = Math.Max(1, options?.MaxReadOnlyConcurrency ?? Environment.ProcessorCount);
    }

    public async Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<ToolExecutionRequest> requests,
        IToolContext context,
        CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return [];

        var results = new List<ToolExecutionResult>(requests.Count);

        for (int index = 0; index < requests.Count;)
        {
            if (IsReadOnly(requests[index].Tool))
            {
                var blockStart = index;
                while (index < requests.Count && IsReadOnly(requests[index].Tool))
                    index++;

                var readOnlyBlock = requests.Skip(blockStart).Take(index - blockStart).ToList();
                var readOnlyResults = await ExecuteParallelAsync(readOnlyBlock, context, ct).ConfigureAwait(false);
                results.AddRange(readOnlyResults);
                continue;
            }

            var request = requests[index++];
            var result = await _pipeline(request.Tool, request.Input, context, ct).ConfigureAwait(false);
            results.Add(new ToolExecutionResult(request.CallId, request.Tool.Name, result));
        }

        return results;
    }

    private async Task<IReadOnlyList<ToolExecutionResult>> ExecuteParallelAsync(
        IReadOnlyList<ToolExecutionRequest> requests,
        IToolContext context,
        CancellationToken ct)
    {
        using var throttler = new SemaphoreSlim(_maxReadOnlyConcurrency);
        var tasks = requests.Select(async request =>
        {
            await throttler.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await _pipeline(request.Tool, request.Input, context, ct).ConfigureAwait(false);
                return new ToolExecutionResult(request.CallId, request.Tool.Name, result);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static bool IsReadOnly(ITool tool) => tool.Annotations?.IsReadOnly == true;
}