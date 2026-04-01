# All-Together Plan: Nexus Building Blocks Roadmap

## Architektur-Prinzip

**Alles ist optional. Alles komponiert. Sensible Defaults für alles.**

```
"Ich will nur einen Agent mit Tools"         → ChatAgent + ToolRegistry
"Ich will Budget-Kontrolle"                  → + BudgetGuardMiddleware (schon da)
"Ich will eine Session mit Auto-Compaction"  → + AgentLoop + CompactionService
"Ich will Multi-Agent Workflows"             → + Orchestrator (schon da)
"Ich will dynamisches Routing"               → + AgentLoop + AgentRouterStrategy
"Ich will alles mit einem Befehl"            → Nexus.Defaults — batteries included
```

Der Consumer entscheidet wie viel er braucht. Nichts ist erzwungen.

## Status-Stand

Stand: 2026-04-01

- Erledigt: `Nexus.CostTracking` inkl. DI-Wiring, Tests, Doku und Beispielintegration
- Erledigt: `Nexus.Permissions` inkl. Rule-Based Approval und Tool-Middleware
- Erledigt: Tool-Concurrency-Basis via `IToolExecutor` und `PartitionedToolExecutor`
- Erledigt: `ChatAgent`-Integration fuer Tool-Approval, Token-/Cost-Propagation und Budget-Events
- Erledigt: Default-Orchestrator-Pfad nutzt Middleware, wiederverwendet `AssignedAgent` und enforced `MaxCostUsd`
- Erledigt: `Nexus.Compaction` Basis mit Token-Counter, Context-Monitor, `DefaultCompactionService`, Micro-/Summary-Strategien, Post-Compaction-Recall und DI-Wiring
- Erledigt: `Nexus.Sessions` Basis mit `ISessionStore`, `ISessionTranscript`, InMemory/FileSystem-Store und Resume-Integration im AgentLoop
- Erledigt: `Nexus.Configuration` mit Dateistore, Merge-Hierarchie, Runtime-Overrides und Tests
- Teilweise erledigt: `Nexus.Tools.Standard` mit File-/Search-/Shell-/Web-/User-/Agent-Tools, Registry-Discovery, Sandboxing und Tests
- Teilweise erledigt: `Nexus.AgentLoop` Basis mit `IAgentLoop`, `DefaultAgentLoop`, Event-Streaming, Session-Persistierung, `MaxTurns`-/`StopWhen`-/Compaction-Integration sowie sequentieller `WorkflowRoutingStrategy`
- Weitgehend erledigt: `Nexus.Defaults` mit `AddDefaults(...)`, `Nexus.CreateDefault(...)`, Batteries-included-Wiring fuer die vorhandenen Kernpakete, Standard-Tools, Commands/Skills und Tests
- Erledigt: `Nexus.Commands` als leichtgewichtiges Command-Package mit Registry/Dispatcher, Markdown-Loadern, gemeinsamem `DelegateCommand`, paketdefinierten Framework-Builtins (`/help`, `/quit`, `/status`, `/resume`, `/cost`, `/model`, `/clear`, `/compact`), Defaults-Wiring und CLI-Integration
- Erledigt: `Nexus.Skills` als leichtgewichtiges Skill-Package mit Katalog/AgentDefinition-Komposition, Markdown-Loadern, Relevanzermittlung, Middleware-Injection, Defaults-Wiring und CLI-Integration

---

## Layer-Architektur

