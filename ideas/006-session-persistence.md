# 006 — Session Persistence & Resume

## Priorität: 🟠 Hoch

## Status

Stand: 2026-04-01

- Erledigt: `Nexus.Sessions` Package existiert
- Erledigt: `ISessionStore`, `ISessionTranscript`, `SessionInfo`, `SessionId`
- Erledigt: InMemory- und FileSystem-Implementierungen inkl. `ReadLastAsync(...)`
- Erledigt: AgentLoop-Integration fuer Session-Erstellung, Persistierung und `ResumeLastSession`
- Erledigt: Unit-Tests fuer InMemory/FileSystem-Stores und Live-Test fuer Persistierung + Resume
- Erledigt: CLI-Resume ueber `/resume` auf Basis des file-basierten Session-Stores
- Offen: dedizierte History-Suche und Paste-Store

## Warum ist das sinnvoll?

**Agent-Sessions sind flüchtig — wenn die Anwendung abstürzt oder der User die Session unterbricht, ist alles weg.**

Nexus' `IConversationStore` speichert Chat-History in-memory. Es gibt keine Möglichkeit:
- Eine unterbrochene Session fortzusetzen
- Session-Transcripts zu durchsuchen
- Vergangene Sessions zu laden und weiterzuführen

Claude Code speichert dagegen:
- **Append-only Transcripts**: Jede Message wird sofort auf Disk persistiert (`~/.claude/projects/{slug}/`)
- **Session Resume**: `/resume` Befehl lädt letzte Session
- **History Search**: Vergangene Inputs durchsuchbar
- **Paste Store**: Große eingefügte Texte werden referenziert statt inline gespeichert
- **Cost State**: Kosten werden pro Session persistiert und bei Resume geladen

## Was muss getan werden?

### Erweiterung von `Nexus.Memory` oder neues Package `Nexus.Sessions`

### 1. Session Abstractions

```csharp
public interface ISessionStore
{
    /// Erstellt eine neue Session.
    Task<SessionInfo> CreateAsync(SessionCreateOptions options, CancellationToken ct = default);

    /// Lädt eine existierende Session.
    Task<SessionInfo?> GetAsync(SessionId id, CancellationToken ct = default);

    /// Listet Sessions (optional gefiltert).
    IAsyncEnumerable<SessionInfo> ListAsync(SessionFilter? filter = null, CancellationToken ct = default);

    /// Löscht eine Session.
    Task<bool> DeleteAsync(SessionId id, CancellationToken ct = default);
}

public interface ISessionTranscript
{
    /// Hängt eine Message an das Transcript an (append-only).
    Task AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken ct = default);

    /// Lädt alle Messages einer Session.
    IAsyncEnumerable<ChatMessage> ReadAsync(SessionId sessionId, CancellationToken ct = default);

    /// Lädt die letzten N Messages (für Resume).
    IAsyncEnumerable<ChatMessage> ReadLastAsync(SessionId sessionId, int count, CancellationToken ct = default);
}

public record SessionInfo
{
    public required SessionId Id { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
    public int MessageCount { get; init; }
    public CostSnapshot? CostSnapshot { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
}

[StronglyTypedId]
public readonly partial struct SessionId;
```

### 2. File-basierte Implementation

```csharp
/// Speichert Sessions als JSONL-Dateien auf Disk.
/// Struktur:
///   {baseDir}/
///     sessions.json              ← Session-Index
///     {sessionId}/
///       transcript.jsonl         ← Append-only Messages
///       cost.json                ← Cost Snapshot
///       metadata.json            ← Session Metadata
public class FileSessionStore : ISessionStore, ISessionTranscript
{
    private readonly string _baseDir;

    public async Task AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken ct)
    {
        var path = GetTranscriptPath(sessionId);
        var line = JsonSerializer.Serialize(message);
        await File.AppendAllLinesAsync(path, [line], ct);
    }

    public async IAsyncEnumerable<ChatMessage> ReadAsync(
        SessionId sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var path = GetTranscriptPath(sessionId);
        if (!File.Exists(path)) yield break;

        await foreach (var line in File.ReadLinesAsync(path, ct))
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return JsonSerializer.Deserialize<ChatMessage>(line)!;
        }
    }
}
```

### 3. Session-Aware Agent Loop Integration

```csharp
public record AgentLoopOptions
{
    // ... bestehende Properties ...

    /// Optionale Session-ID für automatische Persistierung und Resume.
    public SessionId? SessionId { get; init; }

    /// Wenn true, versuche letzte Session fortzusetzen.
    public bool ResumeLastSession { get; init; }
}

// Im AgentLoop:
// - Jede Message wird automatisch via ISessionTranscript.AppendAsync persistiert
// - Bei Resume werden Messages via ReadAsync geladen
// - Cost-State wird bei Session-Ende via ICostTracker.GetSnapshot() gespeichert
```

### 4. Session Resume Flow

```csharp
// Resume der letzten Session:
var sessions = await sessionStore.ListAsync(new SessionFilter
{
    OrderBy = SessionOrderBy.LastActivity,
    Limit = 1
});

var lastSession = await sessions.FirstOrDefaultAsync();
if (lastSession is not null)
{
    var messages = await sessionTranscript.ReadAsync(lastSession.Id).ToListAsync();

    await foreach (var evt in loop.RunAsync(new AgentLoopOptions
    {
        Agent = agent,
        Messages = messages,
        SessionId = lastSession.Id,
        // Session wird nahtlos fortgesetzt
    }))
    {
        // ...
    }
}
```

## Detail-Informationen

### Wie Claude Code Sessions persistiert

1. **Append-Only JSONL**: Jede Message wird als eine Zeile an `transcript.jsonl` angehängt — nie überschrieben
2. **Session Index**: `sessions.json` enthält Metadaten aller Sessions (ID, Titel, Zeitstempel)
3. **Pasted Content**: Große eingefügte Texte (>1KB) werden separat gespeichert und per Hash referenziert
4. **History Search**: `history.ts` bietet einen AsyncGenerator der durch alle Sessions navigieren kann
5. **Cost Persistence**: Token-Verbrauch und Kosten werden pro Session in `cost.json` gespeichert
6. **Resume Command**: `/resume` lädt automatisch die letzte Session oder eine spezifische Session-ID

### Design-Entscheidungen

- **JSONL statt DB**: Einfacher, portable, kein externer Dependency. Für eine Library die richtige Wahl.
- **Append-Only**: Nie bestehende Daten überschreiben → Crash-sicher, kein Datenverlust
- **Lazy Loading**: Messages werden als `IAsyncEnumerable` gestreamt, nicht komplett in Memory geladen
- **Pluggable Storage**: `ISessionStore` und `ISessionTranscript` sind Interfaces — wer will kann eine DB-Implementation bauen

### Aufwand

- Interfaces (ISessionStore, ISessionTranscript): ~100 Zeilen
- FileSessionStore: ~300 Zeilen
- AgentLoop Integration: ~150 Zeilen
- Tests: ~400 Zeilen
- **Gesamt: ~1.5-2 Tage**
