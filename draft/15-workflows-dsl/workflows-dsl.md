# Workflows DSL — Nexus.Workflows.Dsl

> Assembly: `Nexus.Workflows.Dsl`  
> Deps: `Nexus.Core`, `Nexus.Orchestration`  
> Status: **Optional** — Code-first bleibt der primäre Weg.

## 1. Warum dieses Paket existiert

Nexus ist Code-first. Aber nicht jeder Consumer ist ein C#-Entwickler. Und nicht jeder Use-Case braucht eine IDE.

**Das Problem:** `ITaskGraph` kann nur in Code erstellt werden. Das macht es unmöglich:
- Workflows in einer Datenbank zu speichern
- Workflows über eine REST API zu laden
- Einen Visual Builder (à la n8n/Node-RED) darüber zu bauen
- Workflows zu versionieren und zwischen Environments zu promoten
- Non-Developers Workflows erstellen zu lassen

**Die Lösung:** Ein serialisierbares `WorkflowDefinition`-Record das einen kompletten Graphen als JSON/YAML beschreibt. Es ist eine **Daten-Repräsentation** des Graphen — die Engine kennt keinen Unterschied.

## 2. Architektur

```
┌─────────────────────────────────────────────────────┐
│ Consumer Layer (nicht Nexus)                         │
│                                                      │
│  Visual Builder     CLI/API        Datei-Editor       │
│  (React/Blazor)    (REST)         (VS Code)          │
│       │               │               │               │
│       ▼               ▼               ▼               │
│  ┌─────────────────────────────────────────────────┐ │
│  │        WorkflowDefinition (JSON/YAML)            │ │
│  │        Serialisierbar, versionierbar             │ │
│  └────────────────────┬────────────────────────────┘ │
├───────────────────────┼──────────────────────────────┤
│ Nexus.Workflows.Dsl   │                              │
│                       ▼                              │
│  IWorkflowLoader.LoadAsync(stream)                   │
│  IWorkflowValidator.ValidateAsync(def)               │
│  WorkflowDefinition.ToTaskGraph(orchestrator)        │
│  ITaskGraph.ToDefinition()                           │
├──────────────────────────────────────────────────────┤
│ Nexus.Orchestration                                  │
│  IOrchestrator.ExecuteGraphAsync(graph)               │
└──────────────────────────────────────────────────────┘
```

### Prinzip: Gleicher Graph, zwei Eingänge

```csharp
// Weg 1: Code-first (direkt)
var graph = orchestrator.CreateGraph();
var r = graph.AddTask(new AgentTask { ... });
var w = graph.AddTask(new AgentTask { ... });
graph.AddDependency(r, w);

// Weg 2: DSL (aus Definition)
var def = await loader.LoadFromFileAsync("workflow.json");
var graph = def.ToTaskGraph(orchestrator);

// Ab hier identisch — die Engine sieht keinen Unterschied
var result = await orchestrator.ExecuteGraphAsync(graph);
```

## 3. WorkflowDefinition

### Record-Struktur

