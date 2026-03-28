# Nexus.Testing API Reference

## Namespace: Nexus.Testing.Mocks

### FakeChatClient

A test double for `Microsoft.Extensions.AI.IChatClient`:

```csharp
public sealed class FakeChatClient : IChatClient
{
    // Constructors
    public FakeChatClient(params string[] responses);

    // Fluent configuration
    public FakeChatClient WithResponse(string text);
    public FakeChatClient WithStreamingResponse(params string[] chunks);

    // Verification
    public List<IList<ChatMessage>> ReceivedMessages { get; }

    // IChatClient implementation
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

When responses are exhausted, returns `"[No more responses configured]"`.

### MockAgent

A mock `IAgent` with pre-configured responses:

```csharp
public sealed class MockAgent : IAgent
{
    // Factory methods
    public static MockAgent AlwaysReturns(string output, string? name = null);
    public static MockAgent AlwaysFails(string error, string? name = null);
    public static MockAgent WithHandler(Func<AgentTask, AgentResult> handler, string? name = null);
    public static MockAgent WithResponses(params (string input, string output)[] responses);

    // Verification
    public List<AgentTask> ReceivedTasks { get; }

    // IAgent properties
    public AgentId Id { get; }
    public string Name { get; }
    public AgentState State { get; }
}
```

## Namespace: Nexus.Testing.Recording

### EventRecorder

Records all events emitted during agent streaming execution:

```csharp
public sealed class EventRecorder
{
    public IReadOnlyList<AgentEvent> Events { get; }
    public void Clear();
    public static (RecordingAgent Agent, EventRecorder Recorder) Wrap(IAgent agent);
}
```

### RecordingAgent

An agent wrapper that delegates to an inner agent while recording all events:

```csharp
public sealed class RecordingAgent : IAgent
{
    public AgentId Id { get; }
    public string Name { get; }
    public AgentState State { get; }
    // Delegates to inner agent, records events from streaming execution
}
```

**Usage:**

```csharp
var (agent, recorder) = EventRecorder.Wrap(realAgent);

await foreach (var evt in agent.ExecuteStreamingAsync(task, context))
{
    // Events flow through normally
}

// Inspect after execution
Assert.Equal(3, recorder.Events.Count);
Assert.IsType<TextChunkEvent>(recorder.Events[0]);
```
