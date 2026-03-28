# Nexus — Architektur & Gesamtspec v0.4

## 1. Was Nexus ist

Nexus ist eine **Pluggable Engine** für Multi-Agent-Orchestrierung in .NET. Sie liefert Bausteine (Interfaces, Pipelines, Primitives) aus denen der Consumer sein System zusammensteckt.

### Fünf Regeln

1. **Build on, don't replace.** Baut auf `IChatClient` (M.E.AI), offiziellem MCP C# SDK und .NET DI auf.
2. **Alles ist ein Interface.** Jede Komponente austauschbar via DI. Nichts `sealed`, keine Pflicht-Vererbung.
3. **Middleware-Pipeline für alles.** Agent, Tool, Message Execution — composable Chains.
4. **Opt-in, nicht Opt-out.** Nur was registriert wird, existiert.
5. **Streaming ist der Normalfall.** Dual-API überall: `Task<T>` + `IAsyncEnumerable<Event>`.

### Zwei Workflow-Paradigmen

Nexus unterstützt **Code-first** (primär) und **DSL-defined** (optional) Workflows:

```
Visual Builder (n8n-artig)     YAML/JSON DSL          C# Code-first
        │                          │                       │
        ▼                          ▼                       │
   WorkflowDefinition (JSON)  ←───────────────────────────┘
        │                                 ▲
        ▼                                 │
   ITaskGraph.LoadFrom(def)    ITaskGraph.ToDefinition()
        │
        ▼
   IOrchestrator.ExecuteGraphAsync(graph)
```

**Code-first** = der primäre, volle Weg. Volle Kontrolle, IDE-Support, testbar.
**DSL-defined** = optional via `Nexus.Workflows.Dsl`. Serialisierbar, versionierbar, UI-tauglich.

Beides produziert denselben `ITaskGraph`. Die Engine kennt keinen Unterschied.

## 2. Schichten-Modell

```
LAYER 5: Consumer Applications (nicht Teil von Nexus)
  Visual Workflow Builder | CLI Tool | Web Dashboard | Custom App

LAYER 4: Hosting & Endpoints (optional)
  ASP.NET Core: MapMcpEndpoint, MapA2AEndpoint, MapAgUiEndpoint
  Health Checks, Metrics Endpoints

LAYER 3: Protocols (optional)
  MCP (Agent↔Tools) | A2A (Agent↔Agent) | AG-UI (Agent↔Frontend)

LAYER 2: Capabilities (optional)
  Orchestration | Messaging | Guardrails | Memory | Checkpointing
  Auth | Rate Limiting | Telemetry | Testing | Workflows DSL

LAYER 1: Core (einzige Pflicht)
  IAgent | ITool | Events | Pipeline | Auth | Contracts

FOUNDATION: .NET Ecosystem (nicht von Nexus)
  Microsoft.Extensions.AI | ModelContextProtocol SDK | M.E.DI
```

## 3. Assembly-Abhängigkeitsgraph

```
Consumer App
    │
    ├──► Nexus.Hosting.AspNetCore ──► Nexus.Protocols.*
    ├──► Nexus.Workflows.Dsl ────────────────────────────┐
    ├──► Nexus.Orchestration                              │
    ├──► Nexus.Orchestration.Checkpointing                │
    ├──► Nexus.Messaging                                  │
    ├──► Nexus.Guardrails                                 │
    ├──► Nexus.Memory                                     │
    ├──► Nexus.Auth.OAuth2                                │
    ├──► Nexus.Telemetry                                  │
    ├──► Nexus.Testing                                    │
    │         │                                           │
    │         ▼ (alle referenzieren nur)                   │
    │    Nexus.Core ◄─────────────────────────────────────┘
    │         │
    │         ▼
    │    Microsoft.Extensions.AI.Abstractions
    │
    └──► Infrastructure Adapter (Redis, Qdrant, Postgres, ...)
```

## 4. Vollständige Feature-Map