```
┌─────────────────────────────────────────────────────────────┐
│  Layer 6: Applications                                      │
│  ┌───────────┐ ┌────────────────┐ ┌──────────────────────┐  │
│  │  Minimal  │ │  Multi-Agent   │ │   Full CLI (Agent)   │  │
│  │  Example  │ │   Example      │ │   Comparable to CC   │  │
│  └───────────┘ └────────────────┘ └──────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Layer 5: DX Convenience                                    │
│  ┌──────────┐ ┌──────────┐ ┌─────────┐ ┌────────────────┐  │
│  │ Commands │ │  Skills  │ │ Plugins │ │ Nexus.Defaults │  │
│  │ Framework│ │  System  │ │         │ │ (batteries inc)│  │
│  └──────────┘ └──────────┘ └─────────┘ └────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Layer 4: Entry Points (Session-Level)                      │
│  ┌──────────────────────┐  ┌─────────────────────────────┐  │
│  │     IAgentLoop       │  │      IOrchestrator          │  │
│  │  + IRoutingStrategy  │  │  Graph/Seq/Parallel/Hier    │  │
│  │  (Single/Workflow/   │  │  (schon vorhanden)          │  │
│  │   Router)            │  │                             │  │
│  └──────────┬───────────┘  └──────────────┬──────────────┘  │
│             └──────────┬──────────────────┘                 │
│                        ▼                                    │
├─────────────────────────────────────────────────────────────┤
│  Layer 3: Agent Execution (Inner Loop)                      │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  ChatAgent (schon vorhanden)                           │ │
│  │  Model → Tool Calls → [Tool Approval?] → Execute → ↺  │ │
│  │  Wrapped in IAgentMiddleware Pipeline                  │ │
│  │  Tool Approval via PermissionToolMiddleware (opt-in)   │ │
│  └────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  Layer 2: Cross-Cutting Middleware & Wrappers               │
│  ┌─────────────┐ ┌────────────┐ ┌────────────────────────┐ │
│  │ Budget      │ │ Compaction │ │  CostTracking          │ │
│  │ Middleware  │ │ Middleware │ │  ChatClient Wrapper     │ │
│  │ (vorhanden)│ │            │ │                        │ │
│  ├─────────────┤ ├────────────┤ ├────────────────────────┤ │
│  │ Permission  │ │ Retry      │ │  Logging/Telemetry     │ │
│  │ ToolMiddlew.│ │ (vorhanden)│ │  (vorhanden)           │ │
│  └─────────────┘ └────────────┘ └────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  Layer 1: Services (standalone, composable)                 │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐  │
│  │ ICost    │ │ IToken   │ │ ICompact.│ │ ISession     │  │
│  │ Tracker  │ │ Counter  │ │ Service  │ │ Store        │  │
│  ├──────────┤ ├──────────┤ ├──────────┤ ├──────────────┤  │
│  │ IModel   │ │ ITool    │ │ ISkill   │ │ IConfig      │  │
│  │ Pricing  │ │ Permiss. │ │ Registry │ │ Provider     │  │
│  └──────────┘ └──────────┘ └──────────┘ └──────────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Layer 0: Primitives (Nexus.Core — schon vorhanden)         │
│  IAgent, ITool, IChatClient, IAgentContext, Events,         │
│  IBudgetTracker, IApprovalGate, IToolRegistry,              │
│  IConversationStore, IWorkingMemory, IMessageBus            │
└─────────────────────────────────────────────────────────────┘
```

---

## Schlüsselfrage: Wo leben Cross-Cutting Concerns?

**Antwort: Als Services (Layer 1) + Middleware (Layer 2). Nie im Loop oder Orchestrator hartcodiert.**

```
ICostTracker (Service)
    ↓ wird genutzt von
CostTrackingChatClient (DelegatingChatClient, Layer 2)
    ↓ wrapped automatisch
IChatClient (wird an ChatAgent übergeben)
    ↓ dadurch
ChatAgent tracked Kosten automatisch — egal ob von AgentLoop oder Orchestrator aufgerufen
```

| Concern | Service (Layer 1) | Wiring (Layer 2) | Wirkt auf | Status |
|---------|-------------------|-------------------|-----------|--------|
| **Cost Tracking** | `ICostTracker` | `CostTrackingChatClient` wraps `IChatClient` | Jeder LLM-Call, egal wo | ✅ |
| **Budget Enforcement** | `IBudgetTracker` (vorhanden) | `BudgetGuardMiddleware` (vorhanden) | Jeder Agent-Turn | ✅ |
| **Compaction** | `ICompactionService`, `ICompactionRecallService` | AgentLoop-Integration vor dem Turn, optionales Recall danach | Session-/Turn-Level, prüft Context | Teilweise |
| **Permissions** | `IToolPermissionHandler` | `PermissionToolMiddleware` (IToolMiddleware) | Pro Tool-Call | ✅ |
| **Session Persistence** | `ISessionStore` | Integration im AgentLoop | Session-Level | ✅ |
| **Step Approval** | `IApprovalGate` (vorhanden) | `WorkflowRoutingStrategy` (ZWISCHEN Steps) | Pro Workflow-Step | Teilweise |
| **Settings Storage** | `INexusSettingsStore` | `FileBasedSettingsStore` (Default) | Laden/Speichern | Offen |

