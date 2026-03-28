using Nexus.Core.Agents;

namespace Nexus.Orchestration.Propagators;

public sealed class FullPassthroughPropagator : IContextPropagator
{
    public Task<PropagatedContext> ExtractAsync(
        AgentResult result, AgentTask nextTask, int maxTokens, CancellationToken ct = default)
    {
        return Task.FromResult(new PropagatedContext
        {
            Summary = result.Text ?? string.Empty,
            EstimatedTokens = (result.Text?.Length ?? 0) / 4,
        });
    }
}

public sealed class StructuredOnlyPropagator : IContextPropagator
{
    public Task<PropagatedContext> ExtractAsync(
        AgentResult result, AgentTask nextTask, int maxTokens, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object>();
        if (result.StructuredOutput.HasValue)
            data["output"] = result.StructuredOutput.Value;

        return Task.FromResult(new PropagatedContext
        {
            Summary = string.Empty,
            StructuredData = data,
        });
    }
}
