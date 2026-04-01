# Parallel Sub-Agents And Workflow Fan-Out

This example is the canonical recipe for combining quick delegated specialist work with a deterministic workflow merge stage.

## What This Example Solves

Use this shape when:

- one coordinator should delegate several independent tasks immediately
- the merge and publish logic should remain explicit and testable
- you want separate control over sub-agent concurrency and workflow-node concurrency

## Project Files

- `Program.cs`: runnable fan-out and merge sample
- `Nexus.Examples.ParallelSubAgentsAndWorkflowFanOut.csproj`: project wiring
- tests: `tests/Nexus.Examples.Tests` using `ParallelSubAgentsAndWorkflowFanOut_DelegatesThenExecutesWorkflow`

## Run It

```powershell
dotnet run --project examples/Nexus.Examples.ParallelSubAgentsAndWorkflowFanOut
```

## Validate It

```powershell
dotnet test tests/Nexus.Examples.Tests --filter ParallelSubAgentsAndWorkflowFanOut_DelegatesThenExecutesWorkflow
```

## Step By Step

### Step 1: Register orchestration, workflow DSL, and standard sub-agent tools

This scenario combines two different delegation layers.

Why this step exists:

- `AddStandardTools(...Agents)` registers the `agent` tool for fast local fan-out
- `AddWorkflowDsl()` registers the compiler and executor for the explicit merge stage
- `AddOrchestration(...)` provides the actual execution engine beneath both layers

### Step 2: Resolve the `agent` tool from the registry

The example pulls the tool from `IToolRegistry` instead of manually instantiating higher-level orchestration code.

Why this step exists:

- it demonstrates the real runtime contract the coordinator would use
- it proves the standard tool registration path works
- it keeps sub-agent delegation close to the normal tool model

### Step 3: Execute a batched sub-agent request

The input JSON uses `tasks[]` and `maxConcurrency`.

Why this step exists:

- the tasks are independent and should run concurrently
- bounded concurrency prevents uncontrolled fan-out
- the batch result shows how to inspect partial or full success

### Step 4: Define an explicit merge workflow

After fan-out, the example creates a small workflow with `merge` and `publish` nodes.

Why this step exists:

- merge and publish rules are structural, not hidden in a giant prompt
- conditional edges make publish eligibility explicit
- workflow options allow graph-level concurrency and timeout control separately from tool-level concurrency

### Step 5: Execute the workflow through `IWorkflowExecutor`

The explicit merge stage runs after delegated work is complete.

Why this step exists:

- the example cleanly separates local delegation from graph semantics
- the workflow layer handles branching and skip behavior correctly
- it demonstrates how two runtime layers can be composed without custom glue code

## Source Walkthrough

### Tool fan-out input

```csharp
var toolResult = await agentTool.ExecuteAsync(JsonDocument.Parse("""
{
  "maxConcurrency": 3,
  "tasks": [
    { "agent": "Researcher", "task": "Collect the strongest supporting evidence" },
    { "agent": "RiskAnalyst", "task": "List failure modes and missing controls" },
    { "agent": "Reviewer", "task": "Identify weak assumptions and unclear claims" }
  ]
}
""").RootElement.Clone(), new RecipeToolContext(toolRegistry), CancellationToken.None);
```

Why it is written this way:

- the JSON shape mirrors how an LLM or coordinator would call the tool
- `maxConcurrency` bounds local fan-out
- each task is independent so the batch remains safe to parallelize

### Merge workflow

```csharp
var workflow = new WorkflowDefinition
{
    Id = "fanout-merge",
    Name = "Fan-Out Merge",
    Nodes =
    [
        new NodeDefinition { Id = "merge", Name = "Merge", Description = "Merge the findings into one brief." },
        new NodeDefinition { Id = "publish", Name = "Publish", Description = "Publish the approved brief." },
    ],
    Edges =
    [
        new EdgeDefinition { From = "merge", To = "publish", Condition = "result.text.contains('approved')" },
    ],
    Options = new WorkflowOptions { MaxConcurrentNodes = 4, GlobalTimeoutSeconds = 300 }
};
```

Why it is written this way:

- `merge` and `publish` are explicit stages with clear roles
- the condition makes the publish gate inspectable and testable
- graph-level options are independent from sub-agent concurrency settings

## How To Adapt This To Production

1. Replace the fake chat client with provider-backed specialists.
2. Give each delegated agent only the tools it really needs.
3. Add approval or checkpointing at the merge/publish stage if the process is expensive or risky.
4. Keep sub-agent tasks independent so concurrency remains safe.

## When To Use A Different Shape

If the work is strictly sequential, move back to [Human-Approved Workflow](../Nexus.Examples.HumanApprovedWorkflow/README.md) or a simpler orchestration graph.