### 4.1 Core Engine (`Nexus.Core`)

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Agent-Vertrag | `IAgent` | — | Ja |
| Agent-Kontext | `IAgentContext` | `DefaultAgentContext` | Ja |
| Tool-Vertrag | `ITool` | — | Ja |
| Tool-Registry | `IToolRegistry` | `DefaultToolRegistry` | Ja |
| Tool-Annotations | `ToolAnnotations` | — | — |
| Auth-Strategie | `IAuthStrategy` | — | Ja |
| Agent Middleware | `IAgentMiddleware` | — | Erweiterbar |
| Tool Middleware | `IToolMiddleware` | — | Erweiterbar |
| Message Middleware | `IMessageMiddleware` | — | Erweiterbar |
| Correlation | `CorrelationContext` | Auto-propagated | Ja |
| Approval Gate | `IApprovalGate` | `AutoApproveGate` | Ja |
| Budget Tracker | `IBudgetTracker` | `DefaultBudgetTracker` | Ja |
| Rate Limiter | `IRateLimiter` | Token Bucket | Ja |
| Secret Provider | `ISecretProvider` | Environment Vars | Ja |
| Audit Log | `IAuditLog` | `NullAuditLog` | Ja |
| Context Window Mgr | `IContextWindowManager` | Default | Ja |

### 4.2 Orchestration (`Nexus.Orchestration`)

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Agent Pool | `IAgentPool` | `DefaultAgentPool` | Ja |
| Task Graph (DAG) | `ITaskGraph` | `DefaultTaskGraph` | Ja |
| Orchestrator | `IOrchestrator` | `DefaultOrchestrator` | Ja |
| Scheduler | `IScheduler` | `DefaultScheduler` | Ja |
| Chat Agent | `ChatAgent : IAgent` | Built-in | Optional |
| Context Propagator | `IContextPropagator` | `FullPassthrough` | Ja |
| Concurrency Limiter | `IConcurrencyLimiter` | Semaphore | Ja |
| LLM Router | `IChatClientRouter` | `FallbackRouter` | Ja |
| Error Policy | `TaskErrorPolicy` | Defaults | Konfigurierbar |

### 4.3 Workflows DSL (`Nexus.Workflows.Dsl`) — NEU

| Feature | Interface/Klasse | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Workflow Definition | `WorkflowDefinition` | — (Record) | — |
| Node Definition | `NodeDefinition` | — (Record) | — |
| Edge Definition | `EdgeDefinition` | — (Record) | — |
| Definition Loader | `IWorkflowLoader` | JSON + YAML | Ja |
| Definition Serializer | `IWorkflowSerializer` | JSON + YAML | Ja |
| Graph ↔ Definition | Extension Methods | Built-in | — |
| Definition Validator | `IWorkflowValidator` | `DefaultValidator` | Ja |
| Variable Resolution | `IVariableResolver` | Environment + Inline | Ja |
| Template Registry | `IWorkflowTemplateRegistry` | InMemory | Ja |
| Schema Export | `IWorkflowSchemaExporter` | JSON Schema | Ja |

### 4.4 Checkpointing (`Nexus.Orchestration.Checkpointing`)

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Checkpoint Store | `ICheckpointStore` | InMemory | Ja |
| Snapshot Serializer | `ISnapshotSerializer` | JSON | Ja |
| Resume | `IOrchestrator.ResumeAsync()` | Built-in | — |

### 4.5 Messaging (`Nexus.Messaging`)

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Message Bus | `IMessageBus` | InMemory | Ja |
| Shared State | `ISharedState` | InMemory | Ja |
| Dead Letter Queue | `IDeadLetterQueue` | InMemory | Ja |

### 4.6 Guardrails (`Nexus.Guardrails`)

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Guardrail Pipeline | `IGuardrailPipeline` | Default | Ja |
| Individual Guard | `IGuardrail` | 8 Built-in | Erweiterbar |
| Prompt Injection | `PromptInjectionDetector` | Regex+Heuristik | Ja |
| PII Redactor | `PiiRedactor` | Regex | Ja |
| Topic Guard | `TopicGuard` | Keywords | Ja |
| Tool Arg Validator | `ToolArgumentValidator` | JSON Schema | Ja |
| Indirect Injection | `IndirectInjectionDetector` | Heuristik | Ja |

