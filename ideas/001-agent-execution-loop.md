# 001 — Agent Execution Loop (Query Engine)

## Priorität: 🔴 Kritisch

## Status

Stand: 2026-04-01

- Erledigt: `Nexus.AgentLoop` Package existiert mit `IAgentLoop`, `AgentLoopOptions` und Event-Typen
- Erledigt: `DefaultAgentLoop` fuehrt einen Agenten session-aware aus und streamt Text-, Tool-, Approval-, Usage- und Completion-Events
- Erledigt: Integration mit `Nexus.Sessions` fuer Session-Erstellung, Persistierung und `ResumeLastSession`
- Erledigt: `MaxTurns`, `StopWhen` und Context-Compaction sind in `AgentLoopOptions`/`DefaultAgentLoop` integriert; neue Unit-Tests decken MaxTurns und Compaction-Trigger ab
- Erledigt: Post-Compaction-Recall ist als eigene Schicht getrennt (`ICompactionRecallService` / `ICompactionRecallProvider`); `DefaultAgentLoop` kann nach dem Verdichten Memory wieder in den aktiven Kontext einfuegen
- Erledigt: Unit-Tests fuer Basis-Streaming, Resume, MaxTurns und Compaction; Live-Integration-Test gegen Ollama vorhanden
- Erledigt: `IRoutingStrategy` und `WorkflowRoutingStrategy` fuer sequentielle DSL-Workflows mit Step-Approval zwischen Nodes
- Erledigt: Konkreter `LongTermMemoryRecallProvider` in `Nexus.Memory` fuer recall-basiertes Rehydrating nach Compaction
- Offen: LLM-basierte Router-Strategien, parallele/verzweigte Workflow-Ausfuehrung und weitergehende Routing-Decision-Typen

## Warum ist das sinnvoll?

**Das ist der wichtigste fehlende Building Block in Nexus.**

Jeder Entwickler, der mit Nexus einen agentic Workflow baut, muss aktuell den "Agent Loop" selbst implementieren:

1. User-Nachricht an LLM senden
2. Response parsen: Enthält sie Tool-Calls?
3. Wenn ja: Tools ausführen, Ergebnisse zurück an LLM
4. Repeat bis Agent "fertig" ist oder Budget erschöpft
5. Fehlerbehandlung: Was wenn das Context Window voll ist? Was wenn der Output abgeschnitten wird?
6. Wann stoppt der Loop? (Max Turns, Budget, User-Abbruch, Error)

Claude Code löst das mit einem einzigen `async function* query()` Generator, der ~500 Zeilen umfasst und ALL diese Concerns in einem robusten, streaming-fähigen Loop orchestriert. Das ist das absolute Herzstück der gesamten Anwendung.

**Nexus' ChatSession macht aktuell nur ein einfaches `GetStreamingResponseAsync` — kein Tool-Loop, keine Recovery, kein Budget-Check.**

Ohne diesen Building Block ist Nexus eine Sammlung von Werkzeugen ohne Werkbank.

## Was muss getan werden?

### Neues Package: `Nexus.AgentLoop` (oder in `Nexus.Core` integrieren)

### Kern-Abstraktion: `IAgentLoop`

```csharp
public interface IAgentLoop
{
    /// Führt eine komplette Agent-Session aus (multi-turn mit Tool Calls).
    /// Streamt Events für jeden Schritt.
    IAsyncEnumerable<AgentLoopEvent> RunAsync(
        AgentLoopOptions options,
        CancellationToken ct = default);
}

public record AgentLoopOptions
{
    public required IAgent Agent { get; init; }
    public required IList<ChatMessage> Messages { get; init; }
    public AgentBudget? Budget { get; init; }
    public int MaxTurns { get; init; } = 50;
    public StopCondition? StopWhen { get; init; }
    public CompactionStrategy? Compaction { get; init; }
    public IToolPermissionHandler? PermissionHandler { get; init; }
}
```

### Event-Typen (Streaming)

