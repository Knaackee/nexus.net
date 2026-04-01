# 012 — CLI Gap Analysis: Nexus CLI vs Claude Code

## Status-Stand: Weitgehend erledigt (2026-04-01)

- Erledigt: `Nexus.Cli` nutzt jetzt `NexusDefaultHost` + `IAgentLoop` statt eines reinen Chat-Client-Loops
- Erledigt: Standard-Tools, Session-IDs und Skill-/Tool-Profile sind in die CLI integriert
- Erledigt: Slash-Commands laufen jetzt ueber `Nexus.Commands` statt ueber ein grosses `switch`-Statement
- Erledigt: persistentes `/resume` ueber Prozessgrenzen via file-basiertem Session-Store unter `.nexus/sessions`
- Erledigt: Builtin-Command-Surface fuer `/help`, `/quit`, `/status`, `/resume`, `/cost`, `/model`, `/clear` und `/compact`
- Erledigt: file-basierte Skills/Commands aus `.nexus/` und User-Scope koennen geladen werden
- Erledigt: MCP-Server koennen jetzt aus `.nexus/mcp.json` und `~/.nexus/mcp.json` geladen und als Tools registriert werden
- Offen: weitere Power-Features

## Historischer Ausgangszustand der Nexus CLI

Die Nexus Example CLI (`Nexus.Cli`) ist ein **Multi-Session Chat Client** mit:
- GitHub Copilot Authentifizierung
- Multiple parallel Chat Sessions (Name-based)
- Streaming Responses via Spectre.Console
- 8 Slash-Commands: `/new`, `/switch`, `/list`, `/remove`, `/models`, `/status`, `/cancel`, `/help`
- Kein Tool-Support, keine Agent-Loop, kein Memory

**Es ist ein Chat-Client, kein Agent.**

## Was Claude Code hat (und die Nexus CLI bräuchte)

### Tier 1: Minimal Viable Agent CLI

Um von "Chat Client" zu "Agent CLI" zu werden:

| Feature | Claude Code | Nexus CLI | Gap |
|---------|-------------|-----------|-----|
| Agent Loop (multi-turn mit Tools) | ✅ `query()` Generator | ❌ Nur Single-Turn Chat | **Kritisch** |
| Tool Execution | ✅ 30+ Tools | ❌ Keine | **Kritisch** |
| File-System Tools | ✅ Read/Write/Edit/Glob/Grep | ❌ Keine | **Kritisch** |
| Shell/Bash Tool | ✅ Mit Safety-Checks | ❌ Keins | **Kritisch** |
| Streaming Tool Results | ✅ Live Progress | ❌ N/A | **Kritisch** |
| Context Window Management | ✅ Auto-Compaction | ❌ Keins | **Hoch** |
| Cost Tracking | ✅ Token + USD | ❌ Keins | **Hoch** |

### Tier 2: Produktive Agent CLI

| Feature | Claude Code | Nexus CLI | Gap |
|---------|-------------|-----------|-----|
| Permission System | ✅ Rule-based + Interactive | ❌ Keins | **Hoch** |
| Session Persistence | ✅ Append-only Transcripts | ✅ File-basierte Session-Stores | Rest: Search/Paste-Store |
| Session Resume | ✅ `/resume` Command | ✅ `/resume` Command | Rest: History UX |
| Slash Commands Framework | ✅ 60+ Commands | ✅ Framework + Builtins + File-Commands | Rest: mehr Power-Commands |
| Memory System | ✅ MEMORY.md + Topic Files | ❌ Keins | **Hoch** |
| Git Integration | ✅ Branch, Diff, Status | ❌ Keins | **Mittel** |
| MCP Server Support | ✅ stdio/SSE/HTTP/WS | ✅ stdio + SSE via `.nexus/mcp.json`/`~/.nexus/mcp.json` | Rest: HTTP/WS/weitere UX |
| Sub-Agents | ✅ AgentTool, Coordinator | ❌ Keins | **Mittel** |
| User Interaction | ✅ AskUser in Tools | ❌ Keins | **Mittel** |

### Tier 3: Power-User Features

| Feature | Claude Code | Nexus CLI | Gap |
|---------|-------------|-----------|-----|
| Background Tasks | ✅ Task System | ❌ Keins | **Mittel** |
| Custom Skills | ✅ .claude/skills/ | ❌ Keins | **Mittel** |
| Project Config | ✅ .claude/settings.json | ❌ Keins | **Mittel** |
| Extended Thinking | ✅ Thinking blocks | ❌ Keins | **Niedrig** |
| Keyboard Shortcuts | ✅ Context-aware | ⚠️ Basis | **Niedrig** |
| Output Styles | ✅ Custom templates | ❌ Keins | **Niedrig** |
| Voice Input | ✅ Push-to-Talk | ❌ Keins | **Niedrig** |

## Roadmap: Nexus CLI zum Agent upgraden

### Phase 1: Agent-Fähig machen (benötigt: 001, 003, Standard-Tools)

