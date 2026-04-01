# Workflow Patterns And Anti-Patterns

Workflows stay reliable when structure is explicit and each node has a narrow responsibility.

## Good Patterns

- research -> plan -> execute -> review
- fan-out specialists -> merge -> publish
- approval after plan, not after every trivial step
- conditional edges for explicit branch points
- checkpoint after expensive or externally dependent stages

## Anti-Patterns

- one giant node that hides all branching in prompt text
- approval on every step regardless of risk
- hidden dependencies between parallel nodes
- unbounded concurrency for child agents or graph nodes
- workflow steps that rely on side effects not modeled anywhere in the graph

## Heuristics

- if dependencies matter, model them as edges
- if humans must review a step, use an approval gate
- if work is independent and short-lived, sub-agents are often enough
- if merge logic matters, use the workflow layer instead of prompt-only synthesis

## Related Docs

- [Orchestration](orchestration.md)
- [Sub-Agents](sub-agents.md)
- [Workflows DSL](workflows-dsl.md)