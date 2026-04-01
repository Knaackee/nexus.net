# Nexus — Multi-Agent Orchestration Engine

[![CI](https://github.com/your-org/nexus/actions/workflows/ci.yml/badge.svg)](https://github.com/your-org/nexus/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Nexus** is an enterprise-grade .NET 10 framework for building, orchestrating, and managing multi-agent AI systems. It provides a composable architecture for agent execution, tool integration, memory management, guardrails, protocol support (MCP, A2A, AG-UI), and workflow orchestration.

> **[📖 Full Documentation](docs/README.md)** — Architecture guides, API reference, and examples.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                    Nexus.Hosting.AspNetCore               │
│              (AG-UI SSE, A2A JSON-RPC, Health)           │
├──────────────────────────────────────────────────────────┤
│  Nexus.Protocols.Mcp  │  Nexus.Protocols.A2A  │  AG-UI  │
├────────────────────────┴───────────────────────┴─────────┤
│                   Nexus.Workflows.Dsl                    │
│                  (JSON/YAML Pipelines)                   │
├──────────────────────────────────────────────────────────┤
│  Nexus.Orchestration  │  Nexus.Orchestration.Checkpoint  │
│   (Graph, Sequence,   │     (Snapshot Serialization,     │
│    Parallel, Hier.)   │      In-Memory Store)            │
├───────────────────────┴──────────────────────────────────┤
│  Guardrails  │  Memory  │  Messaging  │  Telemetry  │Auth│
│  (PII, Inj.) │ (Conv,   │ (PubSub,   │(OTel Traces│OAuth│
│              │  Working) │  DLQ)      │ & Metrics) │    │
├──────────────────────────────────────────────────────────┤
│                      Nexus.Core                          │
│    Agents · Tools · Pipeline · Events · Contracts · DI   │
└──────────────────────────────────────────────────────────┘
```

## Packages

| Package | Description |
|---------|-------------|
| `Nexus.Core` | Agent abstractions, tool registry, pipeline builder, events, DI configuration |
| `Nexus.Orchestration` | Graph/sequence/parallel/hierarchical execution, agent pool, task graphs |
| `Nexus.Orchestration.Checkpointing` | In-memory checkpoint store, JSON snapshot serialization |
| `Nexus.Memory` | Conversation store, working memory, context window management |
| `Nexus.Messaging` | In-memory message bus, pub/sub, shared state, dead letter queue |
| `Nexus.Guardrails` | Prompt injection detection, PII redaction, secrets detection, input limits |
| `Nexus.Permissions` | Rule-based tool approval, interactive prompts, permission middleware |
| `Nexus.CostTracking` | Provider-agnostic token and USD cost aggregation around `IChatClient` |
| `Nexus.Telemetry` | OpenTelemetry traces & metrics for agents and tools |
| `Nexus.Auth.OAuth2` | API key authentication, OAuth2 client credentials, token cache |
| `Nexus.Protocols.Mcp` | Model Context Protocol tool adapter and host management |
| `Nexus.Protocols.A2A` | Agent-to-Agent protocol client (JSON-RPC over HTTP) |
| `Nexus.Protocols.AgUi` | AG-UI event bridge for real-time streaming to frontends |
| `Nexus.Workflows.Dsl` | Load agent pipelines from JSON/YAML with validation & variable resolution |
| `Nexus.Hosting.AspNetCore` | ASP.NET Core endpoints for AG-UI (SSE) and A2A, health checks |
| `Nexus.Testing` | Mock agents, tools, event recording, evaluation harnesses |

## Quick Start

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
    nexus.UseChatClient(_ => myLlmClient);       // Plug in any IChatClient
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddPermissions(p => p
        .UsePreset(PermissionPreset.Interactive)
        .UseConsolePrompt());
    nexus.AddCostTracking(c => c.AddModel("gpt-4o", input: 2.50m, output: 10.00m));
    nexus.AddMemory(m => m.UseInMemory());
});

var sp = services.BuildServiceProvider();

// Register tools
var tools = sp.GetRequiredService<IToolRegistry>();
tools.Register(new LambdaTool("get_time", "Current UTC time",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O"))))
{
    Annotations = new ToolAnnotations { IsReadOnly = true, IsIdempotent = true }
});

// Spawn an agent and execute
var pool = sp.GetRequiredService<IAgentPool>();
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "You are a helpful assistant.",
    Budget = new AgentBudget { MaxCostUsd = 1.00m },
});

var orchestrator = sp.GetRequiredService<IOrchestrator>();
var result = await orchestrator.ExecuteSequenceAsync([
    AgentTask.Create("Hello!") with { AssignedAgent = agent.Id }
]);

var taskResult = result.TaskResults.Values.First();
Console.WriteLine(taskResult.Text);
Console.WriteLine(taskResult.EstimatedCost);
Console.WriteLine(taskResult.TokenUsage?.TotalTokens);

var tracker = sp.GetRequiredService<ICostTracker>();
var costs = await tracker.GetSnapshotAsync();
Console.WriteLine($"Estimated USD: ${costs.TotalCost:F6}");
```

When the provider returns usage metadata, `UseDefaults()` now propagates token usage and estimated cost into `AgentResult`, and `MaxCostUsd` is enforced through the default budget middleware.

## Multi-Agent Graph

```csharp
var graph = orchestrator.CreateGraph();
var research = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Research AI trends",
    AssignedAgent = researcher.Id,
});
var write = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Write blog post",
    AssignedAgent = writer.Id,
});

graph.AddDependency(research, write);

await foreach (var evt in orchestrator.ExecuteGraphStreamingAsync(graph))
{
    // Handle NodeStarted, AgentEventInGraph, NodeCompleted, etc.
}
```

## Guardrails

```csharp
using Nexus.Guardrails;
using Nexus.Guardrails.BuiltIn;

var pipeline = new DefaultGuardrailPipeline([
    new PromptInjectionDetector(),
    new PiiRedactor(GuardrailPhase.Output),
    new SecretsDetector(),
    new InputLengthLimiter { MaxTokens = 5000 },
]);

var result = await pipeline.EvaluateInputAsync(userInput);
if (!result.IsAllowed)
    Console.WriteLine($"Blocked: {result.Reason}");
```

## Workflow DSL

```json
{
    "id": "content-pipeline",
    "name": "Content Pipeline",
    "nodes": [
        { "id": "research", "name": "Researcher", "description": "Research topic",
          "agent": { "tools": ["web_search"], "budget": { "maxCostUsd": 1.0 } } },
        { "id": "write", "name": "Writer", "description": "Write content" }
    ],
    "edges": [
        { "from": "research", "to": "write" }
    ]
}
```

```csharp
var loader = sp.GetRequiredService<IWorkflowLoader>();
var workflow = loader.LoadFromString(json, "json");
var validation = sp.GetRequiredService<IWorkflowValidator>().Validate(workflow);
```

## Documentation

Full documentation is in the [`docs/`](docs/README.md) directory:

- **Getting Started** — [Installation](docs/getting-started/installation.md) · [Quick Start](docs/getting-started/quickstart.md) · [CLI](docs/getting-started/cli.md)
- **Architecture** — [Overview](docs/architecture/overview.md) · [Core Engine](docs/architecture/core-engine.md)
- **Guides** — [Orchestration](docs/guides/orchestration.md) · [Memory](docs/guides/memory.md) · [Guardrails](docs/guides/guardrails.md) · [Permissions](docs/guides/permissions.md) · [Cost Tracking](docs/guides/cost-tracking.md) · [Messaging](docs/guides/messaging.md) · [Checkpointing](docs/guides/checkpointing.md) · [Workflows DSL](docs/guides/workflows-dsl.md) · [Protocols](docs/guides/protocols.md) · [Telemetry](docs/guides/telemetry.md) · [Auth](docs/guides/auth.md) · [Testing](docs/guides/testing.md) · [Middleware](docs/guides/middleware.md)
- **API Reference** — [Core](docs/api/nexus-core.md) · [Orchestration](docs/api/nexus-orchestration.md) · [Memory](docs/api/nexus-memory.md) · [Guardrails](docs/api/nexus-guardrails.md) · [Permissions](docs/api/nexus-permissions.md) · [Cost Tracking](docs/api/nexus-cost-tracking.md) · [Messaging](docs/api/nexus-messaging.md) · [Workflows DSL](docs/api/nexus-workflows-dsl.md) · [Protocols](docs/api/nexus-protocols.md) · [Testing](docs/api/nexus-testing.md)

## Building & Testing

```bash
dotnet build Nexus.sln
dotnet test Nexus.sln
```

## Project Structure

```
src/
  Nexus.Core/                       Core abstractions & DI
  Nexus.Orchestration/              Agent pool, orchestrator, task graphs
  Nexus.Orchestration.Checkpointing/ Snapshot serialization & storage
  Nexus.Memory/                     Conversation & working memory
  Nexus.Messaging/                  Message bus & shared state
  Nexus.Guardrails/                 Input/output guardrails
    Nexus.Permissions/                Rule-based tool permissions
    Nexus.CostTracking/               Token and cost aggregation
  Nexus.Telemetry/                  OpenTelemetry integration
  Nexus.Auth.OAuth2/                Authentication & token management
  Nexus.Protocols.Mcp/              MCP tool adapter
  Nexus.Protocols.A2A/              A2A protocol client
  Nexus.Protocols.AgUi/             AG-UI event bridge
  Nexus.Workflows.Dsl/              JSON/YAML workflow loader
  Nexus.Hosting.AspNetCore/         ASP.NET Core hosting
  Nexus.Testing/                    Test utilities & mocks
tests/
  Nexus.Core.Tests/                 35 unit tests
  Nexus.Orchestration.Tests/        14 unit tests
  Nexus.Memory.Tests/               12 unit tests
  Nexus.Messaging.Tests/            11 unit tests
  Nexus.Guardrails.Tests/           16 unit tests
    Nexus.Permissions.Tests/          Permissions rules, approval, middleware
    Nexus.CostTracking.Tests/         Usage extraction and aggregation
  Nexus.Workflows.Dsl.Tests/        20 unit tests
  Nexus.Integration.Tests/          9 integration tests + 15 unit tests
examples/
  Nexus.Examples.Minimal/           Single agent with tools & guardrails
  Nexus.Examples.MultiAgent/        Graph orchestration, checkpointing, workflows
```

## License

[MIT](LICENSE)
