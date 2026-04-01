# 003 — Tool Concurrency & Streaming Execution

## Priorität: 🟠 Hoch

## Warum ist das sinnvoll?

**Agents werden 3-5x schneller wenn read-only Tools parallel laufen.**

Wenn ein Agent 5 Files lesen will, dauert das aktuell 5 sequentielle API-Calls. Claude Code partitioniert Tool-Calls intelligent:

- **Read-only Tools** (file_read, glob, web_search): Laufen parallel (bis zu 10 gleichzeitig)
- **Write Tools** (file_write, file_edit, bash): Laufen seriell mit exklusivem Zugriff
- **Streaming**: Ergebnisse werden live gestreamt, nicht erst am Ende gesammelt

Nexus hat aktuell **keine Tool-Execution-Abstraction** — Tools werden direkt im Agent aufgerufen. Es gibt keine zentrale Stelle die Concurrency, Permission-Checks oder Progress-Streaming koordiniert.

## Was muss getan werden?

### Erweiterung von `Nexus.Core` (oder neues Package)

### 1. Tool-Metadaten erweitern

```csharp
public interface ITool
{
    // ... bestehende Properties ...

    /// Gibt an ob dieses Tool sicher parallel mit anderen Tools ausgeführt werden kann.
    /// Default: false (seriell).
    bool IsConcurrencySafe { get; }

    /// Optionale Methode: Prüft für einen konkreten Input ob Parallelausführung sicher ist.
    /// z.B. file_read ist generell safe, aber file_write nur wenn verschiedene Dateien.
    bool IsConcurrencySafe(object? input) => IsConcurrencySafe;
}

// Bestehende LambdaTool erweitern:
toolRegistry.Register(new LambdaTool("file_read", "Read a file",
    execute: async (input, ctx, ct) => ToolResult.Success(await File.ReadAllTextAsync(path, ct)),
    isConcurrencySafe: true  // ← NEU
));
```

### 2. Tool Executor Service

```csharp
public interface IToolExecutor
{
    /// Führt eine Batch von Tool-Calls aus mit intelligenter Parallelisierung.
    /// Streamt Fortschritt und Ergebnisse.
    IAsyncEnumerable<ToolExecutionEvent> ExecuteAsync(
        IReadOnlyList<ToolCallInfo> toolCalls,
        ToolExecutionContext context,
        CancellationToken ct = default);
}

public record ToolCallInfo(string ToolName, string ToolUseId, object Input);

public record ToolExecutionContext
{
    public IToolRegistry Tools { get; init; }
    public IToolPermissionHandler? Permissions { get; init; }
    public int MaxConcurrency { get; init; } = 10;
}

// Events:
public abstract record ToolExecutionEvent;
public record ToolStartedEvent(string ToolUseId, string ToolName) : ToolExecutionEvent;
public record ToolProgressEvent(string ToolUseId, string Message) : ToolExecutionEvent;
public record ToolCompletedEvent(string ToolUseId, ToolResult Result) : ToolExecutionEvent;
public record ToolErrorEvent(string ToolUseId, string ToolName, Exception Error) : ToolExecutionEvent;
public record ToolPermissionDeniedEvent(string ToolUseId, string ToolName, string Reason) : ToolExecutionEvent;
```

### 3. Default Implementation: Partitioned Executor

```csharp
public class PartitionedToolExecutor : IToolExecutor
{
    public async IAsyncEnumerable<ToolExecutionEvent> ExecuteAsync(
        IReadOnlyList<ToolCallInfo> toolCalls,
        ToolExecutionContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Phase 1: Partition in concurrent-safe und nicht-safe
        var (concurrent, serial) = Partition(toolCalls, context.Tools);

        // Phase 2: Concurrent-safe Tools parallel ausführen
        if (concurrent.Count > 0)
        {
            await foreach (var evt in ExecuteConcurrentBatch(concurrent, context, ct))
                yield return evt;
        }

        // Phase 3: Non-safe Tools seriell ausführen
        foreach (var call in serial)
        {
            await foreach (var evt in ExecuteSingle(call, context, ct))
                yield return evt;
        }
    }

    private (List<ToolCallInfo> concurrent, List<ToolCallInfo> serial) Partition(
        IReadOnlyList<ToolCallInfo> calls, IToolRegistry tools)
    {
        var concurrent = new List<ToolCallInfo>();
        var serial = new List<ToolCallInfo>();

        foreach (var call in calls)
        {
            var tool = tools.Get(call.ToolName);
            if (tool?.IsConcurrencySafe(call.Input) == true)
                concurrent.Add(call);
            else
                serial.Add(call);
        }

        return (concurrent, serial);
    }
}
```

