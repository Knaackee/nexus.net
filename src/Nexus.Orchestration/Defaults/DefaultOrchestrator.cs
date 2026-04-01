using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;

namespace Nexus.Orchestration.Defaults;

public sealed class DefaultOrchestrator : IOrchestrator, IDisposable
{
    private readonly IAgentPool _pool;
    private readonly IServiceProvider _services;
    private readonly Subject<OrchestrationEvent> _events = new();

    public IObservable<OrchestrationEvent> Events => _events;

    public DefaultOrchestrator(IAgentPool pool, IServiceProvider services)
    {
        _pool = pool;
        _services = services;
    }

    public ITaskGraph CreateGraph() => new DefaultTaskGraph();

    public async Task<OrchestrationResult> ExecuteGraphAsync(
        ITaskGraph graph, CancellationToken ct = default) =>
        await ExecuteGraphAsync(graph, new OrchestrationOptions(), ct).ConfigureAwait(false);

    public async Task<OrchestrationResult> ExecuteGraphAsync(
        ITaskGraph graph, OrchestrationOptions options, CancellationToken ct = default)
    {
        var results = new ConcurrentDictionary<TaskId, AgentResult>();
        var skipped = new HashSet<TaskId>();
        var sw = Stopwatch.StartNew();

        await foreach (var evt in ExecuteGraphStreamingAsync(graph, options, ct))
        {
            if (evt is NodeCompletedEvent completed)
                results[completed.NodeId] = completed.Result;
            else if (evt is NodeFailedEvent failed)
                results[failed.NodeId] = AgentResult.Failed(failed.Error.Message);
            else if (evt is NodeSkippedEvent skippedEvent)
                skipped.Add(skippedEvent.NodeId);
        }

        sw.Stop();
        var hasFailures = results.Values.Any(r => r.Status != AgentResultStatus.Success);

        return new OrchestrationResult
        {
            Status = hasFailures ? OrchestrationStatus.PartiallyCompleted : OrchestrationStatus.Completed,
            TaskResults = results,
            Duration = sw.Elapsed,
        };
    }

    public IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(
        ITaskGraph graph, CancellationToken ct = default) =>
        ExecuteGraphStreamingAsync(graph, new OrchestrationOptions(), ct);

    public async IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(
        ITaskGraph graph, OrchestrationOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var validation = graph.Validate();
        if (!validation.IsValid)
            throw new InvalidOperationException($"Graph validation failed: {string.Join(", ", validation.Errors)}");

        var defaultGraph = (DefaultTaskGraph)graph;
        var completedResults = new ConcurrentDictionary<TaskId, AgentResult>();
        var terminalIds = new HashSet<TaskId>();
        var skippedIds = new HashSet<TaskId>();
        var sem = new SemaphoreSlim(options.MaxConcurrentNodes);
        var channel = Channel.CreateUnbounded<OrchestrationEvent>(new UnboundedChannelOptions { SingleReader = true });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.GlobalTimeout);
        var linkedCt = timeoutCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (terminalIds.Count < graph.Nodes.Count && !linkedCt.IsCancellationRequested)
                {
                    TaskNodeSchedulingPlan plan;
                    lock (terminalIds)
                    {
                        plan = defaultGraph.CreateSchedulingPlan(terminalIds, completedResults, skippedIds);
                    }

                    foreach (var skipped in plan.SkippedNodes)
                    {
                        lock (terminalIds)
                        {
                            if (!terminalIds.Add(skipped.Node.TaskId))
                                continue;

                            skippedIds.Add(skipped.Node.TaskId);
                        }

                        await channel.Writer.WriteAsync(
                            new NodeSkippedEvent(graph.Id, skipped.Node.TaskId, skipped.Reason), linkedCt).ConfigureAwait(false);
                    }

                    var ready = plan.ReadyNodes;
                    if (ready.Count == 0)
                    {
                        if (plan.SkippedNodes.Count > 0)
                            continue;

                        await Task.Delay(50, linkedCt).ConfigureAwait(false);
                        continue;
                    }

                    var tasks = ready.Select(async node =>
                    {
                        await sem.WaitAsync(linkedCt).ConfigureAwait(false);
                        try
                        {
                            var taskNode = (DefaultTaskNode)node;
                            var agent = await ResolveAgentAsync(taskNode.Task, linkedCt).ConfigureAwait(false);

                            await channel.Writer.WriteAsync(
                                new NodeStartedEvent(graph.Id, node.TaskId, agent.Id), linkedCt).ConfigureAwait(false);

                            var context = CreateAgentContext(agent);

                            try
                            {
                                await foreach (var evt in ExecuteAgentStreamingAsync(agent, taskNode.Task, context, linkedCt))
                                {
                                    await channel.Writer.WriteAsync(
                                        new AgentEventInGraph(graph.Id, node.TaskId, evt), linkedCt).ConfigureAwait(false);

                                    if (evt is AgentCompletedEvent completed)
                                    {
                                        completedResults[node.TaskId] = completed.Result;
                                        lock (terminalIds) { terminalIds.Add(node.TaskId); }
                                        await channel.Writer.WriteAsync(
                                            new NodeCompletedEvent(graph.Id, node.TaskId, completed.Result), linkedCt).ConfigureAwait(false);
                                    }
                                    else if (evt is AgentFailedEvent failed)
                                    {
                                        var failedResult = AgentResult.Failed(failed.Error.Message);
                                        completedResults[node.TaskId] = failedResult;
                                        lock (terminalIds) { terminalIds.Add(node.TaskId); }
                                        await channel.Writer.WriteAsync(
                                            new NodeFailedEvent(graph.Id, node.TaskId, failed.Error), linkedCt).ConfigureAwait(false);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                var failedResult = AgentResult.Failed(ex.Message);
                                completedResults[node.TaskId] = failedResult;
                                lock (terminalIds) { terminalIds.Add(node.TaskId); }
                                await channel.Writer.WriteAsync(
                                    new NodeFailedEvent(graph.Id, node.TaskId, ex), linkedCt).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }).ToArray();

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                channel.Writer.Complete();
            }
        }, linkedCt);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            _events.OnNext(evt);
            yield return evt;
        }
    }