```csharp
/// Vollständige Beschreibung eines Workflows — serialisierbar als JSON/YAML
public record WorkflowDefinition
{
    /// Eindeutige ID des Workflows
    public required string Id { get; init; }

    /// Menschenlesbarer Name
    public required string Name { get; init; }

    /// Optionale Beschreibung
    public string? Description { get; init; }

    /// Versionierung (SemVer)
    public string Version { get; init; } = "1.0.0";

    /// Alle Nodes (Agents/Tasks) im Graphen
    public required IReadOnlyList<NodeDefinition> Nodes { get; init; }

    /// Alle Kanten (Abhängigkeiten) zwischen Nodes
    public IReadOnlyList<EdgeDefinition> Edges { get; init; } = [];

    /// Globale Variablen die in Node-Definitionen referenziert werden können
    public IReadOnlyDictionary<string, object> Variables { get; init; } = new Dictionary<string, object>();

    /// Globale Optionen
    public WorkflowOptions? Options { get; init; }

    /// Metadaten (für UI, Versionierung, etc.)
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

public record NodeDefinition
{
    /// Eindeutige ID innerhalb des Workflows
    public required string Id { get; init; }

    /// Menschenlesbarer Name
    public required string Name { get; init; }

    /// Was der Agent tun soll
    public required string Description { get; init; }

    /// Agent-Konfiguration
    public AgentConfig Agent { get; init; } = new();

    /// Error-Handling für diesen Node
    public ErrorPolicyConfig? ErrorPolicy { get; init; }

    /// Position im Visual Builder (für UI, von Engine ignoriert)
    public NodePosition? Position { get; init; }

    /// Metadaten (Tags, Farben, etc. — für UI)
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

public record AgentConfig
{
    /// Vordefinierter Agent-Typ: "chat" | "custom" | Name einer registrierten Klasse
    public string Type { get; init; } = "chat";

    /// System-Prompt (für chat-Agents)
    public string? SystemPrompt { get; init; }

    /// Welches Modell/Provider (referenziert benannten IChatClient)
    public string? ChatClient { get; init; }

    /// Model ID Override
    public string? ModelId { get; init; }

    /// Tool-Namen die der Agent nutzen darf
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// MCP Server die der Agent nutzen darf
    public IReadOnlyList<string> McpServers { get; init; } = [];

    /// Budget
    public BudgetConfig? Budget { get; init; }

    /// Context Window
    public ContextWindowConfig? ContextWindow { get; init; }
}

public record BudgetConfig
{
    public int? MaxInputTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public decimal? MaxCostUsd { get; init; }
    public int? MaxIterations { get; init; }
    public int? MaxToolCalls { get; init; }
}

public record ContextWindowConfig
{
    public int MaxTokens { get; init; } = 128_000;
    public int TargetTokens { get; init; } = 100_000;
    public string TrimStrategy { get; init; } = "SlidingWindow";
    public int ReservedForOutput { get; init; } = 8_000;
}

public record ErrorPolicyConfig
{
    public int? MaxRetries { get; init; }
    public string? BackoffType { get; init; }
    public string? FallbackChatClient { get; init; }
    public string? FallbackModelId { get; init; }
    public bool EscalateToHuman { get; init; }
    public bool SendToDeadLetter { get; init; }
    public int MaxIterations { get; init; } = 25;
    public int TimeoutSeconds { get; init; } = 300;
}

public record EdgeDefinition
{
    /// Source Node ID
    public required string From { get; init; }

    /// Target Node ID
    public required string To { get; init; }

    /// Optionale Bedingung (Expression die auf AgentResult evaluiert)
    public string? Condition { get; init; }

    /// Context Propagation Strategie
    public ContextPropagationConfig? ContextPropagation { get; init; }
}

public record ContextPropagationConfig
{
    /// "full" | "summarize" | "structured" | "selective"
    public string Strategy { get; init; } = "full";
    public int? MaxTokens { get; init; }
    public string? JsonPath { get; init; }
}

public record WorkflowOptions
{
    public int? MaxConcurrentNodes { get; init; }
    public int? GlobalTimeoutSeconds { get; init; }
    public decimal? MaxTotalCostUsd { get; init; }
    public string? CheckpointStrategy { get; init; }
}

/// Position für Visual Builder — von Engine ignoriert
public record NodePosition(double X, double Y);
```

## 4. JSON Beispiel

```json
{
  "id": "research-and-publish",
  "name": "Research & Publish Pipeline",
  "version": "1.2.0",
  "description": "Recherchiert ein Thema, reviewt den Output und publiziert.",
  "variables": {
    "topic": "AI Agent Orchestration Trends 2026",
    "target_audience": "developers"
  },
  "options": {
    "maxConcurrentNodes": 5,
    "globalTimeoutSeconds": 1800,
    "maxTotalCostUsd": 5.00,
    "checkpointStrategy": "AfterEachNode"
  },
  "nodes": [
    {
      "id": "research",
      "name": "Researcher",
      "description": "Recherchiere '${topic}' für ${target_audience}",
      "agent": {
        "type": "chat",
        "systemPrompt": "Du bist ein gründlicher Research-Agent. Liefere faktenbasierte, strukturierte Ergebnisse.",
        "chatClient": "smart",
        "tools": ["web_search", "file_read"],
        "mcpServers": ["github"],
        "budget": {
          "maxCostUsd": 2.00,
          "maxIterations": 15
        }
      },
      "errorPolicy": {
        "maxRetries": 2,
        "backoffType": "ExponentialWithJitter",
        "fallbackChatClient": "fast",
        "sendToDeadLetter": true
      },
      "position": { "x": 100, "y": 200 }
    },
    {
      "id": "review",
      "name": "Reviewer",
      "description": "Prüfe den Research-Report auf Korrektheit und Vollständigkeit.",
      "agent": {
        "type": "chat",
        "systemPrompt": "Du bist ein kritischer Reviewer. Bewerte Korrektheit, Vollständigkeit und Stil. Gib einen quality_score von 0-1.",
        "chatClient": "smart",
        "budget": { "maxCostUsd": 1.00 }
      },
      "position": { "x": 400, "y": 200 }
    },
    {
      "id": "publish",
      "name": "Publisher",
      "description": "Formatiere und publiziere den geprüften Report.",
      "agent": {
        "type": "chat",
        "systemPrompt": "Du formatierst Texte für die Publikation.",
        "chatClient": "fast",
        "tools": ["publish_to_cms", "generate_social_post"],
        "budget": { "maxCostUsd": 0.50 }
      },
      "errorPolicy": {
        "escalateToHuman": true
      },
      "position": { "x": 700, "y": 200 }
    }
  ],
  "edges": [
    {
      "from": "research",
      "to": "review",
      "contextPropagation": {
        "strategy": "summarize",
        "maxTokens": 4000
      }
    },
    {
      "from": "review",
      "to": "publish",
      "condition": "result.metadata.quality_score >= 0.8",
      "contextPropagation": {
        "strategy": "full"
      }
    }
  ],
  "metadata": {
    "author": "team-ai",
    "tags": ["content", "research", "pipeline"],
    "createdAt": "2026-03-28T10:00:00Z"
  }
}
```

