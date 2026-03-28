# Getting Started

## 1. Installation

```bash
# Minimal — nur Core
dotnet add package Nexus.Core

# Standard — mit Orchestrierung
dotnet add package Nexus.Orchestration

# MCP Tools
dotnet add package Nexus.Protocols.Mcp

# Guardrails
dotnet add package Nexus.Guardrails

# ASP.NET Hosting
dotnet add package Nexus.Hosting.AspNetCore

# Testing
dotnet add package Nexus.Testing
```

## 2. Minimal: Ein Agent, ein LLM

```csharp
using Nexus.Core;
using Nexus.Orchestration;
using Microsoft.Extensions.AI;

var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureServices(services =>
{
    services.AddNexus(n =>
    {
        n.UseChatClient(sp =>
            new OpenAIClient(apiKey).GetChatClient("gpt-4o").AsIChatClient());
        n.AddOrchestration();
    });
});

var host = builder.Build();
var pool = host.Services.GetRequiredService<IAgentPool>();

var agent = await pool.SpawnAsync(ChatAgent.Define(
    name: "Assistant",
    systemPrompt: "Du bist ein hilfreicher Assistent."
));

// Buffered:
var result = await agent.ExecuteAsync(
    new AgentTask { Id = TaskId.New(), Description = "Was ist .NET?" },
    host.Services.GetRequiredService<IAgentContext>());

Console.WriteLine(result.Text);

// Streaming:
await foreach (var chunk in agent.ExecuteStreamingAsync(task, ctx).TextChunksOnly())
{
    Console.Write(chunk);
}
```

## 3. Mit Tools

```csharp
services.AddNexus(n =>
{
    n.UseChatClient(sp => openAiClient);
    n.AddOrchestration();
    n.AddTools(t =>
    {
        // Inline-Tool:
        t.Add(new LambdaTool("get_time", "Returns current time",
            (_, _, ct) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("o")))));

        // Klassen-Tool:
        t.Add<WebSearchTool>();

        // Alle aus Assembly:
        t.AddFromAssembly();
    });
});
```

## 4. Mit MCP Server

```csharp
services.AddNexus(n =>
{
    n.UseChatClient(sp => openAiClient);
    n.AddOrchestration();
    n.AddMcp(mcp =>
    {
        mcp.AddServer("github", new StdioTransport("npx", ["-y", "@mcp/server-github"]));
        mcp.AddServer("filesystem", new StdioTransport("npx", ["-y", "@mcp/server-filesystem"]));
    });
});
```

## 5. Multi-Agent Pipeline

```csharp
var pool = sp.GetRequiredService<IAgentPool>();
var orchestrator = sp.GetRequiredService<IOrchestrator>();

var researcher = await pool.SpawnAsync(ChatAgent.Define(
    name: "Researcher", systemPrompt: "Du recherchierst gründlich.",
    tools: ["web_search"], budget: new(MaxCostUsd: 1.00m)));

var writer = await pool.SpawnAsync(ChatAgent.Define(
    name: "Writer", systemPrompt: "Du schreibst professionelle Texte.",
    budget: new(MaxCostUsd: 0.50m)));

var graph = orchestrator.CreateGraph();
var r = graph.AddTask(new() { Id = TaskId.New(), Description = "Recherchiere AI Trends 2026", AssignedAgent = researcher.Id });
var w = graph.AddTask(new() { Id = TaskId.New(), Description = "Schreibe einen Blogpost", AssignedAgent = writer.Id });
graph.AddDependency(r, w);

// Streaming:
await foreach (var evt in orchestrator.ExecuteGraphStreamingAsync(graph))
{
    switch (evt)
    {
        case AgentEventInGraph { InnerEvent: TextChunkEvent t }:
            Console.Write(t.Text);
            break;
        case NodeCompletedEvent n:
            Console.WriteLine($"\n✅ {n.NodeId} done");
            break;
    }
}
```

## 6. Workflow aus JSON/YAML (DSL)

```bash
dotnet add package Nexus.Workflows.Dsl
```