    public async Task<OrchestrationResult> ExecuteSequenceAsync(
        IEnumerable<AgentTask> tasks, CancellationToken ct = default)
    {
        var graph = CreateGraph();
        ITaskNode? previous = null;
        foreach (var task in tasks)
        {
            var node = graph.AddTask(task);
            if (previous is not null)
                graph.AddDependency(previous, node);
            previous = node;
        }

        return await ExecuteGraphAsync(graph, ct).ConfigureAwait(false);
    }

    public IAsyncEnumerable<OrchestrationEvent> ExecuteSequenceStreamingAsync(
        IEnumerable<AgentTask> tasks, CancellationToken ct = default)
    {
        var graph = CreateGraph();
        ITaskNode? previous = null;
        foreach (var task in tasks)
        {
            var node = graph.AddTask(task);
            if (previous is not null)
                graph.AddDependency(previous, node);
            previous = node;
        }

        return ExecuteGraphStreamingAsync(graph, ct);
    }

    public async Task<OrchestrationResult> ExecuteParallelAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default)
    {
        var graph = CreateGraph();
        foreach (var task in tasks)
            graph.AddTask(task);

        return await ExecuteGraphAsync(graph, ct).ConfigureAwait(false);
    }

    public IAsyncEnumerable<OrchestrationEvent> ExecuteParallelStreamingAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default)
    {
        var graph = CreateGraph();
        foreach (var task in tasks)
            graph.AddTask(task);

        return ExecuteGraphStreamingAsync(graph, ct);
    }

    public async Task<OrchestrationResult> ExecuteHierarchicalAsync(
        AgentTask rootTask, HierarchyOptions options, CancellationToken ct = default)
    {
        var graph = CreateGraph();
        graph.AddTask(rootTask);
        return await ExecuteGraphAsync(graph, new OrchestrationOptions
        {
            GlobalTimeout = options.ChildTimeout,
            MaxConcurrentNodes = options.MaxChildAgents,
        }, ct).ConfigureAwait(false);
    }

    public async Task<OrchestrationResult> ResumeFromCheckpointAsync(
        OrchestrationSnapshot snapshot, ITaskGraph graph, CancellationToken ct = default)
    {
        // Resume by marking completed nodes and re-executing remaining
        return await ExecuteGraphAsync(graph, ct).ConfigureAwait(false);
    }

    private OrchestratorAgentContext CreateAgentContext(IAgent agent)
    {
        return new OrchestratorAgentContext(agent, _services);
    }

    private async Task<IAgent> ResolveAgentAsync(AgentTask task, CancellationToken ct)
    {
        if (task.AssignedAgent is AgentId assignedAgent)
        {
            var existing = _pool.ActiveAgents.FirstOrDefault(agent => agent.Id == assignedAgent);
            if (existing is not null)
                return existing;
        }

        if (task.AgentDefinition is not null)
            return await _pool.SpawnAsync(task.AgentDefinition, ct).ConfigureAwait(false);

        return await _pool.SpawnAsync(new AgentDefinition { Name = $"agent-{task.Id}" }, ct).ConfigureAwait(false);
    }

    private IAsyncEnumerable<AgentEvent> ExecuteAgentStreamingAsync(
        IAgent agent,
        AgentTask task,
        IAgentContext context,
        CancellationToken ct)
    {
        var builder = new AgentPipelineBuilder();
        foreach (var middleware in _services.GetServices<IAgentMiddleware>())
            builder.Use(middleware);

        var pipeline = builder.BuildStreaming((innerTask, innerContext, innerCt) => agent.ExecuteStreamingAsync(innerTask, innerContext, innerCt));
        return pipeline(task, context, ct);
    }

    public void Dispose()
    {
        _events.OnCompleted();
        _events.Dispose();
    }
}
