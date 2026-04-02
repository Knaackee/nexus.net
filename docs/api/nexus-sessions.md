# Nexus.Sessions API Reference

`Nexus.Sessions` stores chat-session metadata and transcripts.

Use it when a conversation should continue over time, be resumable, or have durable message history independent of the loop implementation.

## Key Types

### `ISessionStore`

Session metadata store.

```csharp
public interface ISessionStore
{
    Task<SessionInfo> CreateAsync(SessionCreateOptions options, CancellationToken ct = default);
    Task<SessionInfo?> GetAsync(SessionId id, CancellationToken ct = default);
    IAsyncEnumerable<SessionInfo> ListAsync(SessionFilter? filter = null, CancellationToken ct = default);
    Task UpdateAsync(SessionInfo session, CancellationToken ct = default);
    Task<bool> DeleteAsync(SessionId id, CancellationToken ct = default);
}
```

### `ISessionTranscript`

Transcript storage surface for appending, replacing, and reading messages.

### `SessionInfo`

Session metadata, message count, cost snapshot, and additional metadata.

### `SessionId`

Stable session identifier used by the loop and host code.

## Registration

In-memory:

```csharp
services.AddNexus(builder =>
{
    builder.AddSessions(sessions =>
    {
        sessions.UseInMemory();
    });
});
```

Filesystem:

```csharp
builder.AddSessions(sessions =>
{
    sessions.UseFileSystem(".nexus/sessions");
});
```

## When To Use It

- the same chat should be resumed later
- transcripts must survive host restarts
- cost or metadata snapshots belong to a session identity

## Related Packages

- `Nexus.AgentLoop`
- `Nexus.Compaction`

## Related Docs

- [Chat Session With Memory](../recipes/chat-session-with-memory.md)
- [Memory Guide](../guides/memory.md)