**Das bedeutet: Der Orchestrator BEKOMMT Budget-Checks geschenkt**, weil die Middleware um jeden Agent gewickelt ist, den er aufruft. Er braucht keine eigene Budget-Logik.

```csharp
// Orchestrator ruft Agent auf:
await agent.ExecuteStreamingAsync(task, context, ct);

// Aber die Middleware-Pipeline feuert automatisch:
// 1. BudgetGuardMiddleware → prüft Budget VOR Execution
// 2. CompactionMiddleware → prüft Context Window
// 3. ChatAgent.Execute → inner loop
//    3a. CostTrackingChatClient → tracked jeden LLM-Call
//    3b. PermissionToolMiddleware → prüft jeden Tool-Call (Tool Approval)
// 4. BudgetGuardMiddleware → updated Budget NACH Execution
// 5. WorkflowRoutingStrategy → Step Approval ZWISCHEN Steps (wenn requiresApproval)
```

**Einzige Ausnahme**: Session-Level Concerns (Compaction über Agents HINWEG, globales Budget über alle Agents) — die leben im `IAgentLoop`, weil sie eine Ebene höher aggregieren.

---

## NuGet Package Map

### Bestehende Packages (bleiben)

| Package | Layer | Status |
|---------|-------|--------|
| `Nexus.Core` | 0 | ✅ Fertig — Primitives, Contracts, Pipeline |
| `Nexus.Orchestration` | 3+4 | ✅ Fertig — ChatAgent, Orchestrator, AgentPool |
| `Nexus.Memory` | 1 | ✅ Fertig — ConversationStore, WorkingMemory, LongTermMemory, Recall-Adapter |
| `Nexus.Guardrails` | 2 | ✅ Fertig — Input/Output Validation |
| `Nexus.Messaging` | 1 | ✅ Fertig — Pub/Sub, Point-to-Point |
| `Nexus.Telemetry` | 2 | ✅ Fertig — OTel Traces/Metrics |
| `Nexus.Testing` | — | ✅ Fertig — Mocks, Fakes, Recorder |
| `Nexus.Protocols.Mcp` | 5 | ✅ Fertig — MCP Tool Adapter |
| `Nexus.Protocols.A2A` | 5 | ✅ Fertig — Agent-to-Agent |
| `Nexus.Protocols.AgUi` | 5 | ✅ Fertig — AG-UI SSE Bridge |
| `Nexus.Hosting.AspNetCore` | 5 | ✅ Fertig — Endpoints |
| `Nexus.Workflows.Dsl` | 4 | ✅ Fertig — JSON/YAML Workflows |
| `Nexus.Auth.OAuth2` | 1 | ✅ Fertig — Auth |
| `Nexus.Orchestration.Checkpointing` | 1 | ✅ Fertig — Snapshots |

### Neue Packages

