# Recipe: Checkpointed Recovery Workflow

Use this when a workflow is long enough or expensive enough that restart-from-zero is unacceptable.

## Canonical Sources

- guide: [Checkpointing](../guides/checkpointing.md)
- guide: [Orchestration](../guides/orchestration.md)
- runnable examples index: [../../examples/README.md](../../examples/README.md)

## Decision

Use this recipe if you need resume semantics after partial graph progress.

Prefer the checkpointing guide for API details and the examples index for runnable scenario projects. This page stays intentionally thin so the checkpointing contract only has one real home.
