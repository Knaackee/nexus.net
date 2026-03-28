# Messaging

Inter-agent communication via pub/sub messaging, point-to-point, and request/response patterns.

## IMessageBus

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

## AgentMessage

```csharp
public record AgentMessage
{
    public required MessageId Id { get; init; }
    public required AgentId Sender { get; init; }
    public required string Type { get; init; }
    public required object Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string> Metadata { get; init; }
    public MessageId? CorrelationId { get; init; }
}
```

## Patterns

### Point-to-Point

Send a message directly to one agent:

```csharp
var bus = sp.GetRequiredService<IMessageBus>();

await bus.SendAsync(targetAgent.Id, new AgentMessage
{
    Id = MessageId.New(),
    Sender = myAgent.Id,
    Type = "task.assigned",
    Payload = new { Task = "Analyze Q4 data" },
});
```

### Publish/Subscribe

Publish to a topic; all subscribers receive the message:

```csharp
// Subscribe
var subscription = bus.Subscribe(myAgent.Id, "research.completed", async msg =>
{
    var findings = msg.Payload;
    // Process findings...
});

// Publish
await bus.PublishAsync("research.completed", new AgentMessage
{
    Id = MessageId.New(),
    Sender = researcher.Id,
    Type = "research.completed",
    Payload = new { Summary = "Key findings...", Sources = 12 },
});

// Unsubscribe
subscription.Dispose();
```

### Request/Response

Synchronous-style request with timeout:

```csharp
var response = await bus.RequestAsync(
    analyst.Id,
    new AgentMessage
    {
        Id = MessageId.New(),
        Sender = manager.Id,
        Type = "analysis.request",
        Payload = new { DataSource = "sales_db", Query = "Q4 revenue" },
    },
    timeout: TimeSpan.FromSeconds(30)
);

Console.WriteLine(response.Payload);
```

### Broadcast

Send to all active agents:

```csharp
await bus.BroadcastAsync(new AgentMessage
{
    Id = MessageId.New(),
    Sender = coordinator.Id,
    Type = "system.shutdown",
    Payload = "Graceful shutdown in 30 seconds",
});
```

## Correlation

Use `CorrelationId` to link related messages:

```csharp
var requestId = MessageId.New();

var request = new AgentMessage
{
    Id = requestId,
    Sender = manager.Id,
    Type = "task.request",
    Payload = taskData,
};

// The response carries the original request's ID
var response = new AgentMessage
{
    Id = MessageId.New(),
    Sender = worker.Id,
    Type = "task.response",
    Payload = result,
    CorrelationId = requestId,
};
```

## Dead Letter Queue

Messages that fail delivery (unhandled exceptions in handlers, missing subscribers) are routed to a dead letter queue for later inspection.

## Configuration

```csharp
services.AddNexus(nexus =>
{
    nexus.AddMessaging(m =>
    {
        // Configure DLQ, retry policies, etc.
    });
});
```

## Design Notes

- `IMessageBus` is **forward-declared** in `Nexus.Core.Contracts`. The in-memory implementation lives in `Nexus.Messaging`.
- `MessageId` is a strongly-typed `Guid` wrapper with JSON serialization.
- The bus is in-process only. For distributed scenarios, implement `IMessageBus` over your preferred transport (RabbitMQ, Azure Service Bus, etc.).