| Package | Layer | Was es ist | Abhängigkeiten | Status |
|---------|-------|-----------|----------------|--------|
| **`Nexus.CostTracking`** | 1+2 | ICostTracker, IModelPricing, CostTrackingChatClient | Core | ✅ |
| **`Nexus.Compaction`** | 1+2 | ICompactionService, Recall-API, ITokenCounter, Strategies, Loop-Integration | Core | Teilweise |
| **`Nexus.Permissions`** | 1+2 | IToolPermissionHandler, Rules, PermissionToolMiddleware | Core | ✅ |
| **`Nexus.Sessions`** | 1 | ISessionStore, ISessionTranscript, FileSessionStore | Core | ✅ |
| **`Nexus.Tools.Standard`** | 1 | FileRead/Write/Edit, Shell, Glob, Grep, WebFetch, AskUser, AgentTool, Registry-Discovery | Core, Orchestration | Teilweise |
| **`Nexus.AgentLoop`** | 4 | IAgentLoop, IRoutingStrategy, Single/Workflow-Strategien | Core, Orchestration | Teilweise |
| **`Nexus.Commands`** | 5 | ICommand, CommandRegistry, SlashCommandDispatcher, Builder-Wiring | Core | Erledigt |
| **`Nexus.Skills`** | 5 | SkillDefinition, ISkillCatalog, SkillCatalog, Builder-Wiring | Core | Teilweise |
| **`Nexus.Configuration`** | 1 | INexusConfigurationProvider, INexusSettingsStore, FileBasedSettingsStore, Settings Merge | Core | ✅ |
| **`Nexus.Defaults`** | 5 | `AddDefaults(...)`, `Nexus.CreateDefault()`, all-in-one Setup fuer vorhandene Kernpakete | Alles optional | Teilweise |

---

## IAgentLoop vs IOrchestrator: Wann was?

Beides sind Einstiegspunkte, aber für verschiedene Szenarien:

```
"Ich habe einen Task und will dass ein oder mehrere Agents ihn lösen"
    → IAgentLoop  (Session-Konzept, interaktiv, fortlaufend)

"Ich habe einen bekannten DAG von Tasks die parallel/sequentiell laufen müssen"
    → IOrchestrator  (Batch-Konzept, fire-and-forget)
```

| Aspekt | IAgentLoop | IOrchestrator |
|--------|-----------|---------------|
| Mental Model | Session / Conversation | Job / Pipeline |
| Routing | Dynamisch (Router) oder Statisch (Workflow) | Statisch (Graph) |
| Interaktiv | Ja — Step-Level HITL via IApprovalGate | Nein (läuft durch) |
| Step-Approval | Ja — User kann nach jedem Step approven/eingreifen/modifizieren | Nein |
| Compaction | Session-Level (über alle Turns) | Nicht nötig (kurze Tasks) |
| Resume | Ja (Session Resume) | Ja (Checkpoint Resume) |
| Typischer Consumer | CLI, Chatbot, IDE Extension | Backend-Pipeline, Batch-Job |

**Beide profitieren von den gleichen Middleware/Services.** Sie sind nicht konkurrierend sondern komplementär.

```csharp
// Der AgentLoop kann intern den Orchestrator nutzen:
public class WorkflowRoutingStrategy : IRoutingStrategy
{
    private readonly IOrchestrator _orchestrator;

    // Nutzt den Orchestrator für den statischen Teil,
    // aber der Loop steuert Session-Concerns drumherum
}
```

---

## Implementierungsreihenfolge

### Phase 1: Foundation Services (2 Wochen)

**Ziel**: Die fehlenden Services als standalone Bausteine. Kein neuer Entry Point nötig — bestehende Architektur profitiert sofort via Middleware.

```
Woche 1:
├── 1a. Nexus.CostTracking
│   ├── ICostTracker + DefaultCostTracker
│   ├── IModelPricingProvider + DefaultModelPricing
│   ├── CostTrackingChatClient (DelegatingChatClient)
│   └── Tests  ✅
│
├── 1b. Nexus.Permissions
│   ├── IToolPermissionHandler + RuleBasedHandler
│   ├── ToolPermissionRule + Presets (ReadOnly, Interactive, AllowAll)
│   ├── PermissionToolMiddleware (IToolMiddleware)
│   └── Tests  ✅

Woche 2:
├── 1c. Nexus.Compaction
│   ├── ITokenCounter + HeuristicTokenCounter
│   ├── IContextWindowMonitor + DefaultMonitor
│   ├── ICompactionStrategy + MicroCompaction + SummaryCompaction
│   ├── ICompactionService + DefaultCompactionService
│   ├── CompactionMiddleware (IAgentMiddleware)
│   └── Tests
│
├── 1d. Nexus.Sessions
│   ├── ISessionStore + ISessionTranscript
│   ├── FileSessionStore (JSONL)
│   └── Tests
```

