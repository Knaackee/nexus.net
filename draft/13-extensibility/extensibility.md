# Extensibility — Pluggable Architecture

## 1. Drei Wege zu erweitern

### Weg 1: Interface implementieren

Jede Nexus-Komponente ist ein Interface. Implementiere es und registriere deine Version via DI.

```csharp
// Eigener Agent
public class MyAgent : IAgent { ... }

// Eigener Checkpoint Store
public class CosmosCheckpointStore : ICheckpointStore { ... }

// Eigener Guardrail
public class ComplianceGuard : IGuardrail { ... }

// Eigener Memory Store
public class MongoMemoryStore : ILongTermMemory { ... }
```

### Weg 2: Middleware einhängen

Middleware-Pipelines für Agent Execution, Tool Execution und Messaging. Reihenfolge = Ausführungsreihenfolge.

```csharp
n.AddOrchestration(o =>
{
    o.UseAgentMiddleware<LoggingMiddleware>();       // 1. Logging
    o.UseAgentMiddleware<GuardrailMiddleware>();     // 2. Guardrails
    o.UseAgentMiddleware<BudgetGuardMiddleware>();   // 3. Budget
    o.UseAgentMiddleware<TelemetryMiddleware>();     // 4. Tracing
    o.UseToolMiddleware<ToolAuditMiddleware>();      // Tool-Pipeline
});
```

### Weg 3: Extension Methods

Third-Party NuGet-Pakete können Nexus erweitern:

```csharp
// In einem separaten NuGet-Paket:
public static class AcmeNexusExtensions
{
    public static NexusBuilder AddAcmeDefaults(this NexusBuilder builder)
    {
        return builder
            .AddGuardrails(g => g.Add<AcmeComplianceGuard>())
            .AddSecrets(s => s.UseAcmeVault("..."))
            .AddTelemetry(t => t.ExportToAcmeDashboard());
    }
}

// Consumer:
services.AddNexus(n => n.AddAcmeDefaults().AddOrchestration());
```

## 2. Extension-Point-Referenz

| Was erweitern? | Interface | Aufwand |
|---------------|-----------|--------|
| Neuen LLM Provider | `IChatClient` (M.E.AI) | ~100 LOC |
| Neues Tool | `ITool` | ~20 LOC |
| Tool per Lambda | `LambdaTool` | 1 Zeile |
| Tool per AIFunction | `AIFunctionFactory.Create()` | 1 Zeile |
| MCP Server anbinden | `mcp.AddServer(name, transport)` | 1 Zeile |
| Nexus als MCP Server | `app.MapMcpEndpoint(path)` | 1 Zeile |
| Custom Agent | `IAgent` | ~50 LOC |
| Agent Middleware | `IAgentMiddleware` | ~30 LOC |
| Tool Middleware | `IToolMiddleware` | ~30 LOC |
| Message Middleware | `IMessageMiddleware` | ~30 LOC |
| Custom Routing | `IChatClientRouter` | ~50 LOC |
| Custom Auth | `IAuthStrategy` | ~50 LOC |
| MCP Resource | `IResource` / `[NexusResource]` | ~20 LOC |
| Custom Memory | `ILongTermMemory` | ~100 LOC |
| Custom State Backend | `ISharedState` | ~50 LOC |
| Custom MessageBus | `IMessageBus` | ~80 LOC |
| Custom Checkpoint Store | `ICheckpointStore` | ~80 LOC |
| Custom Guardrail | `IGuardrail` | ~30 LOC |
| Custom Approval Gate | `IApprovalGate` | ~30 LOC |
| Custom Secret Provider | `ISecretProvider` | ~20 LOC |
| Custom Audit Log | `IAuditLog` | ~50 LOC |
| Custom Context Propagator | `IContextPropagator` | ~40 LOC |
| Custom Rate Limiter | `IRateLimiter` | ~50 LOC |
| Custom Agent Type (DSL) | `IAgentTypeRegistry.Register()` | ~30 LOC |
| Custom Condition Evaluator | `IConditionEvaluator` | ~40 LOC |
| Custom Variable Resolver | `IVariableResolver` | ~20 LOC |
| Workflow Templates | `IWorkflowTemplateRegistry` | ~10 LOC |
| Custom Workflow Loader | `IWorkflowLoader` | ~50 LOC |

## 3. Was Nexus NICHT tut

- Kein eigener DI-Container → `Microsoft.Extensions.DependencyInjection`
- Kein eigenes Logging → `Microsoft.Extensions.Logging`
- Kein eigener Serializer → `System.Text.Json`
- Kein eigenes Resilience → Polly v8
- Kein eigenes `IModelProvider` → `IChatClient`
- Kein eigenes MCP → offizielles SDK
- Kein Assembly-Scanning als Pflicht (optional via `AddFromAssembly()`)
- Keine Base-Classes die Vererbung erzwingen
