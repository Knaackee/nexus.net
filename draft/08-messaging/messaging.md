# Messaging — Nexus.Messaging

> Assembly: `Nexus.Messaging`  
> Deps: `Nexus.Core`

## 1. IMessageBus

```csharp
public interface IMessageBus
{
    Task SendAsync(AgentId target, AgentMessage message, CancellationToken ct);
    Task PublishAsync(string topic, AgentMessage message, CancellationToken ct);
    Task<AgentMessage> RequestAsync(AgentId target, AgentMessage request, TimeSpan timeout, CancellationToken ct);
    IDisposable Subscribe(AgentId subscriber, string topic, Func<AgentMessage, Task> handler);
    Task BroadcastAsync(AgentMessage message, CancellationToken ct);
}
```

## 2. AgentMessage

```csharp
public record AgentMessage
{
    public required MessageId Id { get; init; }
    public required AgentId Sender { get; init; }
    public required string Type { get; init; }
    public required object Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public MessageId? CorrelationId { get; init; }
}
```

## 3. ISharedState

```csharp
public interface ISharedState
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, CancellationToken ct);
    Task<bool> CompareAndSwapAsync<T>(string key, T expected, T replacement, CancellationToken ct);
    IObservable<StateChange> Changes { get; }
}
```

## 4. IDeadLetterQueue

```csharp
public interface IDeadLetterQueue
{
    Task EnqueueAsync(FailedTask task, CancellationToken ct);
    IAsyncEnumerable<FailedTask> DequeueAsync(CancellationToken ct);
    Task RetryAsync(FailedTask task, CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
}

public record FailedTask(AgentTask OriginalTask, Exception Error, DateTimeOffset FailedAt, int RetryCount);
```

## 5. IMessageMiddleware

```csharp
public interface IMessageMiddleware
{
    Task InvokeAsync(AgentMessage message, MessageDelegate next, CancellationToken ct);
}
// Use Cases: Encryption, Logging, Transformation, Filtering
```

## 6. Backends

| Backend | Paket | Patterns |
|---------|-------|----------|
| InMemory | `Nexus.Messaging` | Alle (Dev/Test) |
| Redis | `Nexus.Messaging.Redis` | Pub/Sub, Streams, SharedState |