**Was der User danach tun kann:**
```csharp
// Bestehender Code funktioniert wie bisher. Neue Features = opt-in:
services.AddNexus(b =>
{
    b.UseChatClient(sp => new OpenAIClient(...))
     .AddCostTracking()               // ← NEU: jeder LLM-Call wird getrackt
     .AddPermissions(p => p.UsePreset(PermissionPresets.Interactive))  // ← NEU
     .AddCompaction()                  // ← NEU: Auto-Compaction
     .AddOrchestration();              // bestehend
});
```

### Phase 2: Standard Tools & AgentLoop (2 Wochen)

**Ziel**: Tools die Agents nützlich machen + der Session-Level Entry Point.

```
Woche 3:
├── 2a. Nexus.Tools.Standard
│   ├── FileReadTool, FileWriteTool, FileEditTool  ✅
│   ├── ShellTool (Process-based, cross-platform)  ✅
│   ├── GlobTool, GrepTool  ✅
│   ├── WebFetchTool  ✅
│   ├── AskUserTool (5 Fragetypen via IUserInteraction)  ✅
│   ├── AgentTool (Sub-Agent spawnen via IAgentPool — kritisch für Delegation)  ✅
│   ├── Registry-Discovery fuer per DI registrierte Tools  ✅
│   ├── Tests (mit Filesystem-Sandboxing)  ✅
│   └── Offen: Suspend/Resume-Bridge fuer `Deferred` und haertere Policy-Presets
│
├── 2b. ChatAgent Verbesserungen (in Nexus.Orchestration)
│   ├── Tool Concurrency (read-only parallel, write serial)  ✅
│   ├── Max-Output-Token Recovery (retry bei abgeschnittener Response)
│   ├── Streaming Tool Execution (nicht erst auf volle Response warten)  ✅
│   └── Tests  ✅

Woche 4:
├── 2c. Nexus.AgentLoop
│   ├── IAgentLoop + DefaultAgentLoop
│   ├── IRoutingStrategy Interface
│   ├── SingleAgentStrategy (1 Agent, wie heute aber mit Session-Level Services)
│   ├── WorkflowStrategy (statische Schrittfolge, nutzt vorhandenen Orchestrator)
│   ├── AgentRouterStrategy (LLM-basiertes Routing)
│   ├── AgentLoopEvent Hierarchy
│   ├── Integration: Session, Cost, Compaction aggregiert über Steps
│   └── Tests
```

**Was der User danach tun kann:**
```csharp
// Einfachster Weg: Single Agent mit Tools
var loop = nexus.GetRequiredService<IAgentLoop>();
await foreach (var evt in loop.RunAsync("Fix the bug in auth.cs"))
    Console.Write(evt);

// Multi-Agent mit dynamischem Routing
var loop = nexus.CreateLoop(new AgentRouterStrategy(router, ["coder", "reviewer"]));
await foreach (var evt in loop.RunAsync("Refactor the auth module and add tests"))
    Console.Write(evt);

// Statischer Workflow
var loop = nexus.CreateLoop(new WorkflowStrategy([
    ("researcher", "Research: {task}"),
    ("writer",     "Write based on: {previous}"),
    ("reviewer",   "Review: {previous}"),
]));
```

### Phase 3: DX & Convenience (2 Wochen)

**Ziel**: Developer Experience Features + der "batteries included" Pfad.

