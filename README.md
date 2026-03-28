# Nexus — Multi-Agent Orchestration Engine

[![CI](https://github.com/your-org/nexus/actions/workflows/ci.yml/badge.svg)](https://github.com/your-org/nexus/actions/workflows/ci.yml)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Nexus** is an enterprise-grade .NET 8 framework for building, orchestrating, and managing multi-agent AI systems. It provides a composable architecture for agent execution, tool integration, memory management, guardrails, protocol support (MCP, A2A, AG-UI), and workflow orchestration.

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
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Tools;
using Nexus.Memory;
using Nexus.Orchestration;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => myLlmClient);       // Plug in any IChatClient
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddMemory(m => m.UseInMemory());
});

var sp = services.BuildServiceProvider();

// Register tools
var tools = sp.GetRequiredService<IToolRegistry>();
tools.Register(new LambdaTool("get_time", "Current UTC time",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O")))));

// Spawn an agent and execute
var pool = sp.GetRequiredService<IAgentPool>();
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "You are a helpful assistant.",
});

var orchestrator = sp.GetRequiredService<IOrchestrator>();
var result = await orchestrator.ExecuteSequenceAsync([
    new AgentTask { Id = TaskId.New(), Description = "Hello!", AssignedAgent = agent.Id }
]);

Console.WriteLine(result.TaskResults.Values.First().Text);
```

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
  Nexus.Workflows.Dsl.Tests/        20 unit tests
  Nexus.Integration.Tests/          9 integration tests + 15 unit tests
examples/
  Nexus.Examples.Minimal/           Single agent with tools & guardrails
  Nexus.Examples.MultiAgent/        Graph orchestration, checkpointing, workflows
```

## License

[MIT](LICENSE)
