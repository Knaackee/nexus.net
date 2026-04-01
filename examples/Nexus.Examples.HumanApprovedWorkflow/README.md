# Human-Approved Workflow

This example is the canonical recipe for staged work where a human must explicitly approve the transition from planning to execution.

## What This Example Solves

Use this shape when:

- work should proceed through named steps
- a plan or draft must be reviewed before execution continues
- workflow structure should be explicit instead of hidden in prompt text

## Project Files

- `Program.cs`: runnable workflow loop with approval
- `Nexus.Examples.HumanApprovedWorkflow.csproj`: project wiring
- tests: `tests/Nexus.Examples.Tests` using `HumanApprovedWorkflow_UsesApprovalGateBeforeNextStep`

## Run It

```powershell
dotnet run --project examples/Nexus.Examples.HumanApprovedWorkflow
```

## Validate It

```powershell
dotnet test tests/Nexus.Examples.Tests --filter HumanApprovedWorkflow_UsesApprovalGateBeforeNextStep
```

## Step By Step

### Step 1: Register the loop and orchestration runtime

This scenario uses the loop because the workflow is stepwise, and orchestration is required under the hood for agent execution.

Why this step exists:

- `IAgentLoop` is the execution surface for routed multi-step work
- `AddOrchestration(...)` registers the runtime pieces the loop needs
- this keeps approval workflows inside the public Nexus runtime surface

### Step 2: Provide an approval gate

The sample registers `ScriptedApprovalGate` as `IApprovalGate`.

Why this step exists:

- approval should be explicit runtime policy, not vague prompt instructions
- the sample remains runnable without human input in the terminal
- the gate demonstrates that approval can modify continuation context, not just allow or deny

### Step 3: Define an explicit workflow graph

The example models `research -> plan -> execute` as nodes and edges.

Why this step exists:

- the workflow is readable to both humans and LLMs
- `RequiresApproval = true` is attached to the right step instead of being hidden in prompt prose
- the graph is the stable structural source of truth for progression

### Step 4: Run the loop with `WorkflowRoutingStrategy`

The loop delegates next-step decisions to the workflow router.

Why this step exists:

- it separates routing from agent execution
- it keeps workflow semantics reusable and testable
- it is the correct bridge between a conversational loop and an explicit stage graph

### Step 5: Let approval modify the next step input

The scripted gate returns `ModifiedContext` with an adjusted output.

Why this step exists:

- in real systems, reviewers often refine a plan rather than merely approve it unchanged
- the sample demonstrates a stronger runtime pattern than boolean allow/deny
- it shows how human feedback can affect downstream execution deterministically

## Source Walkthrough

### Workflow definition

```csharp
var workflow = new WorkflowDefinition
{
    Id = "approved-change",
    Name = "Approved Change Workflow",
    Nodes =
    [
        new NodeDefinition { Id = "research", Name = "Research", Description = "Research the request: {input}" },
        new NodeDefinition { Id = "plan", Name = "Plan", Description = "Create a plan from: {previous}", RequiresApproval = true },
        new NodeDefinition { Id = "execute", Name = "Execute", Description = "Execute the approved plan: {previous}" },
    ],
    Edges =
    [
        new EdgeDefinition { From = "research", To = "plan" },
        new EdgeDefinition { From = "plan", To = "execute" },
    ],
};
```

Why it is written this way:

- the structure is intentionally linear and easy to inspect
- approval is attached only to the plan step
- descriptions depend on previous-step output so the data flow remains clear

### Approval gate

```csharp
sealed class ScriptedApprovalGate : IApprovalGate
{
    public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var modified = JsonSerializer.SerializeToElement(new
        {
            Output = "Approved implementation plan with rollback checklist"
        });
        return Task.FromResult(new ApprovalResult(true, "recipe-demo", ModifiedContext: modified));
    }
}
```

Why it is written this way:

- the example demonstrates the full approval contract, not just approval success
- the modified output makes downstream behavior observable
- it mirrors realistic reviewer behavior more closely than a raw boolean approval

## How To Adapt This To Production

1. Replace `ScriptedApprovalGate` with a UI-backed or service-backed approval flow.
2. Add tool-level permissions alongside step-level approval when side effects are involved.
3. Expand the workflow with review or publish nodes only when those stages are structurally real.

## When To Move On

Move to [Parallel Sub-Agents And Workflow Fan-Out](../Nexus.Examples.ParallelSubAgentsAndWorkflowFanOut/README.md) when one coordinator should split work to specialists before merging the result.