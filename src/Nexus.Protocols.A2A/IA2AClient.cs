namespace Nexus.Protocols.A2A;

/// <summary>A2A client for communicating with remote agents.</summary>
public interface IA2AClient : IDisposable
{
    /// <summary>Discover a remote agent by fetching its Agent Card.</summary>
    Task<AgentCard> DiscoverAsync(Uri agentCardUri, CancellationToken ct = default);

    /// <summary>Send a task to a remote agent and wait for completion.</summary>
    Task<A2ATask> SendTaskAsync(Uri endpoint, A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>Send a task and stream updates.</summary>
    IAsyncEnumerable<A2ATaskUpdate> StreamTaskAsync(Uri endpoint, A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>Cancel a running task.</summary>
    Task CancelTaskAsync(Uri endpoint, string taskId, CancellationToken ct = default);
}

/// <summary>A2A server interface — implemented by Nexus to expose agents via A2A.</summary>
public interface IA2AServer
{
    /// <summary>The agent card for this server.</summary>
    AgentCard Card { get; }

    /// <summary>Handle an incoming task request.</summary>
    Task<A2ATask> HandleTaskAsync(A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>Handle an incoming task request with streaming.</summary>
    IAsyncEnumerable<A2ATaskUpdate> HandleTaskStreamingAsync(A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>Cancel a running task.</summary>
    Task CancelTaskAsync(string taskId, CancellationToken ct = default);
}
