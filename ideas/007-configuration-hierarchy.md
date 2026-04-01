# 007 — Configuration Hierarchy

## Priorität: 🟡 Mittel

## Status

Stand: 2026-04-01

- Erledigt: `Nexus.Configuration` Package existiert
- Erledigt: `INexusSettingsStore`, `INexusConfigurationProvider`, `SettingSource`, `SettingValue<T>` und `NexusSettings`
- Erledigt: `FileBasedSettingsStore` und `DefaultConfigurationProvider` fuer Default -> Project -> User -> Managed -> Runtime Merge
- Erledigt: `AddConfiguration(...)` / `UseDefaults()` inkl. File-Store-Registrierung und Runtime-/Default-Overrides
- Erledigt: Unit-Tests fuer Merge-Prioritaet, Custom-Store-Registrierung und fehlende Dateien

## Warum ist das sinnvoll?

**Verschiedene Ebenen brauchen verschiedene Settings — und klare Überschreibungsregeln.**

Claude Code hat eine 4-stufige Settings-Hierarchie:

```
Managed (Enterprise) → User → Project → Default
     ^höchste                           niedrigste^
```

Nexus nutzt aktuell Standard-DI-Configuration (`IOptions<T>`, `appsettings.json`). Das reicht für einfache Fälle, aber sobald ein Entwickler eine Library baut die in verschiedenen Kontexten läuft (Dev, Staging, Prod, Enterprise), braucht man:

1. **Project-Level Settings**: `.nexus/settings.json` im Repo (wie `.claude/settings.json`)
2. **User-Level Settings**: `~/.nexus/settings.json` (persönliche Defaults)
3. **Managed Settings**: Enterprise-Policy die User-Settings überschreiben kann
4. **Runtime Override**: Programmatisch zur Laufzeit

## Was muss getan werden?

### In `Nexus.Core` oder neues Micro-Package

### 1. Settings Store Abstraktion (pluggable)

Nexus-Settings sollen standardmäßig aus Dateien geladen werden, aber optional auch
aus einer Datenbank, Cloud, Key Vault oder jedem anderen Backend:

```csharp
/// Pluggable Storage für Nexus-Settings. Default: File-based.
/// Kann durch DB, Cloud, Key Vault etc. ersetzt werden.
public interface INexusSettingsStore
{
    /// Lädt Settings aus einer bestimmten Quelle.
    Task<NexusSettings?> LoadAsync(SettingSource source, string? projectRoot = null, CancellationToken ct = default);

    /// Speichert Settings in eine bestimmte Quelle.
    Task SaveAsync(NexusSettings settings, SettingSource source, string? projectRoot = null, CancellationToken ct = default);
}

/// Registriert mehrere INexusSettingsStore und merged die Ergebnisse.
public interface INexusConfigurationProvider
{
    /// Lädt die gemergte Konfiguration aus allen Quellen.
    Task<NexusSettings> LoadAsync(string? projectRoot = null, CancellationToken ct = default);

    /// Holt den effektiven Wert eines Settings mit Source-Info.
    SettingValue<T> GetEffective<T>(string key);
}

public record SettingValue<T>
{
    public T Value { get; init; }
    public SettingSource Source { get; init; }
    public bool IsManagedOverride { get; init; }
}

public enum SettingSource
{
    Default,
    Project,     // .nexus/settings.json
    User,        // ~/.nexus/settings.json
    Managed,     // Enterprise Policy
    Runtime      // Programmatisch gesetzt
}
```

### 2. Default: File-based (zero config)

```csharp
/// Default-Implementation: Lädt/Speichert Settings aus JSON-Dateien.
public class FileBasedSettingsStore : INexusSettingsStore
{
    private readonly string? _projectRoot;

    public async Task<NexusSettings?> LoadAsync(SettingSource source, CancellationToken ct)
    {
        var path = source switch
        {
            SettingSource.User => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nexus", "settings.json"),
            SettingSource.Project => _projectRoot is not null
                ? Path.Combine(_projectRoot, ".nexus", "settings.json")
                : null,
            SettingSource.Managed => GetManagedSettingsPath(),
            _ => null
        };

        if (path is null || !File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<NexusSettings>(json);
    }

    public async Task SaveAsync(NexusSettings settings, SettingSource source, CancellationToken ct)
    {
        var path = GetPathForSource(source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
    }
}
```

