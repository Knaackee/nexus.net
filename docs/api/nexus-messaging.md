# Nexus.Messaging API Reference

## Namespace: Nexus.Core.Contracts

> These types are forward-declared in `Nexus.Core`. The in-memory implementation lives in `Nexus.Messaging`.

### IMessageBus

```csharp
public interface IMessageBus
{
    Task SendAsync(AgentId target, AgentMessage message, CancellationToken ct = default);
    Task PublishAsync(string topic, AgentMessage message, CancellationToken ct = default);
    Task<AgentMessage> RequestAsync(AgentId target, AgentMessage request, TimeSpan timeout, CancellationToken ct = default);
    IDisposable Subscribe(AgentId subscriber, string topic, Func<AgentMessage, Task> handler);
    Task BroadcastAsync(AgentMessage message, CancellationToken ct = default);
}
```

### AgentMessage

```csharp
public record AgentMessage
{
    public required MessageId Id { get; init; }
    public required AgentId Sender { get; init; }
    public required string Type { get; init; }
    public required object Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IDictionary<string, string> Metadata { get; init; }
    public MessageId? CorrelationId { get; init; }
}
```

### MessageId

```csharp
public readonly record struct MessageId(Guid Value)
{
    public static MessageId New();
    public override string ToString();  // Returns first 8 hex chars
}
```

JSON serialization is handled by `MessageIdJsonConverter`.
