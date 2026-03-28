using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Testing.Recording;

/// <summary>
/// Wraps an IAgent and records all events emitted during streaming execution.
/// </summary>
public sealed class EventRecorder
{
    private readonly List<AgentEvent> _events = [];

    public IReadOnlyList<AgentEvent> Events => _events;

    internal void Record(AgentEvent evt) => _events.Add(evt);

    public void Clear() => _events.Clear();

    /// <summary>Wraps an agent to record all streaming events.</summary>
    public static (RecordingAgent Agent, EventRecorder Recorder) Wrap(IAgent agent)
    {
        var recorder = new EventRecorder();
        var wrapper = new RecordingAgent(agent, recorder);
        return (wrapper, recorder);
    }
}

/// <summary>An agent wrapper that records all emitted events.</summary>
public sealed class RecordingAgent : IAgent
{
    private readonly IAgent _inner;
    private readonly EventRecorder _recorder;

    internal RecordingAgent(IAgent inner, EventRecorder recorder)
    {
        _inner = inner;
        _recorder = recorder;
    }

    public AgentId Id => _inner.Id;
    public string Name => _inner.Name;
    public AgentState State => _inner.State;

    public Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
        => _inner.ExecuteAsync(task, context, ct);

    public async IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(
        AgentTask task,
        IAgentContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in _inner.ExecuteStreamingAsync(task, context, ct))
        {
            _recorder.Record(evt);
            yield return evt;
        }
    }
}
