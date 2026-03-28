# Example: Minimal Agent

The `Nexus.Examples.Minimal` project demonstrates a single agent with tools and guardrails.

**Location:** `examples/Nexus.Examples.Minimal/`

## What It Shows

- Configuring Nexus via `AddNexus` builder
- Registering tools with `LambdaTool`
- Spawning an agent through `IAgentPool`
- Running a simple task via `IOrchestrator`
- Adding guardrails for input validation

## Code

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Guardrails;
using Nexus.Guardrails.BuiltIn;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => myLlmClient);
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddMemory(m => m.UseInMemory());
    nexus.AddGuardrails(g =>
    {
        g.AddPromptInjectionDetector();
        g.AddPiiRedactor();
    });
});

var sp = services.BuildServiceProvider();

// Register tools
var tools = sp.GetRequiredService<IToolRegistry>();
tools.Register(new LambdaTool("get_time", "Current UTC time",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O")))));

// Spawn agent
var pool = sp.GetRequiredService<IAgentPool>();
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "You are a helpful assistant. Use tools when needed.",
    ToolNames = ["get_time"],
});

// Guardrail check
var guardrails = sp.GetRequiredService<IGuardrailPipeline>();
var inputCheck = await guardrails.EvaluateInputAsync("What time is it?");
if (!inputCheck.IsAllowed)
{
    Console.WriteLine($"Blocked: {inputCheck.Reason}");
    return;
}

// Execute
var orchestrator = sp.GetRequiredService<IOrchestrator>();
var result = await orchestrator.ExecuteSequenceAsync([
    new AgentTask
    {
        Id = TaskId.New(),
        Description = "What time is it?",
        AssignedAgent = agent.Id,
    }
]);

Console.WriteLine(result.TaskResults.Values.First().Text);
```

## Running

```bash
cd examples/Nexus.Examples.Minimal
dotnet run
```
