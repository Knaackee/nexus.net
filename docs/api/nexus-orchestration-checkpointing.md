# Nexus.Orchestration.Checkpointing API Reference

`Nexus.Orchestration.Checkpointing` provides workflow snapshot storage and resume support.

Use it when a graph should recover from process restarts or partial failure without rerunning already completed work.

## Key Types

### `ICheckpointStore`

Persistence abstraction for orchestration snapshots.

### `ISnapshotSerializer`

Serialization boundary for persisted snapshots.

### `InMemoryCheckpointStore`

Built-in in-memory store for tests and simple hosts.

### `JsonSnapshotSerializer`

Default serializer registered by the in-memory setup.

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddCheckpointing(checkpoints =>
    {
        checkpoints.UseInMemory();
    });
});
```

## When To Use It

- workflow stages are expensive and should not rerun blindly
- long-running graphs need restart safety
- resume semantics are part of the product behavior

## Related Packages

- `Nexus.Orchestration`
- `Nexus.Workflows.Dsl`

## Related Docs

- [Checkpointing Guide](../guides/checkpointing.md)
- [Checkpointed Recovery Workflow](../recipes/checkpointed-recovery-workflow.md)