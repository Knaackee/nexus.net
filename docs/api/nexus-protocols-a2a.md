# Nexus.Protocols.A2A API Reference

`Nexus.Protocols.A2A` is the agent-to-agent protocol surface for remote task execution over HTTP.

Use it when a Nexus runtime needs to call or expose remote agents through a protocol boundary instead of only running local agents in-process.

## Key Types

### `IA2AClient`

Client surface for discovery, request/response execution, streaming, and cancellation.

```csharp
public interface IA2AClient : IDisposable
{
    Task<AgentCard> DiscoverAsync(Uri agentCardUri, CancellationToken ct = default);
    Task<A2ATask> SendTaskAsync(Uri endpoint, A2ATaskRequest request, CancellationToken ct = default);
    IAsyncEnumerable<A2ATaskUpdate> StreamTaskAsync(Uri endpoint, A2ATaskRequest request, CancellationToken ct = default);
    Task CancelTaskAsync(Uri endpoint, string taskId, CancellationToken ct = default);
}
```

### `IA2AServer`

Server contract for exposing an agent card and handling remote tasks.

### Core Records

- `AgentCard`
- `AgentSkill`
- `A2AAuthRequirements`
- `A2ATaskRequest`
- `A2AMessage`
- `A2ATask`
- `A2AArtifact`
- `A2ATaskUpdate`

### Message Parts And Status

- `A2ATextPart`
- `A2AFilePart`
- `A2ADataPart`
- `A2ATaskStatus`

## Typical Use

```csharp
var client = sp.GetRequiredService<IA2AClient>();

var task = await client.SendTaskAsync(
    new Uri("https://remote-agent.example.com/a2a"),
    new A2ATaskRequest
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = Guid.NewGuid().ToString("N"),
        Messages =
        [
            new A2AMessage
            {
                Role = "user",
                Parts = [new A2ATextPart("Analyze this design document.")],
            }
        ],
    });
```

## When To Use It

- another agent system should be called as a remote worker
- agent discovery and capabilities should be described explicitly
- streaming updates and remote cancellation matter

## Related Docs

- [Protocols Guide](../guides/protocols.md)
- [Nexus.Protocols Overview](nexus-protocols.md)