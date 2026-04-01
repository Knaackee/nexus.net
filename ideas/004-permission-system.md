# 004 — Permission System für Tool-Zugriffskontrolle

## Priorität: 🟠 Hoch

## Warum ist das sinnvoll?

**Ohne Permission-System kann kein Agent sicher in Produktion laufen.**

Sobald ein Agent Tools hat, die das Filesystem ändern, Shell-Commands ausführen oder Netzwerkzugriff haben, braucht man ein Permission-System. Aktuell hat Nexus **keinerlei Konzept** dafür.

Claude Code hat ein ausgereiftes Permission-Modell:
- **Rule-based**: Konfigurierbare Allow/Deny/Ask-Regeln pro Tool
- **Modale**: Default (fragen), Plan (nur planen), AcceptEdits (Edits auto-akzeptieren), Bypass (alles erlauben)
- **Hierarchisch**: Managed (Enterprise) > User > Project Settings
- **Laufzeit**: Permission-Check passiert unmittelbar vor Tool-Ausführung, nicht bei der Registrierung

**Für eine Library wie Nexus ist ein pluggable Permission System essenziell**, weil verschiedene Anwendungen verschiedene Vertrauensmodelle haben:
- CLI: User fragen
- Server: Policy-basiert
- Tests: Alles erlauben
- Enterprise: Managed Rules

## Was muss getan werden?

### Erweiterung von `Nexus.Core`

### 1. Permission Abstractions

```csharp
public interface IToolPermissionHandler
{
    /// Prüft ob ein Tool-Call erlaubt ist. Wird direkt vor Execution aufgerufen.
    Task<PermissionDecision> CheckPermissionAsync(
        ToolPermissionRequest request,
        CancellationToken ct = default);
}

public record ToolPermissionRequest
{
    public required string ToolName { get; init; }
    public required object? Input { get; init; }
    public required AgentId AgentId { get; init; }
    public string? ToolUseId { get; init; }
    public IDictionary<string, object>? Metadata { get; init; }
}

public abstract record PermissionDecision
{
    public required string ToolName { get; init; }
}

public record PermissionGranted(string ToolName) : PermissionDecision
{
    /// Optional: Modifizierter Input (z.B. Pfad-Einschränkung).
    public object? ModifiedInput { get; init; }
}

public record PermissionDenied(string ToolName, string Reason) : PermissionDecision;

public record PermissionAsk(string ToolName, string Question, string[] Options) : PermissionDecision
{
    /// Callback das aufgerufen wird wenn der User antwortet.
    public Func<string, Task<PermissionDecision>>? OnResponse { get; init; }
}
```

### 2. Permission Rules Engine

```csharp
public record ToolPermissionRule
{
    /// Tool-Name Pattern (supports wildcards: "file_*", "bash", "*").
    public required string ToolPattern { get; init; }

    /// Aktion: Allow, Deny, Ask.
    public required PermissionAction Action { get; init; }

    /// Optionale Bedingung (z.B. nur für bestimmte Pfade).
    public Func<ToolPermissionRequest, bool>? Condition { get; init; }

    /// Quelle der Regel (für Priorisierung).
    public PermissionRuleSource Source { get; init; } = PermissionRuleSource.User;
}

public enum PermissionAction { Allow, Deny, Ask }

public enum PermissionRuleSource
{
    Managed = 0,  // Enterprise Policy (höchste Prio)
    User = 1,     // User Settings
    Project = 2,  // Project Settings
    Default = 3   // Framework Default
}

public enum PermissionMode
{
    Default,        // Jeder Tool-Call wird gemäß Regeln geprüft
    Plan,           // Nur zeigen was getan wird, nicht ausführen
    AcceptEdits,    // Datei-Edits automatisch akzeptieren
    Bypass,         // Alles erlauben (für Development/Tests)
}
```

### 3. Rule-Based Permission Handler

```csharp
public class RuleBasedPermissionHandler : IToolPermissionHandler
{
    private readonly IReadOnlyList<ToolPermissionRule> _rules;
    private readonly PermissionMode _mode;
    private readonly Func<PermissionAsk, Task<string>>? _askUser;

    public async Task<PermissionDecision> CheckPermissionAsync(
        ToolPermissionRequest request, CancellationToken ct)
    {
        // 1. Bypass-Mode: Alles erlauben
        if (_mode == PermissionMode.Bypass)
            return new PermissionGranted(request.ToolName);

        // 2. Rules nach Source-Priorität sortiert durchgehen
        foreach (var rule in _rules.OrderBy(r => r.Source))
        {
            if (!MatchesPattern(rule.ToolPattern, request.ToolName))
                continue;
            if (rule.Condition is not null && !rule.Condition(request))
                continue;

            return rule.Action switch
            {
                PermissionAction.Allow => new PermissionGranted(request.ToolName),
                PermissionAction.Deny => new PermissionDenied(request.ToolName,
                    $"Denied by {rule.Source} rule"),
                PermissionAction.Ask => await AskUserAsync(request),
                _ => throw new InvalidOperationException()
            };
        }

        // 3. Default: Ask
        return await AskUserAsync(request);
    }
}
```