### 4.7 Memory (`Nexus.Memory`)

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Conversation Store | `IConversationStore` | InMemory | Ja |
| Working Memory | `IWorkingMemory` | InMemory | Ja |
| Long-Term Memory | `ILongTermMemory` | — (braucht Store) | Ja |
| Context Window Mgr | `IContextWindowManager` | Default | Ja |

### 4.8 Protocols

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| MCP Host | `IMcpHostManager` | Built-in (offizielles SDK) | Ja |
| MCP Server Expose | `MapMcpEndpoint()` | Built-in | Konfigurierbar |
| A2A Client | `IA2AClient` | `HttpA2AClient` | Ja |
| A2A Server | `MapA2AEndpoint()` | Built-in | Konfigurierbar |
| AG-UI Emitter | `IAgUiEventEmitter` | Default | Ja |
| AG-UI Bridge | `AgUiEventBridge` | Built-in | Erweiterbar |

### 4.9 Cross-Cutting

| Feature | Interface | Default | Austauschbar |
|---------|-----------|---------|-------------|
| Rate Limiting | `IRateLimiter` | Token Bucket | Ja |
| Secrets | `ISecretProvider` | Environment | Ja |
| Audit | `IAuditLog` | Noop | Ja |
| Human-in-Loop | `IApprovalGate` | AutoApprove | Ja |
| Health Checks | ASP.NET `IHealthCheck` | Built-in | Erweiterbar |
| Telemetry | OpenTelemetry Activities | Auto-instrumented | Konfigurierbar |
| Budget | `IBudgetTracker` | Default | Ja |

### 4.10 Testing (`Nexus.Testing`)

| Feature | Klasse |
|---------|--------|
| Mock Agent | `MockAgent` |
| Fake ChatClient | `FakeChatClient` |
| Mock Tool | `MockTool` |
| Mock Approval Gate | `MockApprovalGate` |
| Event Recorder | `EventRecorder` |
| Fluent Assertions | `EventAssertions` |
| Agent Evaluator | `IAgentEvaluator` |
| Test Host | `NexusTestHost` |

## 5. Streaming: Der primäre Datenpfad

Jedes Interface folgt dem Dual-API Pattern:

```csharp
public interface IAgent
{
    Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct);
    IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(AgentTask task, IAgentContext ctx, CancellationToken ct);
}
```

**Buffered ist ein Wrapper um Streaming. Nie umgekehrt.**

Event-Topologie:
```
Agent.ExecuteStreamingAsync()
  → AgentMiddleware Pipeline (streaming)
    → IChatClient.GetStreamingResponseAsync()  → TextChunkEvents
    → ITool.ExecuteStreamingAsync()             → ToolEvents
  → IOrchestrator (mergt via Channel<T>)        → OrchestrationEvents
    → AgUiEventBridge                           → AG-UI SSE/WebSocket
```

## 6. Bootstrap-Level

### Minimal (5 Zeilen)
```csharp
services.AddNexus(n =>
{
    n.UseChatClient(sp => new OpenAIClient(key).GetChatClient("gpt-4o").AsIChatClient());
    n.AddOrchestration();
});
```

### Standard (mit Tools + MCP)
```csharp
services.AddNexus(n =>
{
    n.UseChatClient("fast", sp => ollamaClient);
    n.UseChatClient("smart", sp => anthropicClient);
    n.UseRouter<FallbackRouter>();
    n.AddOrchestration();
    n.AddMcp(mcp => mcp.AddServer("github", new StdioTransport("npx", ["-y", "@mcp/server-github"])));
    n.AddTools(t => t.AddFromAssembly());
});
```

### DSL (Workflow aus JSON)
```csharp
services.AddNexus(n =>
{
    n.UseChatClient("openai", sp => openAiClient);
    n.AddOrchestration();
    n.AddWorkflowDsl();
});

var loader = sp.GetRequiredService<IWorkflowLoader>();
var definition = await loader.LoadFromFileAsync("workflow.json");
var graph = definition.ToTaskGraph(orchestrator);
var result = await orchestrator.ExecuteGraphAsync(graph);
```