## 5. YAML Beispiel (dasselbe)

```yaml
id: research-and-publish
name: Research & Publish Pipeline
version: "1.2.0"

variables:
  topic: AI Agent Orchestration Trends 2026
  target_audience: developers

options:
  maxConcurrentNodes: 5
  globalTimeoutSeconds: 1800
  maxTotalCostUsd: 5.00
  checkpointStrategy: AfterEachNode

nodes:
  - id: research
    name: Researcher
    description: "Recherchiere '${topic}' für ${target_audience}"
    agent:
      type: chat
      systemPrompt: >
        Du bist ein gründlicher Research-Agent.
        Liefere faktenbasierte, strukturierte Ergebnisse.
      chatClient: smart
      tools: [web_search, file_read]
      mcpServers: [github]
      budget:
        maxCostUsd: 2.00
        maxIterations: 15
    errorPolicy:
      maxRetries: 2
      fallbackChatClient: fast

  - id: review
    name: Reviewer
    description: Prüfe den Research-Report.
    agent:
      type: chat
      systemPrompt: >
        Du bist ein kritischer Reviewer.
        Gib einen quality_score von 0-1.
      chatClient: smart
      budget:
        maxCostUsd: 1.00

  - id: publish
    name: Publisher
    description: Publiziere den Report.
    agent:
      type: chat
      chatClient: fast
      tools: [publish_to_cms]
    errorPolicy:
      escalateToHuman: true

edges:
  - from: research
    to: review
    contextPropagation:
      strategy: summarize
      maxTokens: 4000

  - from: review
    to: publish
    condition: "result.metadata.quality_score >= 0.8"
```

## 6. Interfaces

### IWorkflowLoader

```csharp
public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadFromFileAsync(string path, CancellationToken ct = default);
    Task<WorkflowDefinition> LoadFromStreamAsync(Stream stream, string format = "json", CancellationToken ct = default);
    WorkflowDefinition LoadFromString(string content, string format = "json");
    Task<WorkflowDefinition> LoadFromUriAsync(Uri uri, CancellationToken ct = default);
}
```

### IWorkflowSerializer

```csharp
public interface IWorkflowSerializer
{
    string Serialize(WorkflowDefinition definition, string format = "json");
    Task SerializeToFileAsync(WorkflowDefinition definition, string path, string format = "json", CancellationToken ct = default);
    Task SerializeToStreamAsync(WorkflowDefinition definition, Stream stream, string format = "json", CancellationToken ct = default);
}
```

### IWorkflowValidator

```csharp
public interface IWorkflowValidator
{
    Task<ValidationResult> ValidateAsync(WorkflowDefinition definition, CancellationToken ct = default);
}

// Validiert:
// - Zyklische Abhängigkeiten
// - Unerreichbare Nodes
// - Referenzierte ChatClients existieren
// - Referenzierte Tools existieren
// - Referenzierte MCP Server existieren
// - Budget-Werte sind positiv
// - Conditions sind syntaktisch korrekt
// - Variable Referenzen (${...}) sind auflösbar
```

### IVariableResolver

```csharp
public interface IVariableResolver
{
    string Resolve(string template, IReadOnlyDictionary<string, object> variables);
}

// Ersetzt ${variable} Referenzen in Descriptions und SystemPrompts.
// Built-in: Environment Variables + Inline Variables
```

### IWorkflowTemplateRegistry

