# Nexus Recipes

Recipes are short scenario selectors, not a second home for the runnable examples.

The runnable step-by-step implementations for the main scenarios live under [../../examples/README.md](../../examples/README.md).

Use this section when you know the problem shape but want help choosing the smallest correct Nexus setup before you dive into the full example or guide.

## How To Use This Section

Each recipe answers four questions quickly:

1. What problem shape is this for?
2. Which Nexus packages and entry points are involved?
3. What should remain outside Nexus?
4. What is the smallest viable wiring?

If you need runnable code, jump to the matching example. If you need API detail, jump to the matching guide.

## Choose A Recipe

### I already have a provider and model picker UI

Use [Existing Provider UI](existing-provider-ui.md).

Choose this when:

- users already pick provider and model in your own frontend
- Nexus should execute, not own provider UX
- you only need to map UI choices to `IChatClient`, model IDs, and pricing

### I want one assistant that can call tools

Use [Single Agent With Tools](single-agent-with-tools.md).

Choose this when:

- one agent is enough
- the main value is tool use
- you do not need sessions or multi-step routing yet

### I want chat-style continuity with memory and compaction

Use [Chat Session With Memory](chat-session-with-memory.md).

Choose this when:

- the same conversation continues over time
- context windows can get large
- you want resume, compaction, and optional post-compaction recall

### I want a workflow with explicit approvals

Use [Human-Approved Workflow](human-approved-workflow.md).

Choose this when:

- work should move through fixed stages
- some steps or tools require human confirmation
- you want a loop plus routing, not just a single task

### I want fast fan-out before a deterministic merge

Use [Parallel Sub-Agents And Workflow Fan-Out](parallel-subagents-and-workflow-fanout.md).

Choose this when:

- one coordinator should delegate specialist work immediately
- the next stage should still be modeled as an explicit workflow
- you want separate concurrency limits for local delegation and graph execution

### I need recovery instead of rerunning a long workflow

Use [Checkpointed Recovery Workflow](checkpointed-recovery-workflow.md).

Choose this when:

- partial graph completion should be preserved
- restart-from-zero is too expensive
- you need checkpoint and resume semantics

### I want a narrow worker with a tiny tool surface

Use [Tool-Only Worker Agent](tool-only-worker-agent.md).

Choose this when:

- a specialized worker should stay constrained
- the main runtime value is tool execution
- routing and session continuity are unnecessary

### I need to process a batch under a cost ceiling

Use [Cost-Aware Batch Processing](cost-aware-batch-processing.md).

Choose this when:

- batch size matters financially
- agents or workflows need explicit budget limits
- you want estimated-cost visibility during or after execution

### I already have a task system and a graph brain

Use [Task System + Graph Brain](task-system-graph-brain.md).

Choose this when:

- task state already lives elsewhere
- a graph database such as Ladybug holds durable knowledge
- Nexus should orchestrate reasoning and tools without replacing those systems

## Mental Model

Nexus is easiest to reason about if you treat it as three layers:

- Tools: how the agent touches the outside world
- Runtime: how the agent executes, loops, pauses, resumes, and compacts
- Routing: how work moves between steps or agents

Most applications start with one recipe and later add one more layer.

Typical growth path:

1. Start with one tool-using agent.
2. Add sessions and compaction.
3. Add approvals.
4. Add workflow routing.
5. Add bounded fan-out plus deterministic merge.

## Related Guides

- [Quick Start](../guides/quick-start.md)
- [Orchestration](../guides/orchestration.md)
- [Memory & Context](../guides/memory.md)
- [Permissions](../guides/permissions.md)
- [Workflows DSL](../guides/workflows-dsl.md)
- [Sub-Agents](../guides/sub-agents.md)
- [External Brain & Task System](../guides/external-brain-task-system.md)