```
Woche 5:
├── 3a. Nexus.Commands
│   ├── ICommand + ICommandRegistry + ICommandDispatcher
│   ├── FileCommandLoader (Markdown + Frontmatter)
│   ├── Builtin: /help, /status, /cost, /compact, /resume, /model, /clear
│   └── Tests
│
├── 3b. Nexus.Skills
│   ├── Skill Record + ISkillRegistry + ISkillLoader
│   ├── SkillInjectionMiddleware (IAgentMiddleware)
│   ├── Discovery: .nexus/skills/, ~/.nexus/skills/
│   └── Tests

Woche 6:
├── 3c. Nexus.Configuration
│   ├── INexusConfigProvider + FileBasedConfig
│   ├── 4-Level Merge: Default → User → Project → Managed
│   ├── .nexus/settings.json Format
│   └── Tests
│
├── 3d. Nexus.Defaults (Meta-Package)
│   ├── Nexus.CreateDefault(chatClient) — alles in einem Aufruf
│   ├── Referenziert: Core, CostTracking, Compaction, Permissions,
│   │   Sessions, Tools.Standard, AgentLoop, Commands, Skills, Configuration
│   ├── Sensible Defaults für alles
│   └── Tests
```

**Was der User danach tun kann:**
```csharp
// EINFACHSTER WEG: 3 Zeilen bis zum laufenden Agent
var nexus = Nexus.CreateDefault(new OpenAIClient("gpt-4o"));

await foreach (var evt in nexus.RunAsync("Fix the bug in auth.cs"))
    Console.Write(evt);

// Enthält automatisch: Standard-Tools, Cost Tracking, Compaction,
// Interactive Permissions (Console), Session Persistence, Skills Loading
```

### Phase 4: Full CLI & Docs (2 Wochen)

**Ziel**: Die Nexus CLI wird ein richtiger Coding Agent + umfassende Dokumentation.

```
Woche 7:
├── 4a. Nexus.Cli Rebuild
│   ├── Nutzt Nexus.Defaults als Basis
│   ├── Agent Loop mit Standard-Tools (File, Shell, Glob, Grep)
│   ├── Interactive Permissions (Spectre.Console Prompts)
│   ├── Cost Display (laufend in Status Bar)
│   ├── Session Persistence & /resume
│   ├── Commands Framework (/help, /cost, /compact, /model, /status)
│   ├── Skills Loading aus .nexus/skills/
│   ├── Git-Kontext im System Prompt
│   ├── MCP Server Support aus `.nexus/mcp.json`
│   └── Sub-Agent Tool (spawn Researcher/Coder/Reviewer)
│
├── 4b. Nexus.Cli Features (Advanced)
│   ├── Router-Mode: Agent entscheidet welcher Sub-Agent dran ist
│   ├── Workflow-Mode: Statische Pipeline via .nexus/workflow.json
│   ├── Background Tasks (Shell Commands parallel)
│   └── Project Configuration (.nexus/settings.json)

Woche 8:
├── 4c. Dokumentation
│   ├── Getting Started: "3 Lines to Agent" Tutorial
│   ├── Guide: Building Blocks Übersicht (Welches Package wofür)
│   ├── Guide: Agent Loop vs Orchestrator (Wann was)
│   ├── Guide: Middleware & Composition (Wie Concerns zusammenspielen)
│   ├── Guide: Standard Tools (Referenz + Custom Tools schreiben)
│   ├── Guide: Permissions (Presets, Custom Rules, Enterprise)
│   ├── Guide: Cost Tracking & Budget
│   ├── Guide: Compaction (Strategien, Konfiguration)
│   ├── Guide: Sessions (Persistence, Resume)
│   ├── Guide: Commands & Skills (Erstellen, Laden, Frontmatter)
│   ├── Guide: Configuration (.nexus/ Projektstruktur)
│   ├── Cookbook: "Build a CLI like Claude Code"
│   ├── Cookbook: "Multi-Agent Research Pipeline"
│   ├── Cookbook: "Enterprise Deployment with Managed Settings"
│   └── API Reference Updates für alle neuen Packages
│
├── 4d. Examples Update
│   ├── Nexus.Examples.Minimal → nutzt CreateDefault()
│   ├── Nexus.Examples.MultiAgent → nutzt AgentLoop + WorkflowStrategy
│   ├── Nexus.Examples.Router → AgentLoop + AgentRouterStrategy
│   └── Nexus.Examples.CustomTools → Eigene Tools schreiben
```

---

## Entry Points: Die 3 Wege

### Weg 1: Direkt (Power User — volle Kontrolle)