```csharp
services.AddNexus(n =>
{
    n.UseChatClient("smart", sp => anthropicClient);
    n.UseChatClient("fast", sp => ollamaClient);
    n.AddOrchestration();
    n.AddTools(t => t.Add<WebSearchTool>());
    n.AddWorkflowDsl();
});

// Workflow aus Datei laden und ausführen:
var loader = sp.GetRequiredService<IWorkflowLoader>();
var definition = await loader.LoadFromFileAsync("workflow.yaml");

var validator = sp.GetRequiredService<IWorkflowValidator>();
var validation = await validator.ValidateAsync(definition);
if (!validation.IsValid) throw new Exception(string.Join(", ", validation.Errors));

var graph = definition.ToTaskGraph(orchestrator, pool);

// Streaming:
await foreach (var evt in orchestrator.ExecuteGraphStreamingAsync(graph))
{
    switch (evt)
    {
        case AgentEventInGraph { InnerEvent: TextChunkEvent t }:
            Console.Write(t.Text);
            break;
        case NodeCompletedEvent n:
            Console.WriteLine($"\n✅ {n.NodeId} done");
            break;
    }
}
```

Workflow-Datei `workflow.yaml`:
```yaml
id: quick-research
name: Quick Research
nodes:
  - id: research
    name: Researcher
    description: "Recherchiere das Thema"
    agent:
      chatClient: smart
      tools: [web_search]
      budget: { maxCostUsd: 1.00 }
  - id: summarize
    name: Summarizer
    description: "Fasse die Ergebnisse zusammen"
    agent:
      chatClient: fast
edges:
  - from: research
    to: summarize
```

## 7. Enterprise Setup

```csharp
services.AddNexus(n =>
{
    // Multi-Provider
    n.UseChatClient("openai", sp => openAiClient);
    n.UseChatClient("anthropic", sp => anthropicClient);
    n.UseChatClient("local", sp => ollamaClient);
    n.UseRouter<FallbackRouter>();

    // Orchestration + Checkpointing
    n.AddOrchestration(o =>
    {
        o.UseAgentMiddleware<TelemetryMiddleware>();
        o.UseAgentMiddleware<GuardrailMiddleware>();
        o.UseAgentMiddleware<BudgetGuardMiddleware>();
    });
    n.AddCheckpointing(c => c.UsePostgres(connStr));

    // Guardrails
    n.AddGuardrails(g =>
    {
        g.Add<PromptInjectionDetector>(GuardrailPhase.Input);
        g.Add<PiiRedactor>(GuardrailPhase.Input);
        g.Add<PiiRedactor>(GuardrailPhase.Output);
        g.RunInParallel = true;
    });

    // Memory
    n.AddMemory(m => { m.UseRedis(redisConn); m.UseQdrant(qdrantUri); });

    // Protocols
    n.AddMcp(mcp => mcp.AddServer("internal", new HttpSseTransport(new Uri(mcpUrl))));
    n.AddA2A();
    n.AddAgUi();
    n.AddWorkflowDsl();

    // Security
    n.AddSecrets(s => s.UseAzureKeyVault(vaultUri));
    n.AddRateLimiting(r => r.ForProvider("openai", new() { RequestsPerMinute = 500 }));
    n.AddTelemetry(t => t.UseOpenTelemetry());
    n.AddAuditLog<PostgresAuditLog>();
});

// ASP.NET Endpoints
app.MapMcpEndpoint("/mcp");
app.MapA2AEndpoint("/a2a");
app.MapAgUiEndpoint("/agent/stream");
app.MapHealthChecks("/health");
```

## 8. Nächste Schritte

1. Lies die [Core Engine](../01-core/core-engine.md) Doku für Interface-Details
2. Lies [Streaming](../03-streaming/streaming.md) für Event-Patterns
3. Lies [Workflows DSL](../15-workflows-dsl/workflows-dsl.md) für JSON/YAML Workflows
4. Lies [Testing](../10-testing/testing.md) für Unit-Test Setup
5. Lies [Observability](../11-observability/observability.md) für Tracing/Metrics
