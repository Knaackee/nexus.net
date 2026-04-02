# Recipe: Human-Approved Workflow

Use this when work should move through explicit stages and either tools or whole steps need approval.

## Not A Good Fit

Do not start here if you only need tool-level safety for a single task. In that case, a single-agent setup plus permissions is usually enough.

## Source-Backed Asset

- runnable example: [../../examples/Nexus.Examples.HumanApprovedWorkflow/README.md](../../examples/Nexus.Examples.HumanApprovedWorkflow/README.md)

## Good Fit

This recipe is a good fit if:

- the agent should not act completely autonomously
- some tool calls are safe and others are sensitive
- users need to review intermediate outputs before the next stage starts

## Core Pieces

Use these Nexus components:

- `AddAgentLoop(loop => loop.UseDefaults())`
- `IRoutingStrategy`, often `WorkflowRoutingStrategy`
- `AddPermissions(...)` for tool-level approval
- `IApprovalGate` for step-level approval

## Approval Split

There are two different approval locations.

### Tool approval

Use this when a specific tool call is risky.

Examples:

- write to disk
- update external state
- delete data

### Step approval

Use this when a whole workflow phase should pause for review.

Examples:

- approve a plan before execution
- approve a draft before publishing
- approve a proposed task update before applying it

## Minimal Shape

```csharp
var workflow = new WorkflowDefinition
{
    Id = "change-flow",
    Name = "Research Plan Execute Review",
    Nodes =
    [
        new NodeDefinition
        {
            Id = "research",
            Name = "Research",
            Description = "Research the request: {input}",
        },
        new NodeDefinition
        {
            Id = "plan",
            Name = "Plan",
            Description = "Create an execution plan from: {previous}",
            RequiresApproval = true,
        },
        new NodeDefinition
        {
            Id = "execute",
            Name = "Execute",
            Description = "Execute the approved plan: {previous}",
        },
    ],
    Edges =
    [
        new EdgeDefinition { From = "research", To = "plan" },
        new EdgeDefinition { From = "plan", To = "execute" },
    ],
};

await foreach (var evt in loop.RunAsync(new AgentLoopOptions
{
    RoutingStrategy = new WorkflowRoutingStrategy(workflow),
    UserInput = "Prepare and apply the production migration.",
}))
{
    Console.WriteLine(evt);
}
```

## Keep Out Of Nexus

Do not encode all business policy inside prompts.

Prefer:

- permission rules for tool-level policy
- approval gates for human checkpoints
- routing definitions for workflow structure

This keeps the system legible to both humans and LLMs.

## Common Next Step

If the workflow is backed by an external task system or graph brain, combine this recipe with [Task System + Graph Brain](task-system-graph-brain.md).

## Related Guides

- [Permissions](../guides/permissions.md)
- [Workflows DSL](../guides/workflows-dsl.md)
- [Orchestration](../guides/orchestration.md)

## Read Next

- external stateful workflows: [Task System + Graph Brain](task-system-graph-brain.md)
- package details: [Nexus.Orchestration](../api/nexus-orchestration.md)