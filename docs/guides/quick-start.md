# Quick Start

Build a working Nexus agent in a few minutes.

This guide takes you from service registration to a running task with tools, budgets, and cost tracking.

## What You Build

- one agent
- one tool registry
- one orchestrator run
- optional permissions and cost tracking

## 1. Register Nexus

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.CostTracking;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Tools;
using Nexus.Memory;
using Nexus.Orchestration;
using Nexus.Permissions;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => myLlmClient);
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddPermissions(p => p
        .UsePreset(PermissionPreset.Interactive)
        .UseConsolePrompt());
    nexus.AddCostTracking(c => c.AddModel("gpt-4o", input: 2.50m, output: 10.00m));
    nexus.AddMemory(m => m.UseInMemory());
});

var sp = services.BuildServiceProvider();
```

`UseDefaults()` wires the common runtime middleware, including budget enforcement and tool execution.

## 2. Register Tools

```csharp
var tools = sp.GetRequiredService<IToolRegistry>();

tools.Register(new LambdaTool(
    "get_time",
    "Returns the current UTC time",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O"))))
{
    Annotations = new ToolAnnotations
    {
        IsReadOnly = true,
        IsIdempotent = true,
    }
});
```

## 3. Spawn The Agent

```csharp
var pool = sp.GetRequiredService<IAgentPool>();

var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "You are a helpful assistant.",
    Budget = new AgentBudget { MaxCostUsd = 1.00m },
    ToolNames = ["get_time"],
});
```

## 4. Execute A Task

```csharp
var orchestrator = sp.GetRequiredService<IOrchestrator>();

var result = await orchestrator.ExecuteSequenceAsync([
    AgentTask.Create("Tell me the current UTC time.") with { AssignedAgent = agent.Id }
]);

var taskResult = result.TaskResults.Values.First();
Console.WriteLine(taskResult.Text);
Console.WriteLine(taskResult.EstimatedCost);
Console.WriteLine(taskResult.TokenUsage?.TotalTokens);
```

## 5. Check Cost Tracking

```csharp
var tracker = sp.GetRequiredService<ICostTracker>();
var costs = await tracker.GetSnapshotAsync();

Console.WriteLine($"Estimated USD: ${costs.TotalCost:F6}");
```

## 6. Where To Go Next

- Need workflow graphs: [Workflows DSL](workflows-dsl.md)
- Need child delegation: [Sub-Agents](sub-agents.md)
- Need approvals: [Permissions](permissions.md)
- Need scenario-first setups: [Recipe Index](../recipes/README.md)