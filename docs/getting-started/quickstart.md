# Quick Start

Build a working agent with tools in under 5 minutes.

## 1. Configure Services

Nexus uses `Microsoft.Extensions.DependencyInjection`. Register everything through `AddNexus`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.CostTracking;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Permissions;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    // Plug in any IChatClient (OpenAI, Azure, Ollama, etc.)
    nexus.UseChatClient(_ => new OpenAIChatClient("gpt-4o"));

    // Enable orchestration with default settings
    nexus.AddOrchestration(o => o.UseDefaults());

    // Read-only tools run automatically; mutating tools require approval.
    nexus.AddPermissions(p => p
        .UsePreset(PermissionPreset.Interactive)
        .UseConsolePrompt());

    // Optional: aggregate token usage and estimated USD cost.
    nexus.AddCostTracking(c => c.AddModel("gpt-4o", input: 2.50m, output: 10.00m));

    // Enable in-memory conversation/working memory
    nexus.AddMemory(m => m.UseInMemory());
});

var sp = services.BuildServiceProvider();
```

`UseDefaults()` now wires a tool executor into `ChatAgent`.
Contiguous read-only tool calls are executed in parallel, while mutating tools remain serial.

## 2. Register Tools

Tools are functions your agents can call. Use `LambdaTool` for quick definitions:

```csharp
var tools = sp.GetRequiredService<IToolRegistry>();

tools.Register(new LambdaTool(
    "get_time",
    "Returns the current UTC time",
    (input, context, ct) =>
        Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O")))
)
{
    Annotations = new ToolAnnotations { IsReadOnly = true, IsIdempotent = true }
});

tools.Register(new LambdaTool(
    "calculate",
    "Evaluates a math expression",
    (input, context, ct) =>
    {
        var expr = input.GetProperty("expression").GetString()!;
        // Your evaluation logic here
        return Task.FromResult(ToolResult.Success($"Result: {expr}"));
    }
)
{
    Annotations = new ToolAnnotations { IsReadOnly = true, IsIdempotent = true }
});
```

## 3. Spawn an Agent

Create an agent definition and spawn it through the agent pool:

```csharp
var pool = sp.GetRequiredService<IAgentPool>();

var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "You are a helpful assistant. Use tools when needed.",
    ToolNames = ["get_time", "calculate"],
    Budget = new AgentBudget { MaxCostUsd = 1.00m },
});
```

## 4. Execute a Task

Run a single task through the orchestrator:

```csharp
var orchestrator = sp.GetRequiredService<IOrchestrator>();

var result = await orchestrator.ExecuteSequenceAsync([
    AgentTask.Create("What time is it? Also calculate 42 * 17.") with { AssignedAgent = agent.Id }
]);

var taskResult = result.TaskResults.Values.First();
Console.WriteLine(taskResult.Text);
Console.WriteLine(taskResult.EstimatedCost);
Console.WriteLine(taskResult.TokenUsage?.TotalTokens);

var tracker = sp.GetRequiredService<ICostTracker>();
var costs = await tracker.GetSnapshotAsync();
Console.WriteLine($"Estimated USD: ${costs.TotalCost:F6}");
```

If a task crosses its configured `AgentBudget.MaxCostUsd`, the completed task result returns `AgentResultStatus.BudgetExceeded` instead of `Success`.

If you register a mutating tool, mark it explicitly and let the permissions package arbitrate it:

```csharp
tools.Register(new LambdaTool(
    "write_file",
    "Writes text to disk",
    (input, context, ct) => Task.FromResult(ToolResult.Success("ok")))
{
    Annotations = new ToolAnnotations
    {
        IsReadOnly = false,
        RequiresApproval = true,
        IsDestructive = false,
    }
});
```

## 5. Streaming Execution

For real-time output, use the streaming variants:

```csharp
await foreach (var evt in orchestrator.ExecuteSequenceStreamingAsync([
    AgentTask.Create("Write a haiku about programming.") with { AssignedAgent = agent.Id }
]))
{
    Console.Write(evt); // TextChunkEvent, AgentCompletedEvent, etc.
}
```

## 6. Cost Tracking

`Nexus.CostTracking` wraps the registered `IChatClient` and records usage whenever the provider includes usage metadata in `ChatResponse` or `ChatResponseUpdate`.

```csharp
var tracker = sp.GetRequiredService<ICostTracker>();
var snapshot = await tracker.GetSnapshotAsync();

Console.WriteLine(snapshot.TotalInputTokens);
Console.WriteLine(snapshot.TotalOutputTokens);
Console.WriteLine(snapshot.TotalCost);
```

The same usage is also exposed on completed task results:

```csharp
var taskResult = result.TaskResults.Values.First();

Console.WriteLine(taskResult.TokenUsage?.TotalInputTokens);
Console.WriteLine(taskResult.TokenUsage?.TotalOutputTokens);
Console.WriteLine(taskResult.EstimatedCost);
Console.WriteLine(taskResult.Status); // Success or BudgetExceeded
```

## Multiple Chat Clients

Register named clients for different models:

```csharp
services.AddNexus(nexus =>
{
    nexus.UseChatClient("gpt4", _ => new OpenAIChatClient("gpt-4o"));
    nexus.UseChatClient("mini", _ => new OpenAIChatClient("gpt-4o-mini"));
    nexus.AddOrchestration(o => o.UseDefaults());
});
```

Then reference them in agent definitions:

```csharp
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "CheapAgent",
    ChatClientName = "mini",
    SystemPrompt = "You are a quick helper.",
});
```

## Next Steps

- [Orchestration Guide](../guides/orchestration.md) — Multi-agent graphs, parallel execution
- [Permissions Guide](../guides/permissions.md) — Rule-based tool approval and interactive prompts
- [Cost Tracking Guide](../guides/cost-tracking.md) — Pricing registration and aggregated usage snapshots
- [Guardrails Guide](../guides/guardrails.md) — Add safety checks to your agents
- [Memory Guide](../guides/memory.md) — Conversation history and context windows
- [Workflow DSL Guide](../guides/workflows-dsl.md) — Define pipelines in JSON/YAML