```csharp
public interface IWorkflowTemplateRegistry
{
    Task RegisterAsync(string name, WorkflowDefinition template, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowTemplateInfo>> ListAsync(CancellationToken ct = default);
}

// Use Case: Vorgefertigte Workflow-Templates die der User instanziieren kann
// z.B. "content-pipeline", "code-review", "customer-support-triage"
```

### IWorkflowSchemaExporter

```csharp
public interface IWorkflowSchemaExporter
{
    /// Exportiert das JSON Schema für WorkflowDefinition
    /// Damit Visual Builder die Struktur kennen und validieren können
    JsonElement ExportSchema();

    /// Exportiert die aktuell verfügbaren ChatClients, Tools und MCP Server
    /// als Enum-Constraints für das Schema
    AvailableResources ExportAvailableResources();
}

public record AvailableResources(
    IReadOnlyList<string> ChatClientNames,
    IReadOnlyList<ToolInfo> AvailableTools,
    IReadOnlyList<string> McpServerNames);
```

## 7. Roundtrip: Graph ↔ Definition

```csharp
public static class WorkflowDslExtensions
{
    /// Definition → TaskGraph
    public static ITaskGraph ToTaskGraph(
        this WorkflowDefinition definition,
        IOrchestrator orchestrator,
        IAgentPool agentPool,
        IVariableResolver? variables = null)
    {
        // 1. Variablen auflösen
        // 2. Für jeden Node: AgentDefinition erstellen, Agent spawnen
        // 3. Für jede Edge: Dependency + ContextPropagator + Condition hinzufügen
        // 4. Graph validieren
    }

    /// TaskGraph → Definition (für Export, Versionierung, UI)
    public static WorkflowDefinition ToDefinition(this ITaskGraph graph)
    {
        // Reverse: Graph in serialisierbare Definition umwandeln
    }
}
```

## 8. Condition Expressions

Edge Conditions werden als einfache Expressions definiert:

```json
{ "condition": "result.metadata.quality_score >= 0.8" }
{ "condition": "result.status == 'Success'" }
{ "condition": "result.text.contains('approved')" }
```

### IConditionEvaluator

```csharp
public interface IConditionEvaluator
{
    bool Evaluate(string expression, AgentResult result);
}

// Default: Einfacher Expression Parser für:
//   result.status == 'Success'
//   result.metadata.key >= 0.5
//   result.text.contains('xyz')
//   result.estimatedCost < 1.0
//
// Custom: Consumer kann eigenen Evaluator registrieren
// (z.B. C# Scripting, Jint JavaScript, etc.)
```

## 9. Custom Agent Types

```json
{
  "id": "custom-node",
  "agent": {
    "type": "MyNamespace.MyCustomAgent, MyAssembly"
  }
}
```

Wenn `type` kein Built-in ist ("chat"), wird es als vollqualifizierter Typname behandelt. Der Agent muss `IAgent` implementieren und im DI-Container registriert sein.

### Agent Type Registry

```csharp
public interface IAgentTypeRegistry
{
    void Register(string typeName, Func<AgentConfig, IServiceProvider, IAgent> factory);
    IAgent Create(AgentConfig config, IServiceProvider sp);
}

// Built-in:
// "chat" → ChatAgent (Default)
//
// Custom registrieren:
n.AddWorkflowDsl(dsl =>
{
    dsl.RegisterAgentType("reviewer", (config, sp) => new ReviewerAgent(sp));
    dsl.RegisterAgentType("code-analyzer", (config, sp) => new CodeAnalyzerAgent(sp));
});
```

## 10. REST API Pattern (Consumer-Beispiel)

Nexus liefert **kein** REST API, aber der Consumer kann einfach eines bauen:

```csharp
// Workflow via API laden und ausführen
app.MapPost("/api/workflows/execute", async (
    WorkflowDefinition definition,
    IWorkflowValidator validator,
    IOrchestrator orchestrator,
    IAgentPool pool) =>
{
    var validation = await validator.ValidateAsync(definition);
    if (!validation.IsValid)
        return Results.BadRequest(validation.Errors);

    var graph = definition.ToTaskGraph(orchestrator, pool);
    var result = await orchestrator.ExecuteGraphAsync(graph);
    return Results.Ok(result);
});

// Workflow via API laden und streamen
app.MapPost("/api/workflows/stream", async (
    WorkflowDefinition definition,
    IOrchestrator orchestrator,
    IAgentPool pool,
    HttpContext http) =>
{
    var graph = definition.ToTaskGraph(orchestrator, pool);
    http.Response.ContentType = "text/event-stream";

    await foreach (var evt in orchestrator.ExecuteGraphStreamingAsync(graph))
    {
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(evt)}\n\n");
        await http.Response.Body.FlushAsync();
    }
});

// Verfügbare Ressourcen für UI
app.MapGet("/api/workflows/schema", (IWorkflowSchemaExporter exporter) =>
{
    return Results.Ok(new
    {
        schema = exporter.ExportSchema(),
        resources = exporter.ExportAvailableResources()
    });
});
```

