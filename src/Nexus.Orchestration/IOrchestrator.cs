using Nexus.Core.Agents;

namespace Nexus.Orchestration;

/// <summary>
/// Orchestrates agent task execution across graphs, sequences, parallel branches, and hierarchies.
/// </summary>
public interface IOrchestrator
{
    Task<OrchestrationResult> ExecuteGraphAsync(ITaskGraph graph, CancellationToken ct = default);

    Task<OrchestrationResult> ExecuteGraphAsync(
        ITaskGraph graph, OrchestrationOptions options, CancellationToken ct = default);

    IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(
        ITaskGraph graph, CancellationToken ct = default);

    IAsyncEnumerable<OrchestrationEvent> ExecuteGraphStreamingAsync(
        ITaskGraph graph, OrchestrationOptions options, CancellationToken ct = default);

    Task<OrchestrationResult> ExecuteSequenceAsync(
        IEnumerable<AgentTask> tasks, CancellationToken ct = default);

    IAsyncEnumerable<OrchestrationEvent> ExecuteSequenceStreamingAsync(
        IEnumerable<AgentTask> tasks, CancellationToken ct = default);

    Task<OrchestrationResult> ExecuteParallelAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default);

    IAsyncEnumerable<OrchestrationEvent> ExecuteParallelStreamingAsync(
        IEnumerable<AgentTask> tasks,
        Func<IEnumerable<AgentResult>, AgentResult>? aggregator = null,
        CancellationToken ct = default);

    Task<OrchestrationResult> ExecuteHierarchicalAsync(
        AgentTask rootTask, HierarchyOptions options, CancellationToken ct = default);

    Task<OrchestrationResult> ResumeFromCheckpointAsync(
        OrchestrationSnapshot snapshot, ITaskGraph graph, CancellationToken ct = default);

    ITaskGraph CreateGraph();

    IObservable<OrchestrationEvent> Events { get; }
}