### 4. Streaming Tool Executor (Advanced)

```csharp
/// Beginnt mit der Ausführung sobald Tool-Calls vom Model gestreamt werden
/// (nicht erst wenn die komplette Response da ist).
public class StreamingToolExecutor : IToolExecutor
{
    private readonly Channel<ToolCallInfo> _incoming = Channel.CreateUnbounded<ToolCallInfo>();
    private readonly Channel<ToolExecutionEvent> _outgoing = Channel.CreateUnbounded<ToolExecutionEvent>();

    /// Fügt einen Tool-Call hinzu (kann aufgerufen werden während das Model noch streamt).
    public void Enqueue(ToolCallInfo call) => _incoming.Writer.TryWrite(call);

    /// Signalisiert dass keine weiteren Tool-Calls kommen.
    public void Complete() => _incoming.Writer.Complete();

    // Intern: Worker liest aus _incoming, prüft Concurrency-Safety,
    // startet parallele/serielle Ausführung, schreibt Events in _outgoing.
}
```

## Detail-Informationen

### Wie Claude Code das macht

1. **`toolOrchestration.ts`**: Partitioniert Tool-Calls in `readOnly` und `nonReadOnly` Batches
2. **Read-Only Batch**: Alle laufen parallel via `Promise.all()`, max 10 concurrent (konfigurierbar via `CLAUDE_CODE_MAX_TOOL_USE_CONCURRENCY`)
3. **Non-Read-Only**: Laufen strikt seriell, je einer nach dem anderen
4. **`StreamingToolExecutor`**: Beginnt Tool-Ausführung sobald der Parser einen Tool-Call identifiziert (bevor die Response komplett ist)
5. **Sibling Abort**: Wenn ein Tool fehlschlägt, werden nur die parallel laufenden Geschwister-Tools abgebrochen (nicht der gesamte Turn)
6. **Progress Streaming**: Tools können während der Ausführung Progress-Messages yielden (z.B. Bash zeigt stdin/stdout live)

### Was Nexus aktuell anders macht

- Tools werden im Agent direkt aufgerufen — keine zentrale Execution-Schicht
- Parallel-Orchestration existiert auf **Agent-Ebene** (mehrere Agents parallel), nicht auf **Tool-Ebene** (mehrere Tools eines Agents parallel)
- Kein Progress-Streaming für einzelne Tool-Ausführungen

### Beispiel-Szenario: Warum das wichtig ist

```
Agent bekommt Aufgabe: "Analysiere die Architektur dieses Projekts"

Tool-Calls in einem Turn:
1. file_read("src/main.ts")           ← read-only
2. file_read("src/config.ts")         ← read-only
3. file_read("package.json")          ← read-only
4. glob("src/**/*.ts")                ← read-only
5. bash("git log --oneline -10")      ← write (Prozess)

Ohne Concurrency: 5 sequentielle Calls = 5 × 200ms = 1000ms
Mit Concurrency:   4 parallel + 1 seriell = 200ms + 200ms = 400ms
```

**2.5x schneller für einen einzigen Turn. Das multipilziert sich über eine ganze Session.**

### Aufwand

- ITool Erweiterung (IsConcurrencySafe): ~50 Zeilen
- PartitionedToolExecutor: ~200 Zeilen
- StreamingToolExecutor (Advanced): ~300 Zeilen
- Events: ~50 Zeilen
- Tests: ~400 Zeilen
- **Gesamt: ~1-2 Tage**