```csharp
public abstract record AgentLoopEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record TextChunkLoopEvent(string Text) : AgentLoopEvent;
public record ThinkingChunkLoopEvent(string Text) : AgentLoopEvent;
public record ToolCallStartedEvent(string ToolName, object Input) : AgentLoopEvent;
public record ToolCallCompletedEvent(string ToolName, ToolResult Result) : AgentLoopEvent;
public record ToolCallProgressEvent(string ToolName, string Progress) : AgentLoopEvent;
public record CompactionTriggeredEvent(int TokensBefore, int TokensAfter) : AgentLoopEvent;
public record TurnCompletedEvent(int TurnNumber, TokenUsage Usage) : AgentLoopEvent;
public record BudgetWarningEvent(decimal CostSoFar, decimal MaxCost) : AgentLoopEvent;
public record LoopCompletedEvent(LoopStopReason Reason, AgentResult FinalResult) : AgentLoopEvent;
public record LoopErrorEvent(Exception Error, bool WillRetry) : AgentLoopEvent;

public enum LoopStopReason
{
    AgentCompleted,      // Agent hat keine Tool-Calls mehr → fertig
    MaxTurnsReached,
    BudgetExhausted,
    UserCancelled,
    Error,
    StopConditionMet,
    CompactionFailed,     // Context konnte nicht weiter komprimiert werden
    StepRejected          // User hat einen Step nicht approved
}
```

Step-Level Approval Events gehoeren nicht in die `AgentLoopEvent`-Hierarchie.
Sie leben auf Outer-Loop-Ebene als Routing-Entscheidungen bzw. Approval-Wartezustaende
der `IRoutingStrategy`.

### State Machine des Loops

```
┌─────────────────────────────────────────────────────┐
│                 INNER LOOP (ChatAgent)               │
│            Ein Agent arbeitet seinen Task ab          │
│                                                     │
│  ┌──────────┐    ┌──────────────┐    ┌──────────┐  │
│  │  Send to  │───▶│ Parse Response│───▶│ Has Tool │  │
│  │   Model   │    │  (Stream)    │    │  Calls?  │  │
│  └──────────┘    └──────────────┘    └─────┬─────┘  │
│       ▲                                    │        │
│       │                              ┌─────┴─────┐  │
│       │                              │Yes      No│  │
│       │                              ▼           ▼  │
│       │                        ┌──────────┐  ┌────┐ │
│       │                        │ Tool     │  │Done│ │
│       │                        │ Approval?│  └────┘ │
│       │                        └──┬───┬───┘         │
│       │                   Granted │   │ Denied      │
│       │                           ▼   ▼             │
│       │                        ┌──────────┐         │
│       │                        │ Execute  │         │
│       │                        │  Tools   │         │
│       │                        └────┬─────┘         │
│       │                             │               │
│       │          ┌──────────────────┤               │
│       │          ▼                  ▼               │
│       │    ┌──────────┐      ┌──────────┐           │
│       │    │  Budget   │      │ Context  │           │
│       │    │  Check    │      │  Check   │           │
│       │    └─────┬────┘      └─────┬────┘           │
│       │          │OK               │Full            │
│       │          ▼                 ▼                 │
│       │                      ┌──────────┐           │
│       └──────────────────────│ Compact  │           │
│                              └──────────┘           │
└─────────────────────────────────────────────────────┘

  Tool Approval = PermissionToolMiddleware (ToolAnnotations.RequiresApproval)
  Entscheidet PRO TOOL-CALL ob der User gefragt wird.
```

### Outer Loop: Step-Level Approval (zwischen Workflow-Steps)

Step Approval lebt NICHT im Inner Loop, sondern im **AgentLoop / IRoutingStrategy**.
Der Agent arbeitet seinen Task komplett ab → danach entscheidet die RoutingStrategy
ob vor dem nächsten Step ein Approval nötig ist.

```
┌──────────────────────────────────────────────────────────────┐
│                   OUTER LOOP (AgentLoop)                     │
│              RoutingStrategy steuert den Workflow            │
│                                                              │
│  ┌──────────┐     ┌──────────────┐     ┌─────────────────┐  │
│  │  Routing  │────▶│  Agent runs   │────▶│ Step completed  │  │
│  │  decides  │     │  Inner Loop   │     │                 │  │
│  │ next Step │     │  (full task)  │     └────────┬────────┘  │
│  └──────────┘     └──────────────┘              │            │
│       ▲                                         │            │
│       │                                  ┌──────┴───────┐    │
│       │                                  │ Requires     │    │
│       │                                  │ Approval?    │    │
│       │                                  └──┬───────┬───┘    │
│       │                                No   │       │ Yes    │
│       │                                     │       ▼        │
│       │                                     │  ┌─────────┐   │
│       │                                     │  │ Ask User│   │
│       │                                     │  └──┬──┬───┘   │
│       │                             ┌───────┘  OK │  │ Reject│
│       │                             │             │  ▼       │
│       │                             │             │ ┌─────┐  │
│       │                             ▼             │ │Stop │  │
│       └─────────────────────────────┘             │ └─────┘  │
│                                     ▲             │          │
│                                     └─────────────┘          │
└──────────────────────────────────────────────────────────────┘

  Step Approval = IApprovalGate via WorkflowRoutingStrategy
  Entscheidet ZWISCHEN STEPS ob der User den Output prüfen muss.
```

