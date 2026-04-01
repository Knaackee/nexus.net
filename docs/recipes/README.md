# Nexus Recipes

Recipes are short, scenario-first guides.

Use them when you already know the shape of the system you want to build and want the smallest correct Nexus setup for it.

## How To Use This Section

Each recipe answers four questions quickly:

1. What problem shape is this for?
2. Which Nexus packages and entry points are involved?
3. What should remain outside Nexus?
4. What is the smallest viable wiring?

If you need API detail after choosing a recipe, jump from the recipe into the matching guide.

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