## 11. Visual Builder Support

Nexus liefert **keinen** Visual Builder, aber die Infrastruktur damit jeder einen bauen kann:

| Was ein Visual Builder braucht | Was Nexus liefert |
|-------------------------------|-------------------|
| Graph-Struktur als JSON | `WorkflowDefinition` (serialisierbar) |
| Verfügbare Tools/Models | `IWorkflowSchemaExporter.ExportAvailableResources()` |
| Schema für Validierung | `IWorkflowSchemaExporter.ExportSchema()` |
| Node-Positionen (x/y) | `NodeDefinition.Position` (von Engine ignoriert) |
| Metadaten (Farben, Tags) | `NodeDefinition.Metadata` (von Engine ignoriert) |
| Echtzeit-Execution | `ExecuteGraphStreamingAsync()` → AG-UI Events |
| Execution History | Checkpointing + Audit Log |
| Template Library | `IWorkflowTemplateRegistry` |

## 12. Was Code-first kann, was DSL nicht kann

| Feature | Code-first | DSL |
|---------|-----------|-----|
| Beliebige C# Logik in Agents | Ja | Nein (nur registrierte Typen) |
| Lambda-Tools inline | Ja | Nein (Tools müssen registriert sein) |
| Dynamische Graph-Konstruktion | Ja | Nein (statisch definiert) |
| Conditional Edges mit Closures | Ja | Nur Expression Strings |
| Custom IAgent Implementierungen | Direkt | Via Agent Type Registry |
| CompensationAction (Saga) | Delegate | Nicht unterstützt |
| Debugging mit Breakpoints | Ja | Eingeschränkt |
| IDE Autocomplete | Ja | JSON Schema |
| Versionierung in Git | .cs Dateien | .json/.yaml Dateien |
| Von Non-Developers erstellbar | Nein | Ja (mit Visual Builder) |
| In Datenbank speicherbar | Nein (Code) | Ja (JSON String) |
| Via REST API ladbar | Nein | Ja |

**Die Empfehlung:** Code-first für Entwickler. DSL für Workflows die von Non-Developers erstellt oder über eine UI gesteuert werden sollen. Beide produzieren denselben `ITaskGraph`.

## 13. Ordnerstruktur

```
Nexus.Workflows.Dsl/
├── WorkflowDefinition.cs          // Haupt-Record + alle Sub-Records
├── NodeDefinition.cs
├── EdgeDefinition.cs
├── AgentConfig.cs
├── BudgetConfig.cs
├── ErrorPolicyConfig.cs
├── WorkflowOptions.cs
├── Interfaces/
│   ├── IWorkflowLoader.cs
│   ├── IWorkflowSerializer.cs
│   ├── IWorkflowValidator.cs
│   ├── IVariableResolver.cs
│   ├── IConditionEvaluator.cs
│   ├── IAgentTypeRegistry.cs
│   ├── IWorkflowTemplateRegistry.cs
│   └── IWorkflowSchemaExporter.cs
├── Defaults/
│   ├── JsonWorkflowLoader.cs
│   ├── YamlWorkflowLoader.cs
│   ├── DefaultWorkflowValidator.cs
│   ├── DefaultVariableResolver.cs
│   ├── SimpleConditionEvaluator.cs
│   ├── DefaultAgentTypeRegistry.cs
│   └── InMemoryTemplateRegistry.cs
├── Extensions/
│   ├── WorkflowDslExtensions.cs     // ToTaskGraph, ToDefinition
│   └── WorkflowDslBuilderExtensions.cs  // n.AddWorkflowDsl()
└── Schema/
    └── WorkflowSchemaExporter.cs
```

## 14. Registrierung

```csharp
services.AddNexus(n =>
{
    n.AddOrchestration();
    n.AddWorkflowDsl(dsl =>
    {
        // Custom Agent Types
        dsl.RegisterAgentType("reviewer", (config, sp) => new ReviewerAgent(sp));

        // Custom Condition Evaluator (z.B. Jint für JS Expressions)
        dsl.UseConditionEvaluator<JintConditionEvaluator>();

        // Custom Variable Resolver
        dsl.UseVariableResolver<DatabaseVariableResolver>();

        // Templates laden
        dsl.AddTemplatesFromDirectory("./workflow-templates/");
    });
});
```