### Default-Implementation

```csharp
public class DefaultAgentLoop : IAgentLoop
{
    private readonly IToolRegistry _tools;
    private readonly IToolExecutor _toolExecutor; // Neu! (siehe 003)
    private readonly ICostTracker _costTracker;   // Neu! (siehe 005)
    private readonly ICompactionService _compaction; // Neu! (siehe 002)
    private readonly IEnumerable<IAgentMiddleware> _middleware;

    public async IAsyncEnumerable<AgentLoopEvent> RunAsync(
        AgentLoopOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = options.Messages.ToList();
        var turn = 0;

        while (!ct.IsCancellationRequested)
        {
            // Budget check
            if (options.Budget is not null && _costTracker.TotalCost >= options.Budget.MaxCostUsd)
            {
                yield return new LoopCompletedEvent(LoopStopReason.BudgetExhausted, ...);
                yield break;
            }

            // Max turns check
            if (++turn > options.MaxTurns)
            {
                yield return new LoopCompletedEvent(LoopStopReason.MaxTurnsReached, ...);
                yield break;
            }

            // Context window check → Auto-compact
            if (_compaction.ShouldCompact(messages, options.Agent))
            {
                var result = await _compaction.CompactAsync(messages, ct);
                messages = result.CompactedMessages;
                yield return new CompactionTriggeredEvent(result.TokensBefore, result.TokensAfter);
            }

            // Call model (streaming)
            var toolCalls = new List<ToolCallInfo>();
            await foreach (var chunk in CallModelStreamingAsync(messages, options, ct))
            {
                if (chunk is TextChunk text)
                    yield return new TextChunkLoopEvent(text.Content);
                else if (chunk is ToolUseChunk tool)
                    toolCalls.Add(tool.Info);
            }

            // No tool calls → Agent is done
            if (toolCalls.Count == 0)
            {
                yield return new LoopCompletedEvent(LoopStopReason.AgentCompleted, ...);
                yield break;
            }

            // Execute tools (with concurrency, permissions, streaming)
            await foreach (var toolEvent in _toolExecutor.ExecuteAsync(toolCalls, ct))
            {
                yield return toolEvent;
            }

            // Add tool results to messages for next turn
            messages.AddRange(BuildToolResultMessages(toolCalls));

            yield return new TurnCompletedEvent(turn, GetUsage());
        }
    }
}
```

### Step-Level Approval: Wo es wirklich lebt

Step Approval ist KEIN Concern des Inner Loops. Es lebt in der **WorkflowRoutingStrategy**,
die den AgentLoop zwischen Steps steuert:

```csharp
public class WorkflowRoutingStrategy : IRoutingStrategy
{
    private readonly WorkflowDefinition _workflow;
    private readonly IApprovalGate _approvalGate;

    public async IAsyncEnumerable<RoutingDecision> RouteAsync(
        AgentStepResult previousResult,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var currentNode = GetCurrentNode(previousResult);
        var nextNodes = GetNextNodes(currentNode, previousResult);

        foreach (var nextNode in nextNodes)
        {
            // Step Approval Check: ZWISCHEN den Steps
            if (currentNode.RequiresApproval)
            {
                yield return new RoutingDecision.WaitForApproval(
                    StepId: currentNode.Id,
                    Description: $"Step '{currentNode.Id}' abgeschlossen. Output prüfen?",
                    StepOutput: previousResult.Output);

                var approval = await _approvalGate.RequestApprovalAsync(
                    new ApprovalRequest(currentNode.Id, "Step approval", previousResult.Output),
                    timeout: null, ct);

                if (!approval.IsApproved)
                {
                    yield return new RoutingDecision.Stop(LoopStopReason.StepRejected);
                    yield break;
                }

                // User kann den Output modifizieren bevor der nächste Step startet
                if (approval.ModifiedContext is not null)
                    previousResult = previousResult with 
                    { 
                        Output = approval.ModifiedContext.ToString() 
                    };
            }

            yield return new RoutingDecision.RunAgent(
                AgentId: nextNode.Agent,
                Task: ResolveVariables(nextNode.Task, previousResult));
        }
    }
}

// Workflow YAML mit Step Approval:
// nodes:
//   - id: design
//     agent: designer
//     requiresApproval: true    ← nach diesem Step wird der User gefragt
//   - id: implement
//     agent: coder
//     requiresApproval: false   ← läuft automatisch weiter
```
```

### Integration mit bestehendem Nexus

```csharp
// So sieht die DX für den Entwickler aus:
var builder = NexusBuilder.Create()
    .AddAgent("coder", agent => agent
        .WithSystemPrompt("You are a coding assistant.")
        .WithTools("file_read", "file_write", "bash")
        .WithBudget(maxCost: 1.0m, maxTurns: 20))
    .AddAgentLoop()          // ← NEU: Registriert DefaultAgentLoop
    .AddCompaction()         // ← NEU: Registriert Auto-Compaction
    .AddCostTracking()       // ← NEU: Registriert Cost Tracker
    .AddToolConcurrency();   // ← NEU: Registriert concurrent ToolExecutor

