using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Orchestration;

public static class StreamMerger
{
    public static async IAsyncEnumerable<OrchestrationEvent> MergeParallelAsync(
        TaskGraphId graphId,
        IReadOnlyList<(TaskId NodeId, IAgent Agent, AgentTask Task, IAgentContext Context)> work,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<OrchestrationEvent>(
            new UnboundedChannelOptions { SingleReader = true });

        var tasks = work.Select(async w =>
        {
            try
            {
                await channel.Writer.WriteAsync(
                    new NodeStartedEvent(graphId, w.NodeId, w.Agent.Id), ct).ConfigureAwait(false);

                await foreach (var evt in w.Agent.ExecuteStreamingAsync(w.Task, w.Context, ct))
                {
                    await channel.Writer.WriteAsync(
                        new AgentEventInGraph(graphId, w.NodeId, evt), ct).ConfigureAwait(false);

                    if (evt is AgentCompletedEvent completed)
                    {
                        await channel.Writer.WriteAsync(
                            new NodeCompletedEvent(graphId, w.NodeId, completed.Result), ct).ConfigureAwait(false);
                    }
                    else if (evt is AgentFailedEvent failed)
                    {
                        await channel.Writer.WriteAsync(
                            new NodeFailedEvent(graphId, w.NodeId, failed.Error), ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                await channel.Writer.WriteAsync(
                    new NodeFailedEvent(graphId, w.NodeId, ex), ct).ConfigureAwait(false);
            }
        }).ToArray();

        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.Complete(), ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            yield return evt;
    }
}
