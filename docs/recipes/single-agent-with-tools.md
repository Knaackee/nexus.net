# Recipe: Single Agent With Tools

Use this when you want the smallest useful Nexus setup: one agent, a few tools, and no extra workflow machinery.

## Good Fit

This recipe is a good fit if:

- a single agent can solve the task
- you want tool use but not session persistence yet
- orchestration complexity would be overhead

## Core Pieces

Use these Nexus components:

- `AddOrchestration(o => o.UseDefaults())`
- `IAgentPool`
- `IOrchestrator`
- `IToolRegistry`

Optional but common:

- `AddPermissions(...)` if any tool mutates state
- `AddCostTracking(...)` if you care about token and USD reporting

## Minimal Wiring

```csharp
var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => chatClient);
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddPermissions(p => p.UsePreset(PermissionPresets.Interactive));
});

var sp = services.BuildServiceProvider();
var tools = sp.GetRequiredService<IToolRegistry>();

tools.Register(new LambdaTool(
    "get_time",
    "Returns the current UTC time",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O"))))
{
    Annotations = new ToolAnnotations { IsReadOnly = true, IsIdempotent = true }
});

var pool = sp.GetRequiredService<IAgentPool>();
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "You are a helpful assistant. Use tools when they help.",
    ToolNames = ["get_time"],
});

var orchestrator = sp.GetRequiredService<IOrchestrator>();
var result = await orchestrator.ExecuteSequenceAsync([
    AgentTask.Create("What time is it?") with { AssignedAgent = agent.Id }
]);
```

## Keep Out Of Nexus

Do not add sessions, recall, or workflow routing until the problem actually needs them.

The simplest rule is:

- one request in
- one agent runs
- tools are called if needed
- one result comes out

## Common Next Step

If users continue the same conversation over time, move next to [Chat Session With Memory](chat-session-with-memory.md).

## Related Guides

- [Quick Start](../getting-started/quickstart.md)
- [Permissions](../guides/permissions.md)
- [Cost Tracking](../guides/cost-tracking.md)