var nexus = builder.Build();
var loop = nexus.GetRequiredService<IAgentLoop>();

// Einfachste Nutzung:
await foreach (var evt in loop.RunAsync(new AgentLoopOptions
{
    Agent = nexus.GetAgent("coder"),
    Messages = [new ChatMessage(ChatRole.User, "Fix the bug in auth.cs")]
}))
{
    switch (evt)
    {
        case TextChunkLoopEvent text: Console.Write(text.Text); break;
        case ToolCallStartedEvent tool: Console.WriteLine($"\n🔧 {tool.ToolName}..."); break;
        case LoopCompletedEvent done: Console.WriteLine($"\n✅ {done.Reason}"); break;
    }
}
```

## Detail-Informationen

### Vergleich mit Claude Code

| Aspekt | Claude Code (`query()`) | Nexus (aktuell) | Nexus (mit AgentLoop) |
|--------|------------------------|------------------|----------------------|
| Multi-turn Tool Loop | ✅ Voll automatisch | ❌ Manuell bauen | ✅ Automatisch |
| Streaming Events | ✅ AsyncGenerator | ⚠️ Nur Text-Chunks | ✅ Alle Event-Typen |
| Auto-Compaction | ✅ 4 Stufen | ❌ Nicht vorhanden | ✅ Pluggable |
| Budget Enforcement | ✅ Token + USD | ⚠️ Nur Definition | ✅ Aktiv enforced |
| Max-Output Recovery | ✅ 3 Retries | ❌ Nicht vorhanden | ✅ Konfigurierbar |
| Stop Conditions | ✅ 6 Gründe | ❌ Nicht vorhanden | ✅ Erweiterbar |
| Tool Permission Check | ✅ Per-Tool | ❌ Nicht vorhanden | ✅ Via Hook |

### Claude Code Recovery-Mechanismen (die wir übernehmen sollten)

1. **Auto-Compact**: Wenn Context Window ~80% voll → automatisch komprimieren
2. **Reactive-Compact**: Vorhersage ob der nächste Response überlaufen wird → preemptiv komprimieren
3. **Max-Output-Token Recovery**: Wenn Response abgeschnitten → bis zu 3x Retry mit erhöhtem Token-Limit
4. **Tool Error Recovery**: Wenn ein Tool fehlschlägt → Error als Tool-Result zurückgeben, Agent entscheiden lassen

### Design-Entscheidungen

- **`IAsyncEnumerable<AgentLoopEvent>` statt Callbacks**: Composable, testbar, LINQ-kompatibel
- **Kein eigener Thread**: Der Loop läuft im aufrufenden Thread — der Consumer steuert das Tempo
- **Middleware-Integration**: Bestehende `IAgentMiddleware` wird pro Turn aufgerufen
- **Kein Opinion über UI**: Der Loop liefert Events — was der Consumer damit macht, ist seine Sache

### Aufwand

- Kern-Implementation: `DefaultAgentLoop` (~300-500 Zeilen)
- Event-Typen: ~100 Zeilen Records
- Integration mit bestehenden Services: ~200 Zeilen DI-Wiring
- Tests: ~500 Zeilen
- **Gesamt: ~2-3 Tage für einen erfahrenen Entwickler**