### 4. Eingebaute Permission Presets

```csharp
public static class PermissionPresets
{
    /// Nur lesende Tools erlaubt, schreibende fragen.
    public static IReadOnlyList<ToolPermissionRule> ReadOnly => [
        new() { ToolPattern = "file_read", Action = PermissionAction.Allow, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "glob", Action = PermissionAction.Allow, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "web_search", Action = PermissionAction.Allow, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "*", Action = PermissionAction.Deny, Source = PermissionRuleSource.Default },
    ];

    /// Alles erlaubt (für Development).
    public static IReadOnlyList<ToolPermissionRule> AllowAll => [
        new() { ToolPattern = "*", Action = PermissionAction.Allow, Source = PermissionRuleSource.Default },
    ];

    /// Standard interaktiv: Lesen ok, Schreiben fragen.
    public static IReadOnlyList<ToolPermissionRule> Interactive => [
        new() { ToolPattern = "file_read", Action = PermissionAction.Allow, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "glob", Action = PermissionAction.Allow, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "file_write", Action = PermissionAction.Ask, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "file_edit", Action = PermissionAction.Ask, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "bash", Action = PermissionAction.Ask, Source = PermissionRuleSource.Default },
        new() { ToolPattern = "*", Action = PermissionAction.Ask, Source = PermissionRuleSource.Default },
    ];
}
```

### 5. Integration mit AgentLoop & ToolExecutor

```csharp
// Im ToolExecutor:
public async IAsyncEnumerable<ToolExecutionEvent> ExecuteAsync(...)
{
    foreach (var call in toolCalls)
    {
        // Permission Check VOR Ausführung
        if (context.Permissions is not null)
        {
            var decision = await context.Permissions.CheckPermissionAsync(
                new ToolPermissionRequest
                {
                    ToolName = call.ToolName,
                    Input = call.Input,
                    AgentId = context.AgentId,
                    ToolUseId = call.ToolUseId
                }, ct);

            if (decision is PermissionDenied denied)
            {
                yield return new ToolPermissionDeniedEvent(call.ToolUseId, call.ToolName, denied.Reason);
                continue; // Tool nicht ausführen, Denial als Result zurückgeben
            }

            if (decision is PermissionGranted granted && granted.ModifiedInput is not null)
            {
                call = call with { Input = granted.ModifiedInput };
            }
        }

        // Tool ausführen...
    }
}
```

### 6. DX: Registration

```csharp
builder.AddPermissions(permissions =>
{
    permissions.Mode = PermissionMode.Default;
    permissions.Rules.AddRange(PermissionPresets.Interactive);

    // Custom Rules:
    permissions.Rules.Add(new ToolPermissionRule
    {
        ToolPattern = "bash",
        Action = PermissionAction.Allow,
        Condition = req => req.Input?.ToString()?.StartsWith("git") == true // git-Befehle erlauben
    });
});
```

## Detail-Informationen

### Claude Code Permission-Architektur

1. **3 Regelquellen**: `policySettings` (Managed/Enterprise), `userSettings` (lokal), `projectSettings` (Git-Repo)
2. **Managed Override**: Wenn `allowManagedPermissionRulesOnly=true` → nur Policy-Regeln gelten
3. **Denial Tracking**: Zählt konsekutive Denials pro Tool — nach N Denials wird das Tool deaktiviert (verhindert Permission-Spam)
4. **ML Classifier**: Optional kann ein ML-Model entscheiden ob ein Tool-Call sicher ist (statt User zu fragen)
5. **IDE Integration**: Permission-Anfragen werden an die IDE weitergeleitet und dort in nativer UI angezeigt

### Warum das für Nexus wichtig ist

1. **Library-User bauen unterschiedliche Produkte**: CLI braucht interaktive Permissions, ein Server braucht Policy-Rules, Tests brauchen Bypass
2. **Enterprise-Adoption**: Ohne Permission-System kein Enterprise-Einsatz
3. **Tool-Ökosystem**: Wenn MCP-Tools von externen Servern kommen → MUSS man deren Zugriff kontrollieren können
4. **Vertrauen**: User vertrauen Agents mehr wenn sie sehen dass gefährliche Operationen abgesichert sind

### Aufwand

- Abstractions (IToolPermissionHandler, Rules): ~150 Zeilen
- RuleBasedPermissionHandler: ~200 Zeilen
- Presets: ~50 Zeilen
- Integration mit ToolExecutor: ~100 Zeilen
- Tests: ~400 Zeilen
- **Gesamt: ~1.5-2 Tage**