### 3. Optionale Beispiele: DB, Cloud

```csharp
// Datenbank-Backend (z.B. für Server-Szenarien):
public class DatabaseSettingsStore : INexusSettingsStore
{
    private readonly IDbConnection _db;

    public async Task<NexusSettings?> LoadAsync(SettingSource source, CancellationToken ct)
    {
        var json = await _db.QuerySingleOrDefaultAsync<string>(
            "SELECT settings_json FROM nexus_settings WHERE source = @source",
            new { source = source.ToString() });
        return json is not null ? JsonSerializer.Deserialize<NexusSettings>(json) : null;
    }

    public async Task SaveAsync(NexusSettings settings, SettingSource source, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(settings);
        await _db.ExecuteAsync(
            "INSERT OR REPLACE INTO nexus_settings (source, settings_json) VALUES (@source, @json)",
            new { source = source.ToString(), json });
    }
}

// Registration:
services.AddNexus(b =>
{
    // Default (Dateien):
    b.AddConfiguration();

    // Oder mit DB:
    b.AddConfiguration(c => c.UseStore<DatabaseSettingsStore>());
    
    // Oder hybrid (Project aus Datei, User aus DB):
    b.AddConfiguration(c =>
    {
        c.UseStore<FileBasedSettingsStore>(SettingSource.Project);
        c.UseStore<DatabaseSettingsStore>(SettingSource.User, SettingSource.Managed);
    });
});
```

```json
// .nexus/settings.json (Project Level)
{
    "permissions": {
        "mode": "default",
        "rules": [
            { "tool": "bash", "action": "ask" },
            { "tool": "file_read", "action": "allow" }
        ]
    },
    "models": {
        "default": "claude-sonnet-4",
        "compaction": "claude-haiku-3"
    },
    "budget": {
        "maxCostUsd": 5.0,
        "maxTurns": 50
    },
    "tools": {
        "maxConcurrency": 10,
        "compactableTools": ["file_read", "bash", "glob"]
    },
    "memory": {
        "directory": ".nexus/memory",
        "maxIndexLines": 200
    }
}
```

### 5. Configuration Provider (merged alle Stores)

```csharp
public class DefaultConfigurationProvider : INexusConfigurationProvider
{
    private readonly IEnumerable<INexusSettingsStore> _stores;

    public async Task<NexusSettings> LoadAsync(string? projectRoot = null, CancellationToken ct = default)
    {
        // 1. Defaults
        var settings = NexusSettings.Default;

        // 2. Lädt aus allen Stores nach Priorität
        foreach (var source in new[] { SettingSource.Project, SettingSource.User, SettingSource.Managed })
        {
            foreach (var store in _stores)
            {
                var loaded = await store.LoadAsync(source, projectRoot, ct);
                if (loaded is not null)
                    settings = Merge(settings, loaded, source);
            }
        }

        // 3. Runtime-Overrides zuletzt
        settings = Merge(settings, _runtimeSettings, SettingSource.Runtime);

        return settings;
    }
}
```

## Detail-Informationen

### Warum nicht einfach `IConfiguration` + `appsettings.json`?

- `IConfiguration` ist flat (Key-Value) — nicht hierarchisch mit Source-Tracking
- Keine eingebaute Project-Discovery (`.nexus/` finden)
- Kein Konzept von "Managed Override" (Enterprise-Policy kann User-Setting überschreiben und locken)
- Kein Merge-Semantik für Arrays (z.B. Permission-Rules)

### Eigenes Format — keine Compat-Layer

Nexus nutzt ausschließlich `.nexus/settings.json` als Konfigurationsformat. Es gibt **keine Kompatibilität** mit `.claude/settings.json`, GitHub Copilot Instructions oder OpenCode Configs. Nexus ist flexibler als diese Tools (Multi-Agent Workflows, Step-Level HITL, Budget-Enforcement) — ein Compat-Layer wäre eine Reduktion unserer Fähigkeiten.

### Aufwand

- INexusSettingsStore + INexusConfigurationProvider: ~100 Zeilen
- FileBasedSettingsStore: ~200 Zeilen
- DefaultConfigurationProvider (Merge-Logik): ~200 Zeilen
- NexusSettings Model: ~100 Zeilen
- Tests: ~400 Zeilen
- **Gesamt: ~1.5-2 Tage**
