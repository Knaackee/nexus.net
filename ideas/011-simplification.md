# 011 — Überflüssiges & Vereinfachungen

## Bestandsaufnahme: Was ist potenziell overengineered?

### 1. Workflows DSL (`Nexus.Workflows.Dsl`) — Überdenken

**Status**: Eigenes Package mit JSON/YAML Pipeline-Definitionen, Cycle Detection, Variable Resolution, Condition Evaluators.

**Problem**: Die frühere Einschätzung "premature" ist im aktuellen Repo-Stand überholt. Das Package ist implementiert und dokumentiert; offen sind vor allem weitere Laufzeit-Integrationen wie `RequiresApproval`-basierte Routing-/AgentLoop-Pfade.

**Empfehlung**: 
- **Nicht löschen**; das Package ist bereits Teil des Stacks
- Priorität für neue Investitionen unterhalb von Agent Loop, Sessions und Compaction halten
- Focus auf den Agent Loop (001) der das gleiche Problem einfacher löst
- Workflows DSL wird relevant wenn Nexus in No-Code/Low-Code Kontexten genutzt wird — für Developer-first ist es weiterhin weniger wichtig als der Agent Loop
- Alternative: Workflows als Code (C# Builder Pattern) statt als DSL — das hat Nexus bereits mit der Orchestration API

### 2. A2A Protocol (`Nexus.Protocols.A2A`) — Timing

**Status**: Agent-to-Agent Protocol via JSON-RPC über HTTP.

**Problem**: A2A ist ein Google-getriebener Standard der noch wenig Adoption hat. Claude Code nutzt A2A nicht — es orchestriert Agents lokal (Agent Tool, Coordinator Mode) oder remote (CCR Service).

**Empfehlung**:
- Behalten, aber nicht als Kern-Feature positionieren
- Erst wichtig wenn Enterprise-Federated-Agent-Szenarien real werden
- Für v1.0 Focus auf lokale Multi-Agent (Coordinator Pattern) statt föderierte Agents

### 3. AG-UI Protocol (`Nexus.Protocols.AgUi`) — Scope

**Status**: Event Bridge für SSE Streaming zu Frontends.

**Problem**: User hat explizit gesagt "Es geht nicht um UIs". AG-UI ist ein UI-zentriertes Protokoll.

**Empfehlung**:
- Behalten (es funktioniert und ist fertig)
- Aber nicht als primären DX-Fokus — die meisten Developer Experience Verbesserungen passieren auf der Agent-Loop-Ebene, nicht auf der UI-Streaming-Ebene

### 4. Inter-Agent Messaging (`Nexus.Messaging`) — Vereinfachen?

**Status**: Rich Messaging mit Pub/Sub, Point-to-Point, Request/Response, Broadcast, Dead Letter Queue.

**Problem**: Claude Code nutzt ein viel simpleres Modell:
- Task Notifications (XML-basiert in User Messages)
- Scratchpad-Verzeichnis für geteilte Dateien
- SendMessage Tool für direkte Kommunikation

Die volle Messaging-Palette (Dead Letter Queue, Broadcast) ist Enterprise-Messaging — für Agent-Kommunikation overkill.

**Empfehlung**:
- Kern behalten (Point-to-Point, einfaches Pub/Sub)
- Dead Letter Queue, Broadcast etc. als optionale Features, nicht als Standard-Dokumentation
- Focus auf das was Claude Code zeigt: **Tool-basierte Kommunikation** (Agent schickt Message via Tool, nicht via Message Bus)

---

## Was VEREINFACHT werden sollte

### A. Builder API vereinfachen

**Aktuell** (Nexus):
```csharp
var builder = NexusBuilder.Create();
builder.AddCore();
builder.AddAgent("coder", agent => { /* 10 Zeilen Config */ });
builder.AddMemory(memory => { /* ... */ });
builder.AddGuardrails(guardrails => { /* ... */ });
builder.AddOrchestration(orch => { /* ... */ });
builder.AddTelemetry(telemetry => { /* ... */ });
// ... 30+ Zeilen bevor der Agent überhaupt was tut
```

**Besser** (inspiriert von Claude Code's Simplicity):
```csharp
// Minimal: 3 Zeilen bis zum laufenden Agent
var agent = Nexus.CreateAgent("coder")
    .WithModel(chatClient)
    .WithTools("file_read", "file_edit", "bash")
    .Build();

await foreach (var evt in agent.RunAsync("Fix the bug in auth.cs"))
{
    Console.Write(evt.Text);
}
```

**Das bedeutet**:
- `Nexus.CreateAgent()` als Top-Level Entry Point (neben dem bestehenden Builder)
- Sensible Defaults für alles (Compaction, Cost Tracking, etc.)
- Zero-Config Modus der "einfach funktioniert"

### B. Standard-Tool-Set bereitstellen

Claude Code kommt mit 15+ eingebauten Tools. Nexus hat **keine eingebauten Tools** — der Developer muss alles selbst registrieren.

**Empfehlung**: Package `Nexus.Tools.Standard` mit:
- `FileReadTool` — Datei lesen
- `FileWriteTool` — Datei schreiben/erstellen
- `FileEditTool` — Datei partiell editieren (Search & Replace)
- `BashTool` / `ShellTool` — Shell-Commands ausführen
- `GlobTool` — Files per Pattern finden
- `GrepTool` — Text in Files suchen
- `WebFetchTool` — HTTP GET
- `WebSearchTool` — Web-Suche

```csharp
// Statt 50 Zeilen Tool-Registration:
builder.AddStandardTools(); // Registriert alle Standard-Tools
// Oder selektiv:
builder.AddStandardTools(tools => tools.FileSystem().Shell());
```

### C. "Batteries Included" Default-Konfiguration

```csharp
// Der "Ich will einfach einen Agent der funktioniert" Pfad:
var nexus = Nexus.CreateDefault(chatClient);

// Enthält automatisch:
// - Agent Loop mit Auto-Compaction
// - Standard-Tools (FileSystem, Shell, Search)
// - Cost Tracking
// - In-Memory Session Store
// - Interactive Permissions (Console-basiert)
// - Basic Telemetry (Console Logging)
```

---

## Zusammenfassung

| Bereich | Empfehlung |
|---------|------------|
| Workflows DSL | Priorität senken, nicht für v1.0 bewerben |
| A2A Protocol | Behalten, aber nicht fokussieren |
| AG-UI Protocol | Behalten, kein DX-Fokus |
| Messaging | Vereinfachen, Tool-basierte Kommunikation bevorzugen |
| Builder API | Vereinfachten Entry Point hinzufügen |
| Standard-Tools | Neues Package mit eingebauten Tools |
| Default-Config | "Batteries Included" Modus |
