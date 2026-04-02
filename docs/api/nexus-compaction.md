# Nexus.Compaction API Reference

`Nexus.Compaction` manages context-window pressure for long-running conversations.

Use it when transcript growth can exceed the effective context window and the runtime must reduce active history without losing important state.

## Key Types

### `ITokenCounter`

Counts tokens for single messages or message collections.

### `IContextWindowMonitor`

Measures current token pressure against `ContextWindowOptions`.

### `ICompactionStrategy`

Pluggable compaction strategy with `Priority`, `ShouldCompact(...)`, and `CompactAsync(...)`.

### `ICompactionService`

Top-level service that decides whether compaction is needed and executes it.

### `ICompactionRecallProvider` and `ICompactionRecallService`

Post-compaction extension points that can rewrite or enrich the remaining active messages.

## Core Records

- `ContextWindowSnapshot`
- `CompactionContext`
- `CompactionRecallContext`
- `CompactionResult`
- `CompactionRecallResult`

## Defaults

`UseDefaults()` registers:

- `DefaultTokenCounter`
- `DefaultContextWindowMonitor`
- `MicroCompactionStrategy`
- `SummaryCompactionStrategy`
- `DefaultCompactionService`
- `DefaultCompactionRecallService`

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddCompaction(compaction =>
    {
        compaction.UseDefaults();
    });
});
```

## Design Boundary

Compaction and recall are intentionally separate.

- Compaction reduces active history.
- Recall can then reintroduce distilled or external context.

This separation keeps the token-reduction path predictable and makes recall strategies easier to test independently.

## Related Packages

- `Nexus.AgentLoop`
- `Nexus.Sessions`
- `Nexus.Memory`

## Related Docs

- [Chat Session With Memory](../recipes/chat-session-with-memory.md)
- [Memory Guide](../guides/memory.md)
- [Testing And Benchmarks](../llms/testing-and-benchmarks.md)