using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Core.Pipeline;

public delegate Task<AgentResult> AgentExecutionDelegate(
    AgentTask task, IAgentContext ctx, CancellationToken ct);

public delegate IAsyncEnumerable<AgentEvent> StreamingAgentExecutionDelegate(
    AgentTask task, IAgentContext ctx, CancellationToken ct);

public interface IAgentMiddleware
{
    Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx,
        AgentExecutionDelegate next, CancellationToken ct);

    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task, IAgentContext ctx,
        StreamingAgentExecutionDelegate next,
        CancellationToken ct = default)
        => next(task, ctx, ct);
}
