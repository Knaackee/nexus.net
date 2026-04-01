# 002 — Context Window Management & Auto-Compaction

## Priorität: 🔴 Kritisch

## Status

Stand: 2026-04-01

- Erledigt: `Nexus.Compaction` Package existiert mit `ITokenCounter`, `IContextWindowMonitor`, `ICompactionStrategy` und `ICompactionService`
- Erledigt: `DefaultCompactionService`, `MicroCompactionStrategy` und `SummaryCompactionStrategy` sind implementiert und ueber `NexusBuilder.AddCompaction(...)` registrierbar
- Erledigt: `DefaultAgentLoop` kann Compaction vor einem Turn ausloesen und emittiert dafuer ein `CompactionTriggeredLoopEvent`
- Erledigt: Unit-Tests decken Micro-Compaction, Summary-Compaction, Strategy-Priorisierung und Auto-Compact-Trigger ab
- Offen: Threshold-Listener-API, `MemoryExtractionStrategy` und eine dedizierte Middleware-/Session-Memory-Integration

## Warum ist das sinnvoll?

**Ohne automatische Context-Management bricht jede Agent-Session nach wenigen Turns zusammen.**

Nexus hat aktuell `ContextWindowOptions` mit Trimming-Strategien (SlidingWindow, SummarizeAndTruncate etc.), aber diese greifen nur beim **initialen Aufbau** des Kontexts. Was fehlt:

- **Laufzeit-Monitoring**: Wie viele Tokens sind gerade im Context? Wann wird es kritisch?
- **Automatische Compaction**: Wenn das Window voll wird → automatisch zusammenfassen
- **Micro-Compaction**: Alte Tool-Ergebnisse komprimieren ohne die ganze Konversation neu zu summarizen
- **Budget-Tracking**: Wie viel Platz brauche ich noch für die Antwort?

Claude Code hat **vier Compaction-Stufen**, die ineinander greifen:

| Stufe | Trigger | Aktion | Token-Einsparung |
|-------|---------|--------|-------------------|
| Micro-Compact | Alte Tool-Results | Tool-Outputs durch `[komprimiert]` ersetzen | Moderat (10-30%) |
| Auto-Compact | Context ≥80% | Gesamte Konversation summarizen | Stark (50-70%) |
| Reactive-Compact | Vorhersage: nächster Response wird überlaufen | Preemptive Summary | Stark (50-70%) |
| Session-Memory | Sehr alte Nachrichten | In persistenten Memory extrahieren | Maximal (80%+) |

## Was muss getan werden?

### Neues Package: `Nexus.Context` (oder Erweiterung von `Nexus.Memory`)

### 1. Token Counter Service

```csharp
public interface ITokenCounter
{
    /// Zählt Tokens für eine Nachricht (model-spezifisch).
    int CountTokens(ChatMessage message, string? modelId = null);

    /// Zählt Tokens für eine Sequenz von Nachrichten inkl. System Prompt.
    int CountTokens(IEnumerable<ChatMessage> messages, string? systemPrompt = null, string? modelId = null);
}

public interface IContextWindowMonitor
{
    /// Aktueller Token-Verbrauch der Konversation.
    int CurrentTokenCount { get; }

    /// Maximales Context Window des Models.
    int MaxContextTokens { get; }

    /// Reserviert für die Antwort (Output-Tokens).
    int ReservedForOutput { get; }

    /// Effektiv verfügbarer Platz: Max - Reserved - Current.
    int AvailableTokens { get; }

    /// Prozentualer Füllstand (0.0 - 1.0).
    double FillRatio { get; }

    /// Registriert Listener für Threshold-Überschreitungen.
    IDisposable OnThreshold(double ratio, Action<ContextWindowAlert> callback);
}
```

### 2. Compaction Pipeline

```csharp
public interface ICompactionStrategy
{
    /// Kann diese Strategie auf den aktuellen Zustand angewendet werden?
    bool ShouldCompact(CompactionContext context);

    /// Führt die Komprimierung durch.
    Task<CompactionResult> CompactAsync(CompactionContext context, CancellationToken ct);

    /// Priorität (niedrigere = wird zuerst versucht). Micro=10, Auto=50, Full=100.
    int Priority { get; }
}

public record CompactionContext
{
    public required IList<ChatMessage> Messages { get; init; }
    public required IContextWindowMonitor Monitor { get; init; }
    public required IChatClient ChatClient { get; init; }
    public string? SystemPrompt { get; init; }
}

public record CompactionResult
{
    public required IList<ChatMessage> CompactedMessages { get; init; }
    public required int TokensBefore { get; init; }
    public required int TokensAfter { get; init; }
    public required string StrategyUsed { get; init; }
}
```

### 3. Eingebaute Compaction-Strategien