### Enterprise (alles)
```csharp
services.AddNexus(n =>
{
    n.UseChatClient("openai", sp => openAiClient);
    n.UseChatClient("anthropic", sp => anthropicClient);
    n.UseChatClient("local", sp => ollamaClient);
    n.UseRouter<CostAwareRouter>();

    n.AddOrchestration(o =>
    {
        o.UseAgentMiddleware<TelemetryMiddleware>();
        o.UseAgentMiddleware<GuardrailMiddleware>();
        o.UseAgentMiddleware<BudgetGuardMiddleware>();
        o.UseToolMiddleware<ToolAuditMiddleware>();
        o.MaxConcurrentAgents = 20;
    });

    n.AddCheckpointing(c => c.UsePostgres(connStr));
    n.AddMessaging(m => m.UseRedis(redisConn));
    n.AddGuardrails(g =>
    {
        g.Add<PromptInjectionDetector>(GuardrailPhase.Input);
        g.Add<PiiRedactor>(GuardrailPhase.Input);
        g.Add<PiiRedactor>(GuardrailPhase.Output);
        g.Add<IndirectInjectionDetector>(GuardrailPhase.ToolResult);
        g.RunInParallel = true;
    });
    n.AddMemory(m => { m.UseRedis(redisConn); m.UseQdrant(qdrantUri); });
    n.AddMcp(mcp =>
    {
        mcp.AddServer("github", new StdioTransport("npx", ["-y", "@mcp/server-github"]));
        mcp.AddServer("internal", new HttpSseTransport(new Uri(mcpUrl))
        {
            Auth = new McpAuthConfig { AuthStrategy = new OAuth2ClientCredentials(...) }
        });
    });
    n.AddA2A();
    n.AddAgUi();
    n.AddWorkflowDsl();
    n.AddSecrets(s => s.UseAzureKeyVault(vaultUri));
    n.AddRateLimiting(r => r.ForProvider("openai", new() { RequestsPerMinute = 500 }));
    n.AddTelemetry(t => t.UseOpenTelemetry());
    n.AddAuditLog<PostgresAuditLog>();
});

app.MapMcpEndpoint("/mcp");
app.MapA2AEndpoint("/a2a");
app.MapAgUiEndpoint("/agent/stream");
app.MapHealthChecks("/health");
```

## 7. Target Stack

| Aspekt | Entscheidung |
|--------|-------------|
| Runtime | .NET 8 LTS+ |
| Language | C# 12+ |
| AI Abstractions | `Microsoft.Extensions.AI` v10.x |
| MCP | `ModelContextProtocol` v1.0 (offizielles C# SDK) |
| Async | `IAsyncEnumerable<T>`, `Channel<T>` |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Serialization | `System.Text.Json` (Source Generators, AOT-ready) |
| Resilience | `Microsoft.Extensions.Resilience` (Polly v8) |
| gRPC | `Grpc.Net.Client` (A2A v0.3) |
| Logging | `Microsoft.Extensions.Logging` |
| Telemetry | `System.Diagnostics.Activity` + OpenTelemetry |
| Testing | `xUnit` + `NSubstitute` + `Verify` + `Testcontainers` |

## 8. Nicht-Ziele

- Kein eigener DI-Container, Logger, Serializer, Resilience-Framework
- Kein eigenes `IModelProvider` — nutze `IChatClient`
- Kein UI/Dashboard — AG-UI Events für jedes Frontend
- Kein Skill-Marketplace — Tools via Consumer oder MCP Server
- Keine eigene Vector-DB — Adapter für existierende
- Keine erzwungene Projektstruktur
- Keine Base-Classes die Vererbung erzwingen
- Kein Hosting, kein eigener Prozess — Nexus ist eine Library
- Kein Visual Workflow Builder — aber serialisierbare Definitionen damit jeder einen bauen KANN
