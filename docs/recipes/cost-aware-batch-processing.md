# Recipe: Cost-Aware Batch Processing

Use this when many similar tasks must be executed under an explicit spend constraint.

## Good Fit

- tasks can be run as a bounded batch
- each item does not need a full interactive session
- the batch has a real cost ceiling

## Core Pieces

- `AddCostTracking(...)`
- agent or workflow budgets
- orchestration sequencing or bounded graph execution
- post-run cost snapshot inspection

## Operating Notes

- set per-agent or per-workflow budgets before large runs
- prefer shorter prompts and narrower tools for batch workers
- surface estimated cost with each run so the batch controller can stop early

## Related Guides

- [Cost Tracking](../guides/cost-tracking.md)
- [Orchestration](../guides/orchestration.md)