```csharp
/// Ersetzt alte Tool-Results durch komprimierte Platzhalter.
/// Niedrigste Kosten, schnellstes, aber geringstes Einsparpotential.
public class MicroCompactionStrategy : ICompactionStrategy
{
    public int Priority => 10;

    // Komprimierbare Tool-Typen: file_read, bash, web_search, etc.
    // Nur Results die > N Turns alt sind
    // Ersetzt durch "[Tool-Ergebnis komprimiert: {summary}]"
}

/// Summarized die gesamte bisherige Konversation via LLM Call.
/// Höhere Kosten (ein LLM-Call), aber starke Einsparung.
public class SummaryCompactionStrategy : ICompactionStrategy
{
    public int Priority => 50;

    // Gruppiert Messages in API-Rounds
    // Sendet Batches parallel an LLM zur Zusammenfassung
    // Ersetzt alle Messages durch eine Summary-Message
    // Behält die letzten N Turns unverändert (für Kontext)
}

/// Extrahiert wichtige Fakten in persistenten Memory, löscht alte Messages.
/// Maximale Einsparung, aber verliert Detail-Kontext.
public class MemoryExtractionCompactionStrategy : ICompactionStrategy
{
    public int Priority => 100;

    // Extrahiert Key Facts via LLM
    // Speichert in IWorkingMemory oder IConversationStore
    // Entfernt alle alten Messages
    // Injected Summary + Memory-Referenzen am Anfang
}
```

### 4. Compaction Service (Orchestrator)

```csharp
public interface ICompactionService
{
    /// Prüft ob Compaction nötig ist (basierend auf registrierten Strategien).
    bool ShouldCompact(IList<ChatMessage> messages, IAgent agent);

    /// Führt die passende Compaction-Strategie aus.
    Task<CompactionResult> CompactAsync(
        IList<ChatMessage> messages,
        IAgent agent,
        CancellationToken ct);
}

public class DefaultCompactionService : ICompactionService
{
    private readonly IEnumerable<ICompactionStrategy> _strategies;
    private readonly IContextWindowMonitor _monitor;

    // Probiert Strategien nach Priorität (niedrigste zuerst)
    // Stoppt sobald eine Strategie genug Tokens freigibt
    // Circuit-Breaker: Nach 3 fehlgeschlagenen Compactions → aufgeben
}
```

### 5. Konfiguration

```csharp
public record CompactionOptions
{
    /// Ab welchem Füllstand wird Auto-Compact getriggert (default: 0.80).
    public double AutoCompactThreshold { get; init; } = 0.80;

    /// Warnungs-Threshold für Events (default: 0.70).
    public double WarningThreshold { get; init; } = 0.70;

    /// Wie viele Tokens für den Output reserviert werden (default: 8192).
    public int OutputTokenReserve { get; init; } = 8192;

    /// Maximale Anzahl fehlgeschlagener Compactions bevor aufgegeben wird.
    public int MaxConsecutiveFailures { get; init; } = 3;

    /// Welche Tool-Typen bei Micro-Compact komprimierbar sind.
    public HashSet<string> CompactableToolTypes { get; init; } = ["file_read", "bash", "web_search", "glob"];

    /// Minimales Alter (in API-Rounds) bevor ein Tool-Result micro-compacted wird.
    public int MicroCompactMinAge { get; init; } = 3;
}

// Registration:
builder.AddCompaction(options =>
{
    options.AutoCompactThreshold = 0.80;
    options.OutputTokenReserve = 8192;
});
```

## Detail-Informationen

### Wie Claude Code Compaction implementiert

1. **Token-Tracking**: Jeder API-Call liefert `usage.input_tokens` zurück. Claude Code trackt das kumulativ.
2. **Auto-Compact Trigger**: `effectiveContextWindow = maxTokens - outputReserve`. Wenn `currentTokens > effectiveContextWindow - 13_000` → Auto-Compact.
3. **Micro-Compact**: Ersetzt Tool-Results älterer Rounds durch `[komprimiert]`. Betrifft nur bestimmte Tool-Typen (File Read, Bash, Search etc.).
4. **Summary-Compact**: Gruppiert Messages in Batches, sendet parallel an LLM mit Prompt "Summarize this conversation segment". Result: eine einzige Summary-Message.
5. **Post-Compact Cleanup**: Nach Compaction werden bis zu 5 relevante Files und 5 Skills re-injected (weil der Kontext verloren ging).

### Warum Nexus' existierendes Context Trimming nicht reicht

Nexus' `ContextWindowOptions` (SlidingWindow, KeepFirstAndLast, etc.) sind **statische Strategien** die beim Aufbau des Kontexts greifen. Sie haben kein Konzept von:

- **Laufzeit-Monitoring**: Wann ist das Window voll?
- **LLM-basierter Summarization**: Intelligentes Zusammenfassen statt Abschneiden
- **Prioritätsbasierter Komprimierung**: Tool-Results sind weniger wichtig als User-Nachrichten
- **Inkrementeller Komprimierung**: Erst günstige Strategien, dann aufwändige

### Design Decisions

- **Token-Counting**: Nutze `tiktoken` oder model-spezifische Tokenizer. Für eine erste Version reicht auch eine Heuristik (4 chars ≈ 1 token).
- **LLM-Call für Summary**: Nutze das gleiche Model oder ein günstigeres (z.B. Haiku/Flash für Compaction).
- **Kein Breaking Change**: Die bestehenden `ContextWindowOptions` bleiben — Compaction ist ein zusätzlicher Layer der zur Laufzeit greift.

### Aufwand

- ITokenCounter + IContextWindowMonitor: ~200 Zeilen
- CompactionStrategien (Micro + Summary): ~400 Zeilen
- CompactionService: ~150 Zeilen
- Integration mit AgentLoop: ~100 Zeilen
- Tests: ~500 Zeilen
- **Gesamt: ~2-3 Tage**
