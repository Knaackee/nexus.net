# Performance And Benchmarking

Performance work in Nexus should focus on orchestration shape, tool latency, prompt growth, and delegated concurrency rather than raw token throughput alone.

## What To Measure

- workflow compile time for large graphs
- orchestration duration across sequential and parallel shapes
- sub-agent fan-out latency under bounded concurrency
- prompt growth and compaction frequency in long-running sessions
- high-cost tool calls or provider round trips that dominate end-to-end latency

## Existing Benchmark Surface

`benchmarks/Nexus.Benchmarks` currently exercises:

- workflow compilation
- workflow execution
- parallel sub-agent delegation

## Operational Guidance

- benchmark cold-start and warm-start behavior separately
- keep graph concurrency bounded instead of unbounded
- measure branch-heavy workflows, not just happy-path sequences
- treat provider latency and local orchestration overhead as separate costs

## Related Docs

- [Benchmarks README](../../benchmarks/README.md)
- [Sub-Agents](sub-agents.md)
- [Workflows DSL](workflows-dsl.md)