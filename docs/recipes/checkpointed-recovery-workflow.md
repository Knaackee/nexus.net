# Recipe: Checkpointed Recovery Workflow

Use this when a workflow is long enough or expensive enough that restart-from-zero is unacceptable.

## Good Fit

- workflows have multiple expensive nodes
- failures can happen after partial completion
- operators need resume instead of rerun

## Core Pieces

- `AddOrchestration(o => o.UseDefaults())`
- `AddCheckpointing(c => c.UseInMemory())` or another checkpoint store
- `IOrchestrator`
- `OrchestrationSnapshot` and checkpoint serializers

## Shape

- execute a graph
- save checkpoints after relevant nodes
- restore snapshot state when rerunning after a failure

## Related Guides

- [Checkpointing](../guides/checkpointing.md)
- [Orchestration](../guides/orchestration.md)