```csharp
// VORHER (aktuell): Einfacher Chat
session.Send(userMessage); // → StreamingResponse → fertig

// NACHHER: Agent Loop mit Tools
var loop = nexus.GetRequiredService<IAgentLoop>();

await foreach (var evt in loop.RunAsync(new AgentLoopOptions
{
    Agent = agent,
    Messages = conversation,
    Budget = new AgentBudget { MaxCostUsd = 2.0m, MaxTurns = 30 }
}))
{
    switch (evt)
    {
        case TextChunkLoopEvent text:
            AnsiConsole.Markup(Markup.Escape(text.Text));
            break;

        case ToolCallStartedEvent tool:
            AnsiConsole.MarkupLine($"\n[yellow]⚡ {tool.ToolName}[/]");
            break;

        case ToolCallCompletedEvent tool:
            AnsiConsole.MarkupLine($"[green]✓ {tool.ToolName}[/]");
            break;

        case CompactionTriggeredEvent compact:
            AnsiConsole.MarkupLine($"[grey]📦 Compacted {compact.TokensBefore} → {compact.TokensAfter} tokens[/]");
            break;

        case BudgetWarningEvent budget:
            AnsiConsole.MarkupLine($"[yellow]💰 ${budget.CostSoFar:F4} / ${budget.MaxCost:F4}[/]");
            break;

        case LoopCompletedEvent done:
            AnsiConsole.MarkupLine($"\n[grey]─── {done.Reason} ───[/]");
            break;
    }
}
```

### Phase 2: Produktiv machen (benötigt: 004, 005, 006, 008)

```csharp
// Permissions: User wird gefragt bevor ein Tool ausgeführt wird
permissions.OnAsk = async (request) =>
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"Allow [yellow]{request.ToolName}[/]?")
            .AddChoices("Allow", "Deny", "Always Allow"));
    return choice;
};

// Session Resume: /resume lädt letzte Session
case "/resume":
    var lastSession = await sessionStore.GetLastAsync();
    if (lastSession is not null)
    {
        conversation = await transcript.ReadAsync(lastSession.Id).ToListAsync();
        AnsiConsole.MarkupLine($"[green]Resumed session '{lastSession.Title}'[/]");
    }
    break;

// Cost Display: /cost zeigt Breakdown
case "/cost":
    var tracker = nexus.GetRequiredService<ICostTracker>();
    var table = new Table();
    table.AddColumn("Model").AddColumn("Calls").AddColumn("Tokens").AddColumn("Cost");
    foreach (var (model, usage) in tracker.UsageByModel)
        table.AddRow(model, usage.CallCount.ToString(), $"{usage.InputTokens + usage.OutputTokens}", $"${usage.CostUsd:F4}");
    AnsiConsole.Write(table);
    break;
```

### Phase 3: Power-Features (benötigt: 009, 010, Sub-Agents)

```csharp
// Sub-Agent für Research:
toolRegistry.Register(new AgentTool("researcher",
    "Spawn a sub-agent for research tasks",
    agentFactory: () => nexus.CreateAgent("researcher")
        .WithSystemPrompt("You are a research specialist. Search the web and summarize findings.")
        .WithTools("web_search", "web_fetch")
        .Build()));

// Skills laden:
var skills = skillLoader.LoadFromDirectory(".nexus/skills/", SkillSource.Project);
foreach (var skill in skills)
    AnsiConsole.MarkupLine($"[grey]Loaded skill:[/] {skill.Name}");

// Memory Integration:
case "/memory":
    var memoryPath = Path.Combine(projectRoot, ".nexus", "memory", "MEMORY.md");
    if (File.Exists(memoryPath))
        AnsiConsole.Write(new Panel(File.ReadAllText(memoryPath)).Header("Memory"));
    break;
```

## Konkreter Umbau-Plan

```
Phase 1 (Agent-Fähig):
├── AgentLoop integrieren (statt direktem ChatClient-Call)
├── Standard-Tools registrieren (FileRead, FileWrite, Shell, Glob, Grep, WebFetch)
├── AskUserTool (5 Fragetypen, Timeout, Suspend/Resume via IUserInteraction)
├── AgentTool (Sub-Agent Delegation via IAgentPool)
├── Tool-Ergebnisse im UI anzeigen
├── Auto-Compaction aktivieren
└── Budget-Display in Status-Bar

Phase 2 (Produktiv):
├── Permission-System einbauen (interaktive Console-Prompts)
├── Session-Persistierung (JSONL auf Disk)
├── /resume, /cost, /compact Commands
├── Git-Kontext in System Prompt
└── .nexus/ Projekt-Konfiguration laden

Phase 3 (Power):
├── Skills aus .nexus/skills/ laden
├── MCP Server Connection
├── Background Tasks
└── Custom Commands aus Dateien
```

## Zusammenfassung

Die aktuelle Nexus CLI ist kein reiner **Chat-Client** mehr, sondern bereits ein nutzbarer Agent-Host mit Agent-Loop, Standard-Tools, Skills, Session-Store, gemeinsamer Command-Surface und MCP-Tool-Loading. Fuer volle Claude-Code-Naehere fehlen jetzt vor allem weitere Power-Features wie Sub-Agents, AskUser-/HITL-Ausbau, Git-Integration und Background Tasks.

Die weiteren Features (Permissions, Sessions, Cost Tracking) machen den Unterschied zwischen "Demo" und "Daily Driver".

### Step-Level Human-in-the-Loop

Die CLI unterstützt HITL nicht nur auf Tool-Ebene (Permission-Prompts), sondern auch zwischen Workflow-Steps. Wenn ein Workflow-Node `requiresApproval: true` hat, pausiert der Agent nach diesem Step und zeigt dem User den Output. Der User kann:
- **Approven** → weiter zum nächsten Step
- **Modifizieren** → Feedback/Änderungen bevor der nächste Step startet
- **Ablehnen** → Workflow wird gestoppt

### Bewusst kein Compat-Layer

Die Nexus CLI nutzt ausschließlich das `.nexus/` Konfigurationsformat. Es gibt **keine Import/Export-Kompatibilität** mit `.claude/`, GitHub Copilot oder OpenCode Configs. Nexus bietet mehr Flexibilität (Multi-Agent Workflows, Step-Level HITL, Budget, Compaction) als jedes dieser Tools — ein Compat-Layer wäre eine Reduktion auf den kleinsten gemeinsamen Nenner.