```csharp
// Der User baut alles selbst zusammen
var agent = new ChatAgent("coder", chatClient, new ChatAgentOptions
{
    SystemPrompt = "You are a coder.",
    MaxIterations = 20,
});

var context = new ManualAgentContext(toolRegistry, workingMemory, ...);
await foreach (var evt in agent.ExecuteStreamingAsync(task, context, ct))
    HandleEvent(evt);
```

**Nutzt**: Nur `Nexus.Core` + `Nexus.Orchestration`
**Middleware**: Nur was der User explizit registriert
**Für**: Library-Autoren die eigene Abstraktionen bauen

### Weg 2: AgentLoop (Recommended — pluggbare Session)

```csharp
services.AddNexus(b =>
{
    b.UseChatClient(sp => new OpenAIClient("gpt-4o"))
     .AddStandardTools()
     .AddCostTracking()
     .AddCompaction()
     .AddPermissions(p => p.UsePreset(PermissionPresets.Interactive))
     .AddAgentLoop(loop =>
     {
         loop.AddAgent("coder", a => a.WithSystemPrompt("...").WithTools("file_read", "bash"));
         loop.UseRouting<SingleAgentStrategy>();
         // ODER: loop.UseRouting(new WorkflowStrategy([...]));
         // ODER: loop.UseRouting(new AgentRouterStrategy(router, [...]));
     });
});

var loop = sp.GetRequiredService<IAgentLoop>();
await foreach (var evt in loop.RunAsync("Fix the bug")) { ... }
```

**Nutzt**: Core + Orchestration + gewünschte Services
**Middleware**: Automatisch per DI (alle registrierten Middlewares greifen)
**Für**: 90% der Anwendungsfälle

### Weg 3: Nexus.Defaults (Quickstart — zero config)

```csharp
var nexus = Nexus.CreateDefault(new OpenAIClient("gpt-4o"));
await foreach (var evt in nexus.RunAsync("Fix the bug in auth.cs"))
    Console.Write(evt);
```

**Nutzt**: Alles (1 Meta-Package)
**Middleware**: Alles mit sensiblen Defaults
**Für**: Prototyping, Tutorials, "Ich will einfach loslegen"

---

## Opt-Out Beispiele

Der User kann immer Teile weglassen:

```csharp
// Ohne Cost Tracking:
services.AddNexus(b =>
{
    b.UseChatClient(...)
     .AddStandardTools()
     // .AddCostTracking()  ← einfach weglassen
     .AddAgentLoop(...);
});

// Ohne Compaction (kurze Sessions):
services.AddNexus(b =>
{
    b.UseChatClient(...)
     .AddStandardTools()
     .AddCostTracking()
     // .AddCompaction()  ← weglassen, Context wird nie komprimiert
     .AddAgentLoop(...);
});

// Ohne Standard-Tools (eigene Tools):
services.AddNexus(b =>
{
    b.UseChatClient(...)
     .AddTools(t =>
     {
         t.Register(new MyCustomTool());
         t.Register(new MyOtherTool());
     })
     .AddAgentLoop(...);
});

// Nur Orchestrator, kein AgentLoop:
services.AddNexus(b =>
{
    b.UseChatClient(...)
     .AddOrchestration()
     .AddCostTracking();  // Middleware greift trotzdem
});
```

---

## Wie alles zusammen in der CLI aussieht

```
$ nexus-cli
╔═╗╔═╗═╗ ╔╔  ╔╗╔═╗
║║║║╣  ╠╣║║  ║╠╬╗║
╝╚╝╚═╝╩ ╩╚═╝╚═╝╩

Nexus CLI v1.0 — Powered by Nexus Framework
Loaded 12 tools | 3 skills | 5 commands
Model: gpt-4o | Budget: $5.00

> Fix the null reference in AuthService.cs

⚡ file_read AuthService.cs                          ✓ 234 lines
⚡ grep_search "null" src/AuthService.cs              ✓ 3 matches
🤔 Found null reference on line 42: user.Email without null check

Allow file_edit on src/AuthService.cs? [Y/n/always] y

⚡ file_edit src/AuthService.cs                       ✓ applied
⚡ bash "dotnet test"                                 ✓ 24 passed

Fixed: Added null check for user.Email on line 42. All tests pass.

─── completed | 6 turns | $0.0234 | 3,421 tokens ───

> /cost
Model      Calls  Tokens   Cost
gpt-4o     6      3,421    $0.0234

> /resume
Resumed session "fix-auth" (6 messages, $0.0234)
```

