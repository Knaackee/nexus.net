# 010 — Skills System (Custom Prompt Templates)

## Priorität: 🟡 Mittel

## Status: Erledigt

- Erledigt: Neues Package `Nexus.Skills` mit `SkillDefinition`, `ISkillCatalog`, `SkillCatalog` und Builder-/DI-Wiring
- Erledigt: Skill-basierte `AgentDefinition`-Komposition fuer Prompt-, Model- und Tool-Profile
- Erledigt: CLI nutzt bereits einen programmatischen Skill-Katalog fuer unterschiedliche Agent-Profile
- Erledigt: Datei-basierte Skills aus `.nexus/skills/`, User-Scope und optionalen Zusatzpfaden per Markdown + Frontmatter
- Erledigt: Offizieller `.nexus`-Pfad bleibt Standard; `.claude/skills` ist nur explizit per Opt-in in der CLI nutzbar
- Erledigt: automatische Relevanzermittlung ueber `ISkillCatalog.FindRelevant(...)` und Middleware-basierte Skill-Injection ueber `SkillInjectionMiddleware`

## Warum ist das sinnvoll?

**Skills sind wiederverwendbare Prompt-Bausteine die die Qualität von Agent-Outputs dramatisch verbessern.**

Claude Code hat ein Skills-System bei dem Entwickler Markdown-Dateien mit YAML-Frontmatter anlegen können (`~/.claude/skills/` oder `.claude/skills/`). Diese werden dem Agent als zusätzlicher Kontext injected. Das ist extrem mächtig weil:

1. **Domain-Wissen wird portabel**: Statt in jedem Prompt zu erklären wie das Projekt aufgebaut ist → Skill-Datei
2. **Best Practices werden kodifiziert**: Team-Conventions als Skills statt als Wiki-Seiten
3. **Automatic Injection**: Skills können basierend auf `whenToUse` automatisch aktiviert werden
4. **Tool-Scoping**: Skills definieren welche Tools sie brauchen

Nexus hat aktuell **kein Konzept** von wiederverwendbaren Prompt-Templates oder Skills.

## Was muss getan werden?

### In `Nexus.Core` oder neues Micro-Package `Nexus.Skills`

### 1. Skill Definition

```csharp
public record Skill
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Content { get; init; }      // Markdown-Inhalt
    public SkillSource Source { get; init; }

    // Frontmatter-Werte:
    public IReadOnlyList<string>? AllowedTools { get; init; }     // Tools die dieser Skill nutzt
    public string? WhenToUse { get; init; }                        // Beschreibung wann der Skill passt
    public string? ModelOverride { get; init; }                    // Optionales Model-Override
    public SkillPriority Priority { get; init; } = SkillPriority.Medium;
}

public enum SkillSource { Project, User, Plugin, Managed, Inline }
public enum SkillPriority { High, Medium, Low }
```

### 2. Skill Registry & Loader

```csharp
public interface ISkillRegistry
{
    /// Alle geladenen Skills.
    IReadOnlyList<Skill> Skills { get; }

    /// Registriert einen Skill programmatisch.
    void Register(Skill skill);

    /// Findet relevante Skills basierend auf Kontext.
    IReadOnlyList<Skill> FindRelevant(string userMessage, string? currentFile = null);

    /// Generiert den System-Prompt-Abschnitt für aktive Skills.
    string BuildSkillsPromptSection(IReadOnlyList<Skill> activeSkills);
}

public interface ISkillLoader
{
    /// Lädt Skills aus einem Verzeichnis.
    IReadOnlyList<Skill> LoadFromDirectory(string path, SkillSource source);
}
```

### 3. Skill-Datei Format

```markdown
---
name: csharp-conventions
description: C# coding conventions for this project
whenToUse: When writing or reviewing C# code
tools:
  - file_read
  - file_edit
  - bash
priority: high
---

# C# Conventions

## Naming
- Use PascalCase for public members
- Use camelCase with _ prefix for private fields
- Use meaningful names, no abbreviations

## Error Handling
- Use Result<T> pattern instead of exceptions for business logic
- Use exceptions only for truly exceptional cases
- Always log at the appropriate level

## Testing
- One test class per production class
- Use Arrange/Act/Assert pattern
- Use NSubstitute for mocking
```

### 4. Automatic Skill Injection

```csharp
// Skills werden automatisch in den System Prompt injected:
public class SkillInjectionMiddleware : IAgentMiddleware
{
    private readonly ISkillRegistry _skills;

    public async Task<AgentResult> InvokeAsync(
        AgentTask task, IAgentContext ctx,
        AgentExecutionDelegate next, CancellationToken ct)
    {
        // Relevante Skills für diesen Task finden
        var relevant = _skills.FindRelevant(task.Description);

        if (relevant.Count > 0)
        {
            // Skills in System Prompt injected
            var skillsSection = _skills.BuildSkillsPromptSection(relevant);
            ctx.AppendToSystemPrompt(skillsSection);
        }

        return await next(task, ctx, ct);
    }
}
```

### 5. Discovery-Pfade

```
Reihenfolge (höchste Priorität zuerst):
1. Managed Skills   → Enterprise Policy bereitgestellt
2. Project Skills   → .nexus/skills/
3. User Skills      → ~/.nexus/skills/
4. Plugin Skills    → Via Plugin-System
5. Inline Skills    → Programmatisch registriert
```

## Detail-Informationen

### Wie Claude Code Skills funktionieren

- **Dateiformat**: Markdown mit YAML Frontmatter
- **Frontmatter-Keys**: `tools`, `effort`, `modelOverride`, `whenToUse`, `allowedTools`
- **Token-Budget**: Skills werden auf ~5K Tokens pro Skill begrenzt
- **Post-Compact Restore**: Nach einer Compaction werden bis zu 5 relevante Skills re-injected
- **Slash-Command Integration**: Jeder Skill wird als `/skill_name` Command verfügbar
- **Automatic vs Manual**: Skills mit `whenToUse` können automatisch aktiviert werden; andere manuell via Command

### Beispiele für nützliche Skills

```
.nexus/skills/
├── architecture.md        # Projekt-Architektur beschreiben
├── testing.md             # Test-Conventions
├── api-design.md          # API-Design Guidelines
├── error-handling.md      # Error Handling Patterns
├── database.md            # DB Schema & Query-Conventions
├── deployment.md          # Deployment-Prozess
└── security.md            # Security-Requirements
```

### Aufwand

- Skill Model + Registry: ~150 Zeilen
- SkillLoader (Markdown + Frontmatter Parsing): ~200 Zeilen
- SkillInjectionMiddleware: ~100 Zeilen
- FindRelevant (Keyword-Matching): ~100 Zeilen
- Tests: ~300 Zeilen
- **Gesamt: ~1-1.5 Tage**
