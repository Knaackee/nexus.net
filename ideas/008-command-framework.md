# 008 — Command Framework (Erweiterbare Slash-Commands)

## Priorität: 🟡 Mittel

## Status: Erledigt

- Erledigt: Neues Package `Nexus.Commands` mit `ICommand`, `CommandInvocation`, `CommandResult`, `CommandDispatchResult`, `ICommandCatalog`, `CommandRegistry`, `SlashCommandDispatcher` und Builder-/DI-Wiring
- Erledigt: Tests fuer Dispatcher- und Registry-Verhalten
- Erledigt: CLI-Migration weg vom hardcodierten `switch` hin zu `CommandRegistry` + `SlashCommandDispatcher`
- Erledigt: Datei-basierte Commands mit Markdown/YAML-Frontmatter aus `.nexus/commands/`, User-Scope und optionalen Zusatzpfaden
- Erledigt: Prompt- und Action-Commands koennen ueber Frontmatter geladen und durch den Dispatcher einheitlich verarbeitet werden
- Erledigt: Builtin-Command-Katalog auf Framework-Ebene statt nur CLI-spezifischer Commands, inklusive gemeinsamem `DelegateCommand` sowie paketdefinierten Builtins fuer `/help`, `/quit`, `/status`, `/resume`, `/cost`, `/model`, `/clear` und `/compact`

## Warum ist das sinnvoll?

**Slash-Commands sind die primäre Interaktionsschnittstelle zwischen User und Agent-System.**

Claude Code hat 60+ eingebaute Commands (`/commit`, `/review`, `/plan`, `/help`, `/memory`, `/resume`, etc.) plus ein System für custom Commands aus Dateien. Nexus' CLI hat aktuell nur 8 hardcoded Commands für Session-Management.

Ein **Command Framework als Building Block** bedeutet: Jeder Entwickler der mit Nexus eine CLI, einen Chatbot oder ein IDE-Plugin baut, bekommt ein fertiges System für:
- Eingebaute Commands
- User-definierte Commands (aus Dateien)
- Plugin-Commands
- Command-Discovery und Help-Generierung

## Was muss getan werden?

### Neues Package: `Nexus.Commands` (oder in `Nexus.Core`)

### 1. Command Abstractions

```csharp
public interface ICommand
{
    string Name { get; }
    string Description { get; }
    CommandType Type { get; }
    CommandSource Source { get; }

    /// Führt den Command aus.
    Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken ct = default);
}

public enum CommandType
{
    Action,  // Wird direkt ausgeführt (z.B. /clear, /quit)
    Prompt   // Generiert einen Prompt der an den Agent geht (z.B. /review, /commit)
}

public enum CommandSource
{
    Builtin,   // Framework-eigene Commands
    User,      // ~/.nexus/commands/
    Project,   // .nexus/commands/
    Plugin,    // Von Plugins bereitgestellt
    Custom     // Programmatisch registriert
}

public record CommandContext
{
    public required string[] Arguments { get; init; }
    public required string RawInput { get; init; }
    public IServiceProvider Services { get; init; }
    public IAgentLoop? AgentLoop { get; init; }
    public ICostTracker? CostTracker { get; init; }
    public ISessionStore? SessionStore { get; init; }
}

public record CommandResult
{
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? PromptToSend { get; init; } // Für Prompt-Commands
    public bool ShouldExit { get; init; }
}
```

### 2. Command Registry

```csharp
public interface ICommandRegistry
{
    void Register(ICommand command);
    ICommand? Get(string name);
    IReadOnlyList<ICommand> GetAll();
    IReadOnlyList<ICommand> GetBySource(CommandSource source);
}
```

### 3. File-basierte Commands (wie Claude Code Skills)

```markdown
---
name: review
description: Review code changes in current branch
type: prompt
tools: bash, file_read, glob
---

Review the code changes in the current branch. Focus on:
1. Potential bugs
2. Code style issues
3. Missing error handling
4. Security concerns

Use `git diff main...HEAD` to see the changes.
```

```csharp
/// Lädt Commands aus .nexus/commands/ und ~/.nexus/commands/
public class FileCommandLoader
{
    public IReadOnlyList<ICommand> LoadFromDirectory(string path, CommandSource source);

    // Parst Markdown mit YAML Frontmatter
    // Erstellt PromptCommand oder ActionCommand
}
```

### 4. Eingebaute Commands

```csharp
public static class BuiltinCommands
{
    public static IEnumerable<ICommand> GetAll() =>
    [
        new HelpCommand(),         // /help — Zeigt alle Commands
        new ClearCommand(),        // /clear — Löscht Konversation
        new StatusCommand(),       // /status — Zeigt Session-Status (Tokens, Kosten)
        new ResumeCommand(),       // /resume — Lädt letzte Session
        new CompactCommand(),      // /compact — Triggert manuelle Compaction
        new ModelCommand(),        // /model — Wechselt Model
        new CostCommand(),         // /cost — Zeigt Kosten-Breakdown
        new MemoryCommand(),       // /memory — Zeigt/editiert Memory
    ];
}
```

### 5. Command Dispatcher

```csharp
public interface ICommandDispatcher
{
    /// Prüft ob der Input ein Command ist (startet mit /).
    bool IsCommand(string input);

    /// Parst und dispatched einen Command.
    Task<CommandResult> DispatchAsync(string input, CancellationToken ct = default);
}
```

## Detail-Informationen

### Claude Code Command-Sources (Reihenfolge)

1. Hardcoded Commands (höchste Prio)
2. Managed Commands (Enterprise Policy)
3. Project `.claude/commands/`
4. User `~/.claude/commands/`
5. Plugin-bundled Skills
6. MCP-bereitgestellte Commands

### Warum ein Framework und nicht einfach ein Switch-Statement?

- **Erweiterbarkeit**: Plugins und User können Commands hinzufügen ohne Code zu ändern
- **Discoverability**: `/help` generiert sich automatisch aus der Registry
- **Prompt-Commands**: Commands die Text an den Agent senden (wie `/review`) brauchen ein einheitliches Pattern
- **File-basierte Commands**: Riesiger DX-Gewinn — User schreiben Markdown-Files, keine C#-Klassen

### Aufwand

- Abstractions: ~100 Zeilen
- Registry + Dispatcher: ~150 Zeilen
- FileCommandLoader (Markdown + Frontmatter): ~200 Zeilen
- Builtin Commands: ~300 Zeilen
- Tests: ~400 Zeilen
- **Gesamt: ~1.5-2 Tage**
