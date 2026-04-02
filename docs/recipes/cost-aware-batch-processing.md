# Recipe: Cost-Aware Batch Processing

Use this when many similar tasks must be executed under an explicit spend constraint.

## Canonical Sources

- guide: [Cost Tracking](../guides/cost-tracking.md)
- guide: [Orchestration](../guides/orchestration.md)
- runnable examples index: [../../examples/README.md](../../examples/README.md)

## Decision

Use this recipe when cost is a hard batch control, not just an observability metric.

Start from the cost-tracking guide for pricing and token surfaces, then narrow the orchestration shape to sequence, graph, or bounded parallelism based on the batch controller you actually need.
