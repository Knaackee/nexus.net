# Tasks to Perfection

Vollständige Auflistung aller Aufgaben um die ideas-Dokumente konsistent zu machen,
den bestehenden Code zu fixen, und die geplanten Packages zu implementieren.

Basiert auf: Chat-Verlauf, ideas/*.md, Codebase-Analyse, Claude Code Source.

Status-Stand: 2026-04-01

- `[Erledigt]` = im Repo umgesetzt
- `[Teilweise]` = teilweise umgesetzt, Rest offen
- `[Offen]` = noch nicht umgesetzt

---

## Teil 1: Inkonsistenzen in den Ideas-Dokumenten fixen

### T1.1 — all-together-plan.md: Layer 3 Text falsch [Erledigt]

**Datei:** `ideas/all-together-plan.md`, Layer-Diagramm Zeile ~49-51

**Problem:** Layer 3 (Inner Loop) sagt noch:
```
│  │  Model → Tool Calls → Execute → [Approval?] → Repeat  │ │
│  │  Step-Level HITL via IApprovalGate (opt-in)            │ │
```

**Sollte sein:**
```
│  │  Model → Tool Calls → [Tool Approval?] → Execute → ↺  │ │
│  │  Tool Approval via PermissionToolMiddleware (opt-in)   │ │
```

Step-Level HITL gehört in Layer 4 (steht dort schon korrekt bei RoutingStrategy).

---

### T1.2 — 001-agent-execution-loop.md: Step-Events noch in AgentLoopEvent Hierarchy [Erledigt]

**Datei:** `ideas/001-agent-execution-loop.md`

**Problem:** `StepApprovalRequestedEvent` und `StepApprovalResponseEvent` sind noch in der
`AgentLoopEvent`-Hierarchy definiert. Step Approval lebt in der `WorkflowRoutingStrategy`
(Outer Loop), nicht im AgentLoop (Inner Loop).

**Fix:** Erledigt. Die Step-Approval-Events wurden aus der `AgentLoopEvent`-Hierarchy
entfernt; das Dokument beschreibt jetzt konsistent, dass Step Approval auf Outer-Loop-
Ebene als Routing-Entscheidung bzw. Approval-Wartezustand lebt.

`LoopStopReason.StepRejected` kann bleiben — der AgentLoop empfängt die Stop-Entscheidung
von der RoutingStrategy und kann diesen Grund reporten.

---

### T1.3 — 011-simplification.md: Workflows DSL Einschätzung veraltet [Erledigt]

**Datei:** `ideas/011-simplification.md`

**Problem:** Sagt "Workflows DSL ist premature". Das Package existiert aber vollständig
mit WorkflowDefinition, NodeDefinition, EdgeDefinition, Validation, JSON/YAML Loading,
Builder Pattern. Es ist fertig und bleibt.

**Fix:** Text ändern zu: "Workflows DSL ist implementiert. Braucht Erweiterung um
`RequiresApproval` auf NodeDefinition und Integration mit WorkflowRoutingStrategy."

---

## Teil 2: Bestehenden Code fixen / erweitern

### T2.1 — ChatAgent: Tool Approval einbauen (KRITISCH) [Erledigt]

**Datei:** `src/Nexus.Orchestration/ChatAgent.cs`, Zeilen 105-135

**Ist-Zustand:** Tools werden direkt ausgeführt ohne jeglichen Check:
```csharp
var tool = context.Tools.Resolve(fc.Name);
var toolResult = await tool.ExecuteAsync(inputJson, toolContext, ct);
```

**Soll-Zustand:** Vor Tool-Execution prüfen ob das Tool Approval braucht:
```csharp
var tool = context.Tools.Resolve(fc.Name);

// Tool Approval Check (wenn IApprovalGate vorhanden + Tool RequiresApproval)
if (context.ApprovalGate is not null && tool.Annotations?.RequiresApproval == true)
{
    var approval = await context.ApprovalGate.RequestApprovalAsync(
        new ApprovalRequest($"Tool '{fc.Name}' ausführen?", Id, fc.Name, inputJson));
    
    if (!approval.IsApproved)
    {
        messages.Add(new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(fc.CallId, "Tool execution denied by user")]));
        yield return new ToolCallCompletedEvent(Id, fc.CallId, ToolResult.Failure("Denied"));
        continue;
    }
}

var toolResult = await tool.ExecuteAsync(inputJson, toolContext, ct);
```

**Abhängigkeit:** `IAgentContext.ApprovalGate` muss zugänglich sein (Interface prüfen).

**Tests:**
- Tool mit `RequiresApproval=true` + ApprovalGate→Deny → Tool wird nicht ausgeführt
- Tool mit `RequiresApproval=true` + ApprovalGate→Approve → Tool wird ausgeführt
- Tool mit `RequiresApproval=false` → Tool wird immer ausgeführt (kein Gate-Check)
- Kein ApprovalGate registriert → alle Tools laufen durch

---

### T2.2 — ChatAgent: Doppelter LLM-Call fixen [Erledigt]

**Datei:** `src/Nexus.Orchestration/ChatAgent.cs`, Zeilen 74-91

**Problem:** Der Agent macht ZWEI LLM-Calls pro Iteration:
1. `GetStreamingResponseAsync()` — für Streaming-Output
2. `GetResponseAsync()` — nochmal komplett, um Tool-Calls zu detecten

Das ist ein Bug/Workaround. Kostet doppelt Tokens und doppelt Latenz.

**Fix:** Nur `GetStreamingResponseAsync()` nutzen und Tool-Calls aus dem
gestreamten Response extrahieren. `ChatResponseUpdate` enthält auch
`FunctionCallContent` — die müssen während des Streamings gesammelt werden.

---

### T2.3 — NodeDefinition: RequiresApproval Property hinzufügen [Erledigt]

**Datei:** `src/Nexus.Workflows.Dsl/WorkflowDefinition.cs`

**Ist-Zustand:** `NodeDefinition` hat kein `RequiresApproval`.

**Fix:**
```csharp
public record NodeDefinition
{
    // ... bestehende Properties ...
    public bool RequiresApproval { get; init; }  // ← NEU
}
```

**Tests:**
- JSON/YAML Serialisierung mit `requiresApproval: true`
- Workflow Validation akzeptiert das Feld
- Default ist `false`

---

### T2.4 — Fehlende Tests für bestehende Features [Erledigt]

**Problem:** Bestehende Tests prüfen nur Typen/Records/IDs, nicht Verhalten.

**Fehlende Tests:**
- ChatAgent: Tool-Loop Verhalten (Tool wird aufgerufen, Ergebnis geht an LLM)
- ChatAgent: MaxIterations wird eingehalten
- ChatAgent: Tool nicht gefunden → Error-Message an LLM
- DefaultOrchestrator: Graph-Execution mit echten (Fake) Agents
- BudgetGuardMiddleware: Budget überschritten → Agent stoppt
- Middleware-Pipeline: Streaming-Variante

---

## Teil 3: Neue Packages implementieren

### Phase 1: Foundation Services

#### T3.1 — Nexus.CostTracking [Erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Interfaces:**
- `ICostTracker` — aggregiert Token-Verbrauch und USD-Kosten pro Session
- `IModelPricingProvider` — Preistabelle pro Model (Input/Output/Cache per MTok)
- `CostTrackingChatClient : DelegatingChatClient` — wraps IChatClient, extrahiert Usage automatisch

**Implementierungen:**
- `DefaultCostTracker` — thread-safe, per-model Breakdown, Snapshot für Persistenz
- `DefaultModelPricingProvider` — hardcoded Preise (erweiterbar via DI)
- `CostTrackingChatClient` — liest `response.Usage` aus, ruft `ICostTracker.RecordUsage()`

**DI:**
```csharp
builder.AddCostTracking(pricing => pricing.AddModel("custom", input: 1.0m, output: 5.0m));
```

**Tests:**
- CostTracker aggregiert korrekt über mehrere Calls
- CostTrackingChatClient extrahiert Usage aus Response und Streaming Response
- Unbekanntes Model → kein Crash, Cost = 0

---

#### T3.2 — Nexus.Permissions [Erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Interfaces:**
- `IToolPermissionHandler` — prüft vor Tool-Execution ob erlaubt
- `PermissionDecision` Hierarchy — `Granted`, `Denied`, `Ask`
- `ToolPermissionRule` — Pattern + Action + Condition + Source
- `PermissionToolMiddleware : IToolMiddleware` — wraps Tool-Execution

**Implementierungen:**
- `RuleBasedPermissionHandler` — Rules nach Priorität, Pattern-Matching
- `PermissionPresets` — `ReadOnly`, `Interactive`, `AllowAll`

**DI:**
```csharp
builder.AddPermissions(p => p.UsePreset(PermissionPresets.Interactive));
```

**Tests:**
- Rule-Matching: `file_*` matcht `file_read` und `file_write`
- Priorität: Managed > User > Project > Default
- Bypass-Mode: alles erlaubt
- Ask-Flow: Callback wird aufgerufen

---

#### T3.3 — Nexus.Compaction [Teilweise erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Status:**
- Erledigt: `Nexus.Compaction` mit `ITokenCounter`, `IContextWindowMonitor`, `ICompactionService`, `MicroCompactionStrategy` und `SummaryCompactionStrategy`
- Erledigt: `builder.AddCompaction(...)` sowie Integration in `DefaultAgentLoop`
- Erledigt: Post-Compaction-Recall als getrennte API (`ICompactionRecallService`, `ICompactionRecallProvider`) und Loop-Integration inklusive Guard gegen uebergrossen Recall-Kontext
- Erledigt: `Nexus.Memory.LongTermMemoryRecallProvider` als konkreter Recall-Adapter fuer `ILongTermMemory`
- Erledigt: Tests fuer Micro-Compaction, Summary-Compaction, Auto-Compact-Trigger und Strategy-Reihenfolge
- Offen: `MemoryExtractionStrategy`, Threshold-Events und eine dedizierte Middleware-Integration

**Interfaces:**
- `ITokenCounter` — zählt Tokens (heuristisch oder model-spezifisch)
- `IContextWindowMonitor` — FillRatio, AvailableTokens, Threshold-Events
- `ICompactionStrategy` — `ShouldCompact()` + `CompactAsync()`
- `ICompactionService` — orchestriert Strategies nach Priorität
- `ICompactionRecallProvider` / `ICompactionRecallService` — rehydrieren den aktiven Verlauf nach erfolgreicher Verdichtung

**Strategien:**
- `MicroCompactionStrategy` (Priority 10) — alte Tool-Results durch Platzhalter ersetzen
- `SummaryCompactionStrategy` (Priority 50) — LLM-Summary der Konversation
- `MemoryExtractionStrategy` (Priority 100) — Key Facts in Memory extrahieren

**Konfiguration:**
```csharp
builder.AddCompaction(o => {
    o.AutoCompactThreshold = 0.80;
    o.OutputTokenReserve = 8192;
});
```

**Tests:**
- MicroCompaction: Alte Tool-Results werden ersetzt
- SummaryCompaction: Messages werden durch Summary ersetzt
- Monitor: Threshold-Events feuern bei Überschreitung
- CompactionService: Probiert Strategies nach Priorität

---

#### T3.4 — Nexus.Sessions [Weitgehend erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Interfaces:**
- `ISessionStore` — CRUD für Sessions
- `ISessionTranscript` — Append-only Message-Log
- `SessionInfo`, `SessionId`

**Implementierungen:**
- `FileSessionStore` — JSONL auf Disk, Append-only Transcripts
- Session-Index als `sessions.json`

**Tests:**
- Create/List/Get/Delete Session
- Append Messages zu Transcript
- ReadAsync streamt Messages als IAsyncEnumerable
- Resume: Letzte Session laden und weitermachen

---

### Phase 2: Execution Layer

#### T3.5 — Tool Concurrency in ChatAgent oder IToolExecutor [Erledigt]

**Erweiterung** von Nexus.Core oder Nexus.Orchestration.

**Problem:** ChatAgent führt Tools sequentiell aus (`foreach`).

**Interface:**
- `IToolExecutor` — führt Tool-Batches aus mit intelligenter Parallelisierung
- `ITool.IsConcurrencySafe` Property (oder über `ToolAnnotations.IsReadOnly` ableiten)

**Implementierung:**
- `PartitionedToolExecutor` — ReadOnly parallel (max N), Write seriell
- Nutzt `ToolAnnotations.IsReadOnly` um zu partitionieren (existiert bereits!)

**Integration:**
- ChatAgent nutzt `IToolExecutor` statt eigener `foreach`-Schleife
- Oder: `IToolExecutor` als optionaler Service via DI

**Tests:**
- 5 ReadOnly-Tools → laufen parallel (Zeitmessung)
- Mix aus ReadOnly + Write → ReadOnly parallel, dann Write seriell
- Kein IToolExecutor registriert → Fallback auf sequentiell

---

#### T3.6 — Nexus.AgentLoop [Teilweise erledigt]

**Neues Package.** Hängt ab von: Nexus.Core, Nexus.Orchestration

**Interfaces:**
- `IAgentLoop` — Session-level Loop mit Event-Streaming
- `IRoutingStrategy` — entscheidet welcher Agent als nächstes dran ist
- `AgentLoopEvent` Hierarchy — TextChunk, ToolStarted, ToolCompleted, Compaction, etc.
- `AgentLoopOptions` — Agent, Messages, Budget, MaxTurns, StopCondition

**Status:**
- Erledigt: `DefaultAgentLoop` mit Session-Persistierung, `ResumeLastSession`, `MaxTurns`, `StopWhen` und Compaction-Trigger vor dem Turn
- Erledigt: `IRoutingStrategy` und `WorkflowRoutingStrategy` fuer sequentielle Workflow-DSL-Ausfuehrung inkl. Step-Approval zwischen Nodes
- Erledigt: Tests fuer Streaming, Resume, MaxTurns, Compaction-Event und Workflow-Routing
- Offen: `AgentRouterStrategy`, parallele/verzweigte Routing-Pfade und feinere Outer-Loop-Routing-Entscheidungen

**Strategien:**
- `SingleAgentStrategy` — ein Agent, wie ChatAgent aber mit Session-Services
- `WorkflowRoutingStrategy` — statische Steps aus WorkflowDefinition, Step Approval
- `AgentRouterStrategy` — LLM entscheidet welcher Agent dran ist

**Step Approval (in WorkflowRoutingStrategy):**
- Prüft `NodeDefinition.RequiresApproval` nach jedem Step
- Ruft `IApprovalGate.RequestApprovalAsync()` auf
- User kann: Approve, Modify (Output ändern), Reject (Stop)
- `RoutingDecision.WaitForApproval` / `RoutingDecision.RunAgent` / `RoutingDecision.Stop`

**Tests:**
- SingleAgentStrategy: Agent läuft, Events werden gestreamt
- WorkflowRoutingStrategy: Steps laufen in Reihenfolge
- WorkflowRoutingStrategy + RequiresApproval: Loop pausiert, wartet auf Gate
- AgentRouterStrategy: LLM-basierte Routing-Entscheidung
- Budget erschöpft → Loop stoppt
- MaxTurns erreicht → Loop stoppt

---

#### T3.7 — Nexus.Tools.Standard [Teilweise erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Status:**
- Erledigt: Neues Package `Nexus.Tools.Standard` mit `FileReadTool`, `FileWriteTool`, `FileEditTool`, `ShellTool`, `GlobTool`, `GrepTool`, `WebFetchTool`, `AskUserTool` und `AgentTool`
- Erledigt: DI-Wiring via `AddStandardTools(...)` fuer `NexusBuilder` und `ToolBuilder`
- Erledigt: Tool-Registry-Hydration aus per DI registrierten `ITool`-Implementierungen
- Erledigt: Filesystem-Sandboxing, Shell-Working-Directory-Begrenzung und gezielte Tests
- Erledigt: Integration in `Nexus.Defaults` inklusive Console-basierter `IUserInteraction`
- Offen: Checkpoint-/Suspend-Integration fuer `AskUserTool` bei spaeteren Antworten (`Deferred`)
- Offen: Haertere Shell/Web-Policy-Presets und feinere Sicherheitsgrenzen fuer produktive Hosts

**Tools:**
- `FileReadTool` — Datei lesen (Zeilen-Range)
- `FileWriteTool` — Datei erstellen/überschreiben
- `FileEditTool` — Search & Replace in Datei
- `ShellTool` — Shell-Command ausführen (cross-platform)
- `GlobTool` — Files per Pattern finden
- `GrepTool` — Text in Files suchen (Regex)
- `WebFetchTool` — HTTP GET
- `AskUserTool` — User eine Frage stellen (Rückkanal Agent→User)
- `AgentTool` — Sub-Agent spawnen und Ergebnis zurückliefern

**AskUserTool Detail:**
```csharp
// Nutzt IUserInteraction Interface (nicht IApprovalGate — das ist binär ja/nein).
// AskUserTool stellt typisierte Fragen und bekommt strukturierte Antworten.
public interface IUserInteraction
{
    Task<UserResponse> AskAsync(
        UserQuestion question,
        UserInteractionOptions? options = null,
        CancellationToken ct = default);
}

// --- Frage-Typen als Type Hierarchy ---

public abstract record UserQuestion(string Question);

public record FreeTextQuestion(string Question, string? Placeholder = null)
    : UserQuestion(Question);

public record ConfirmQuestion(string Question, bool? DefaultValue = null)
    : UserQuestion(Question);

public record SelectQuestion(string Question, IReadOnlyList<string> Options, int? DefaultIndex = null)
    : UserQuestion(Question);

public record MultiSelectQuestion(string Question, IReadOnlyList<string> Options,
    IReadOnlyList<int>? DefaultSelected = null)
    : UserQuestion(Question);

public record SecretQuestion(string Question)
    : UserQuestion(Question);

// --- Antwort mit Status ---

public record UserResponse(
    string Answer,
    UserResponseStatus Status = UserResponseStatus.Answered);

public enum UserResponseStatus
{
    Answered,       // User hat geantwortet
    Cancelled,      // User hat abgebrochen (Ctrl+C, "skip")
    TimedOut,       // Timeout abgelaufen
    Deferred        // User will später antworten → Agent soll suspendieren
}

// --- Optionen (Timeout, Kontext, Urgency) ---

public record UserInteractionOptions
{
    /// Wie lange maximal auf Antwort warten. null = unbegrenzt.
    public TimeSpan? Timeout { get; init; }

    /// Fallback-Antwort wenn Timeout abläuft. null = Status wird TimedOut.
    public string? DefaultOnTimeout { get; init; }

    /// Darf der Agent weitermachen ohne Antwort? (für low-priority Fragen)
    public bool IsOptional { get; init; }

    /// Kontext für die UI (welcher Agent fragt, warum, wie dringend)
    public InteractionContext? Context { get; init; }
}

public record InteractionContext(
    string AgentId,
    string? Reason = null,
    InteractionUrgency Urgency = InteractionUrgency.Normal);

public enum InteractionUrgency
{
    Low,        // Agent kann auch ohne Antwort weitermachen
    Normal,     // Agent wartet, aber kein Zeitdruck
    High        // Blockiert kritischen Pfad
}
```

**Hinweis zum aktuellen Stand:** `Answered` und `Cancelled` sind direkt nutzbar; `Deferred`
ist als Response-Status modelliert, suspendiert den Agenten aber noch nicht automatisch via
Checkpointing.

**Timeout-Verhalten:**

| Szenario | `Timeout` | `DefaultOnTimeout` | Ergebnis |
|----------|-----------|---------------------|----------|
| Muss warten | `null` | `null` | Blockiert unbegrenzt |
| Zeitlimit mit Fallback | `5min` | `"ja"` | Nach 5min automatisch "ja" |
| Zeitlimit ohne Fallback | `5min` | `null` | `Status=TimedOut` → Agent entscheidet |
| Optional | egal | egal | `IsOptional=true` → Agent kann ohne Antwort weiter |

**Suspend/Resume bei langer Wartezeit:**
- `Status=Deferred` → Agent-Loop suspendiert sich via Checkpointing
- Server-Ressourcen werden freigegeben (kein Memory/Thread belegt)
- User kommt zurück → antwortet → Loop wird resumed
- Verknüpft AskUserTool mit Nexus.Orchestration.Checkpointing

**Bewusst kein FormTool im Standard:**
Multi-Field-Forms mit Validation, Required-Flags, Conditional-Visibility etc. sind
anwendungsspezifisch und kosten ~400-600 Schema-Tokens pro LLM-Call. Wer Forms braucht,
registriert ein eigenes `FormTool`. Für 90% der Fälle reicht AskUser mit 5 Fragetypen.

- `IsReadOnly=true` — verändert nichts am System
- Agent nutzt das Tool wenn er Klarstellung braucht oder dem User eine Auswahl geben will
- In einer CLI: Spectre.Console Prompt. In einem Server: Event an Frontend.

**AgentTool Detail:**
```csharp
// Nutzt IAgentPool um einen Sub-Agent zu spawnen.
// Der Sub-Agent bekommt einen isolierten Kontext (eigene Messages, eigenes Budget).
public class AgentTool : ITool
{
    // Parameters:
    //   agent: string — Name/ID des Agents aus dem Pool
    //   task: string — Aufgabenbeschreibung für den Sub-Agent
    //   model: string? — optionales Model-Override
    
    // Execution:
    //   1. Agent aus IAgentPool holen (oder neu spawnen)
    //   2. Agent.ExecuteAsync(task, isolatedContext, ct)
    //   3. AgentResult.Text als Tool-Result zurückgeben
    //   4. Sub-Agent Events werden als ToolProgress gestreamt
}
```
- `IsReadOnly=false` — Sub-Agent kann Tools mit Seiteneffekten nutzen
- `RequiresApproval=false` — der Sub-Agent hat seine eigenen Permission-Checks
- Sub-Agent erbt Permission-Rules vom Parent, aber hat eigenes Budget
- Kritisch für: Research-Delegation, Code-Review, parallele Aufgaben

**Annotations:**
- FileRead, Glob, Grep, WebFetch, AskUser → `IsReadOnly=true`
- FileWrite, FileEdit → `IsReadOnly=false`, `RequiresApproval=true`
- Shell → `IsReadOnly=false`, `RequiresApproval=true`, `IsDestructive=true`
- AgentTool → `IsReadOnly=false`, `RequiresApproval=false`

**Tests:**
- Jedes Tool: Happy Path
- FileEdit: Search-String nicht gefunden → Fehler
- Shell: Command nicht vorhanden → Fehler
- Sandbox: Tools arbeiten nur in erlaubtem Verzeichnis
- AskUserTool: FreeTextQuestion → Antwort kommt zurück
- AskUserTool: ConfirmQuestion → bool-Antwort korrekt gemappt
- AskUserTool: SelectQuestion → Index/Wert aus Options
- AskUserTool: MultiSelectQuestion → mehrere Werte
- AskUserTool: SecretQuestion → Antwort wird nicht geloggt
- AskUserTool: Timeout abgelaufen → Status=TimedOut, DefaultOnTimeout greift
- AskUserTool: User cancelled → Status=Cancelled
- AskUserTool: Status=Deferred → Agent-Loop wird über Checkpointing suspendiert
- AskUserTool: IsOptional=true + Timeout → Agent macht ohne Antwort weiter
- AskUserTool: Kein IUserInteraction registriert → Fehler mit klarer Message
- AgentTool: Sub-Agent wird gespawnt, läuft, Result wird als Tool-Result zurückgegeben
- AgentTool: Sub-Agent Budget wird separat getrackt
- AgentTool: Sub-Agent Fehler → Tool-Result mit Error

---

### Phase 3: DX & Convenience

#### T3.8 — Nexus.Commands [Erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Status:**
- Erledigt: `Nexus.Commands` mit `ICommand`, `CommandInvocation`, `CommandResult`, `CommandDispatchResult`, `ICommandCatalog`, `CommandRegistry` und `SlashCommandDispatcher`
- Erledigt: Builder-/DI-Wiring ueber `NexusBuilder.AddCommands(...)`
- Erledigt: Markdown-/Frontmatter-Loader fuer Commands aus Projekt-, User- und Zusatzverzeichnissen
- Erledigt: `Nexus.Defaults` verdrahtet Commands im Default-Host automatisch mit
- Erledigt: CLI verwendet das Paket fuer Slash-Commands statt eines hardcodierten `switch`
- Erledigt: Tests fuer Dispatcher- und Registry-Verhalten
- Erledigt: gemeinsamer `DelegateCommand` und paketdefinierte Framework-Builtins fuer `/help`, `/quit`, `/status`, `/resume`, `/cost`, `/model`, `/clear` und `/compact`

**Interfaces:**
- `ICommand` — Name, Description, Type (Action/Prompt), Execute
- `ICommandRegistry` — Register, Get, GetAll
- `ICommandDispatcher` — IsCommand(), DispatchAsync()
- `FileCommandLoader` — Markdown + YAML Frontmatter → ICommand

**Builtins:**
- `/help`, `/status`, `/cost`, `/compact`, `/resume`, `/model`, `/clear`

**Tests:**
- Dispatch: `/help` → HelpCommand.Execute
- Unknown Command → Fehler
- FileCommandLoader: Markdown → PromptCommand
- Registry: Mehrere Sources (Builtin + User + Plugin)

---

#### T3.9 — Nexus.Skills [Erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Status:**
- Erledigt: `Nexus.Skills` mit `SkillDefinition`, `ISkillCatalog`, `SkillCatalog` und Hilfen zur `AgentDefinition`-Komposition
- Erledigt: Builder-/DI-Wiring ueber `NexusBuilder.AddSkills(...)`
- Erledigt: Markdown-/Frontmatter-Loader fuer Skills aus Projekt-, User- und Zusatzverzeichnissen
- Erledigt: `Nexus.Defaults` verdrahtet Skills im Default-Host automatisch mit
- Erledigt: CLI verwendet einen programmatischen Skill-Katalog fuer Chat-/Coding-Profile
- Erledigt: Tests fuer Skill-Komposition und Katalog-Lookups
- Erledigt: automatische Relevanzermittlung ueber `ISkillCatalog.FindRelevant(...)`
- Erledigt: `SkillInjectionMiddleware` fuer automatische Prompt-/Tool-Injection

**Interfaces:**
- `Skill` record — Name, Content, AllowedTools, WhenToUse, Source
- `ISkillRegistry` — Register, FindRelevant, BuildPromptSection
- `ISkillLoader` — LoadFromDirectory (Markdown + Frontmatter)
- `SkillInjectionMiddleware : IAgentMiddleware` — injected relevante Skills in System Prompt

**Discovery:**
```
1. Project: .nexus/skills/
2. User: ~/.nexus/skills/
3. Plugin: via Plugin-System
4. Inline: programmatisch
```

**Tests:**
- Skill laden aus Markdown mit Frontmatter
- FindRelevant: matcht Keywords aus WhenToUse
- SkillInjectionMiddleware: Skills werden in System Prompt injected
- Leeres Skills-Verzeichnis → kein Fehler

---

#### T3.10 — Nexus.Configuration [Erledigt]

**Neues Package.** Hängt ab von: Nexus.Core

**Interfaces:**
- `INexusSettingsStore` — `LoadAsync(source)`, `SaveAsync(settings, source)` (pluggable)
- `INexusConfigurationProvider` — `LoadAsync()` merged alle Stores
- `NexusSettings` — das gemergte Settings-Objekt
- `SettingValue<T>` — Wert + Source-Info

**Implementierungen:**
- `FileBasedSettingsStore` — default, JSON-Dateien
- `DefaultConfigurationProvider` — merged Default → Project → User → Managed → Runtime
- `NexusSettings`, `SettingSource`, `SettingValue<T>`, Builder-Extensions fuer Store-Registrierung und Runtime-/Default-Overrides

**DI:**
```csharp
// Default (Dateien):
builder.AddConfiguration();

// Mit DB:
builder.AddConfiguration(c => c.UseStore<DatabaseSettingsStore>());
```

**Tests:**
- 4-Level Merge: Managed überschreibt User überschreibt Project überschreibt Default
- Pluggable Store: Custom INexusSettingsStore wird aufgerufen
- Fehlende Datei → kein Fehler, Default-Werte

---

#### T3.11 — Nexus.Defaults (Meta-Package) [Teilweise erledigt]

**Neues Package.** Referenziert: alle opt-in Packages.

**Zweck:** `Nexus.CreateDefault(chatClient)` — alles in einem Aufruf.

**Enthält:**
- Erledigt: `AddDefaults(...)` fuer bestehende NexusBuilder-Setups
- Erledigt: `Nexus.CreateDefault(chatClient)` und `NexusDefaultHost` mit `RunAsync(...)`
- Erledigt: Cost Tracking
- Erledigt: Compaction
- Erledigt: Interactive Permissions (Console-basiert)
- Erledigt: In-Memory Session Store
- Erledigt: AgentLoop mit Default-Agent-Definition
- Erledigt: Standard-Tools inkl. Console-basierter `IUserInteraction`
- Erledigt: Commands/Skills koennen auf Builder-Ebene komponiert werden und sind Teil des Default-Auto-Wirings

**Tests:**
- `CreateDefault()` → kompiliert und läuft
- RunAsync → Events werden gestreamt
- Alle Services sind registriert und auflösbar

---

### Phase 4: CLI & Docs

#### T3.12 — Nexus.Cli Rebuild [Weitgehend erledigt]

**Bestehend:** `examples/Nexus.Cli/` — war nur Chat-Client, ist jetzt teilweise zum Agent-CLI umgebaut.

**Status:**
- Erledigt: `ChatSession` nutzt `NexusDefaultHost` + `IAgentLoop`
- Erledigt: Standard-Tools und Skill-/Tool-Profile sind eingebunden
- Erledigt: Slash-Commands laufen ueber `Nexus.Commands`
- Erledigt: persistentes `/resume` ueber Prozessgrenzen via Dateispeicher unter `.nexus/sessions`
- Erledigt: file-basiertes Skills-/Commands-Loading aus `.nexus/` und User-Scope
- Erledigt: `/cost` fuer laufende Sessionkosten und Statusabfrage
- Erledigt: `/model`, `/clear` und `/compact` als Teil der gemeinsamen Command-Surface
- Erledigt: CLI-Dokumentation wurde auf die neue Architektur umgestellt
- Erledigt: MCP-Server koennen aus Projekt-/User-Konfiguration geladen und als Tools registriert werden
- Offen: weiterer Daily-Driver-Feinschliff

**Umbau zu Agent CLI:**
- AgentLoop mit Standard-Tools
- Interactive Permissions (Spectre.Console Prompts)
- Cost Display (laufend in Status Bar)
- Session Persistence & `/resume`
- Commands Framework (alle Builtins)
- Skills Loading aus `.nexus/skills/`
- Git-Kontext im System Prompt
- MCP Server Support ueber `.nexus/mcp.json` und `~/.nexus/mcp.json`

**Phasen des Umbaus:**
1. ChatSession → AgentLoop ersetzen
2. Standard-Tools registrieren
3. Permission-Prompts einbauen
4. Session-Persistierung auf Disk
5. Commands Framework integrieren
6. Skills laden

---

#### T3.13 — Dokumentation [Teilweise]

- Getting Started: "3 Lines to Agent" Tutorial
- Guide: Building Blocks Übersicht
- Guide: Agent Loop vs Orchestrator (wann was)
- Guide: Middleware & Composition
- Guide: Standard Tools + Custom Tools
- Guide: Permissions
- Guide: Cost Tracking & Budget
- Guide: Compaction
- Guide: Sessions
- Guide: Commands & Skills
- Guide: Configuration
- Guide: Workflows + Step-Level HITL
- Cookbook: "Build a CLI like Claude Code"
- Cookbook: "Multi-Agent Research Pipeline"
- API Reference Updates für alle neuen Packages

---

## Teil 4: Zusammenfassung

### Abhängigkeits-Graph

```
T2.1 ChatAgent Fix (Tool Approval)     ← keine Abhängigkeit, sofort machbar
T2.2 ChatAgent Fix (Doppelter Call)     ← keine Abhängigkeit, sofort machbar
T2.3 NodeDefinition.RequiresApproval    ← keine Abhängigkeit, sofort machbar
T2.4 Fehlende Tests                     ← keine Abhängigkeit, sofort machbar

T3.1 CostTracking                       ← Core
T3.2 Permissions                        ← Core
T3.3 Compaction                         ← Core
T3.4 Sessions                           ← Core

T3.5 Tool Concurrency                   ← Core (ToolAnnotations.IsReadOnly)
T3.7 Tools.Standard                     ← Core, Orchestration (für AgentTool → IAgentPool)

T3.6 AgentLoop                          ← Core, Orchestration, T3.1, T3.2, T3.3, T2.3
T3.8 Commands                           ← Core
T3.9 Skills                             ← Core
T3.10 Configuration                     ← Core

T3.11 Defaults                          ← T3.1–T3.10
T3.12 CLI Rebuild                       ← T3.11
T3.13 Dokumentation                     ← T3.12

T1.1–T1.3 Ideas fixen                   ← keine Abhängigkeit, sofort machbar
```

### Prioritätsreihenfolge

| Prio | Task | Typ | Aufwand |
|------|------|-----|---------|
| 🔴 1 | T2.1 ChatAgent Tool Approval Fix | Code Fix | 0.5 Tage | Erledigt |
| 🔴 2 | T2.2 ChatAgent Doppelter LLM-Call | Code Fix | 0.5 Tage | Erledigt |
| 🔴 3 | T2.3 NodeDefinition.RequiresApproval | Code Fix | 0.5 Stunden | Erledigt |
| 🔴 4 | T2.4 Fehlende Tests | Tests | 1-2 Tage | Erledigt |
| 🟡 5 | T1.1-T1.3 Ideas-Docs fixen | Docs | 0.5 Stunden | Erledigt |
| 🟠 6 | T3.1 CostTracking | Neues Package | 1-1.5 Tage | Erledigt |
| 🟠 7 | T3.2 Permissions | Neues Package | 1-1.5 Tage | Erledigt |
| 🟠 8 | T3.3 Compaction | Neues Package | 2 Tage | Teilweise erledigt |
| 🟠 9 | T3.4 Sessions | Neues Package | 1.5 Tage | Weitgehend erledigt |
| 🟠 10 | T3.5 Tool Concurrency | Erweiterung | 1 Tag | Erledigt |
| 🟠 11 | T3.7 Tools.Standard | Neues Package | 2 Tage | Teilweise erledigt |
| 🟠 12 | T3.6 AgentLoop | Neues Package | 3 Tage | Teilweise erledigt |
| 🟡 13 | T3.8 Commands | Neues Package | 1.5 Tage | Erledigt |
| 🟡 14 | T3.9 Skills | Neues Package | 1.5 Tage | Erledigt |
| 🟡 15 | T3.10 Configuration | Neues Package | 1.5 Tage | Erledigt |
| 🟡 16 | T3.11 Defaults | Meta-Package | 1 Tag | Weitgehend erledigt |
| 🟡 17 | T3.12 CLI Rebuild | Umbau | 3-4 Tage | Weitgehend erledigt |
| 🟡 18 | T3.13 Dokumentation | Docs | 3-4 Tage | Teilweise |

### Architektur-Entscheidungen (bestätigt)

- ✅ Step Approval → WorkflowRoutingStrategy (Outer Loop, Layer 4)
- ✅ Tool Approval → PermissionToolMiddleware / ChatAgent (Inner Loop, Layer 3)
- ✅ Keine Kompatibilität mit .claude/, Copilot, OpenCode
- ✅ `.nexus/` als eigenes Konfigurationsformat
- ✅ `INexusSettingsStore` als pluggable Interface (File, DB, Cloud)
- ✅ Cross-Cutting Concerns als Services + Middleware, nie im Loop hartcodiert
- ✅ IAgentLoop und IOrchestrator sind komplementär (Session vs Batch)
