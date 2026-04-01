# Single Agent With Tools

This example is the canonical recipe for the smallest useful Nexus setup: one agent, one orchestration pass, and a narrow tool surface.

## What This Example Solves

Use this shape when:

- one agent is enough
- the main runtime value is tool execution
- sessions and workflow routing would only add complexity

## Project Files

- `Program.cs`: complete runnable example
- `Nexus.Examples.SingleAgentWithTools.csproj`: project wiring
- tests: `tests/Nexus.Examples.Tests` using `SingleAgentWithTools_RunsToolCallFlow`

## Run It

```powershell
dotnet run --project examples/Nexus.Examples.SingleAgentWithTools
```

## Validate It

```powershell
dotnet test tests/Nexus.Examples.Tests --filter SingleAgentWithTools_RunsToolCallFlow
```

## Step By Step

### Step 1: Build a minimal Nexus container

The example starts with `AddNexus(...)`, `UseChatClient(...)`, and `AddOrchestration(...)`.

Why this step exists:

- `UseChatClient(...)` gives the runtime an LLM endpoint
- `AddOrchestration(...)` registers `IAgentPool` and `IOrchestrator`
- nothing else is added because this scenario does not need sessions or routing

### Step 2: Register exactly one useful tool

`IToolRegistry` receives a single `LambdaTool` named `get_time`.

Why this step exists:

- the example should demonstrate tool usage without drowning the setup in unrelated capabilities
- `IsReadOnly` and `IsIdempotent` describe safe execution semantics
- a narrow tool list makes the agent behavior easier to reason about and test

### Step 3: Spawn one focused agent

The agent definition gives the assistant one job: use the `get_time` tool when needed.

Why this step exists:

- the agent should have a constrained tool surface
- the system prompt expresses the rule clearly
- the example demonstrates the basic `IAgentPool -> AgentDefinition` lifecycle

### Step 4: Execute exactly one task

The example sends a single `AgentTask` through `IOrchestrator.ExecuteSequenceAsync(...)`.

Why this step exists:

- one-shot orchestration is the correct abstraction for a non-session scenario
- this keeps the runtime deterministic and small
- it avoids introducing multi-turn loop behavior before it is needed

### Step 5: Let the fake chat client force a tool call

The demo chat client first emits a `FunctionCallContent` for `get_time`, then renders the tool result on the second pass.

Why this step exists:

- the example remains runnable without external credentials
- the tool path is exercised every time
- the sample output proves the end-to-end flow instead of just registering a tool and hoping it is used

## Source Walkthrough

### Service registration

`Program.cs` starts by configuring the smallest possible runtime surface for this scenario.

```csharp
services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => new ToolCallingChatClient());
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddPermissions(p => p.UsePreset(PermissionPreset.Interactive));
});
```

Why it is written this way:

- orchestration is required for spawning and executing the agent
- permissions are kept available so the example remains close to a realistic runtime
- no sessions or memory are added because they are not part of this problem shape

### Tool registration

The tool is registered after the container is built.

```csharp
toolRegistry.Register(new LambdaTool(
    "get_time",
    "Returns the current UTC timestamp.",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))))
{
    Annotations = new ToolAnnotations { IsReadOnly = true, IsIdempotent = true },
});
```

Why it is written this way:

- `LambdaTool` is the shortest path to a readable sample
- the timestamp format is deterministic and easy to assert in tests
- annotations model safe tool semantics explicitly

### Agent execution

The example spawns one agent and executes one task through the orchestrator.

```csharp
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "Use the get_time tool when the user asks for the current time.",
    ToolNames = ["get_time"],
});

var result = await orchestrator.ExecuteSequenceAsync([
    AgentTask.Create("What time is it right now?") with { AssignedAgent = agent.Id }
]);
```

Why it is written this way:

- the example demonstrates the actual runtime API, not a fake wrapper
- the explicit `AssignedAgent` shows how tasks bind to an agent instance
- sequence execution is enough because there is only one task

## How To Adapt This To Production

1. Replace `ToolCallingChatClient` with your provider-backed `IChatClient`.
2. Expand `ToolNames` only with tools the agent really needs.
3. Tighten approval policy if any tool mutates state.
4. Add cost tracking when the provider reports token usage.

## When To Move On

Move to [Chat Session With Memory](../Nexus.Examples.ChatSessionWithMemory/README.md) when the interaction becomes conversational instead of one-shot.