---

## Testing-Strategie

Jedes neue Package bekommt:

| Test-Typ | Was | Wo |
|----------|-----|-----|
| Unit Tests | Service-Logik isoliert | `tests/Nexus.{Package}.Tests/` |
| Integration Tests | Middleware-Composition | `tests/Nexus.Integration.Tests/` |
| DX Tests | "3 Lines to Agent" compiliert und läuft | `tests/Nexus.Defaults.Tests/` |

Bestehende `Nexus.Testing` Utilities (FakeChatClient, MockAgent, EventRecorder) werden für alle neuen Packages genutzt:

```csharp
// Test: Cost Tracking
var fake = new FakeChatClient()
    .WithResponse("Hello!")
    .WithUsage(inputTokens: 100, outputTokens: 50);

var tracker = new DefaultCostTracker(pricing);
var wrapped = new CostTrackingChatClient(fake, tracker);

await wrapped.GetResponseAsync([new("user", "Hi")]);

Assert.Equal(100, tracker.TotalInputTokens);
Assert.True(tracker.TotalCostUsd > 0);
```

---

## Bewusste Entscheidung: Eigenes Format, keine Kompatibilität

Nexus nutzt ausschließlich das eigene `.nexus/` Konfigurationsformat:

```
.nexus/
  settings.json          ← Projekt-Konfiguration
  skills/                ← Skill-Dateien (Markdown + Frontmatter)
  commands/              ← Custom Commands
  memory/                ← Agent Memory
  workflow.yaml          ← Workflow-Definitionen
```

**Wir implementieren KEINE Kompatibilität mit:**
- `.claude/` (Claude Code Configs, CLAUDE.md, Skills, Settings)
- `.github/copilot-instructions.md` (GitHub Copilot)
- OpenCode Konfiguration

**Warum:**
- Nexus bietet **mehr Flexibilität** als jedes dieser Tools (Workflows, Multi-Agent, Step-Level HITL, Budget, Compaction)
- Kompatibilitäts-Layer wären eine Vereinfachung/Reduktion unserer Fähigkeiten auf den kleinsten gemeinsamen Nenner
- Die Formate der anderen Tools sind auf Single-Agent-Sessions zugeschnitten — Nexus kann Multi-Agent-Workflows mit Step-Level Approvals
- Wartungsaufwand für Compat-Layer bei jedem Update der Drittanbieter-Formate ist unverhältnismäßig
- Wer von Claude Code/Copilot migriert, migriert bewusst auf ein mächtigeres System

---

## Zusammenfassung

| Frage | Antwort |
|-------|---------|
| Verliert der Consumer Flexibilität? | Nein — alles ist opt-in via DI |
| Wo leben Cross-Cutting Concerns? | Services (Layer 1) + Middleware (Layer 2) → wirken auf alles |
| Budget im Orchestrator? | Ja — via BudgetGuardMiddleware die jeden Agent wraps |
| IAgentLoop vs IOrchestrator? | Komplementär: Session vs Batch. AgentLoop kann Orchestrator intern nutzen |
| Standard-Tools? | Neues Package, opt-in via `.AddStandardTools()` |
| Batteries Included? | `Nexus.Defaults` Meta-Package mit `Nexus.CreateDefault()` |
| Step-Level HITL? | Ja — IApprovalGate zwischen Workflow-Steps, User kann approven/eingreifen/modifizieren |
| Kompatibilität mit Claude Code/Copilot? | Nein — bewusste Entscheidung: `.nexus/` eigenes Format, mehr Flexibilität |
| CLI Upgrade? | Phase 4 — bisher noch offen |
| Gesamtaufwand | ~8 Wochen in 4 Phasen |
