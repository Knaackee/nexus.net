# 009 — Plugin Architecture

## Priorität: 🟡 Mittel

## Warum ist das sinnvoll?

**Ein Plugin-System macht Nexus erweiterbar ohne dass die Kern-Library wachsen muss.**

Claude Code hat ein Plugin-System das:
- Tools hinzufügt (via MCP Server)
- Commands/Skills bereitstellt
- Hooks für Custom-Logik injected
- Built-in Plugins und Marketplace-Plugins unterscheidet

Für Nexus bedeutet das: Statt jedes Feature in die Library zu bauen, können Community und Enterprise eigene Erweiterungen als Plugins paketieren.

**Beispiele für Nexus-Plugins:**
- `Nexus.Plugin.GitHub` — PR-Review, Issue-Tracker Integration
- `Nexus.Plugin.Azure` — Azure DevOps Integration
- `Nexus.Plugin.Docker` — Container-Management Tools
- `Nexus.Plugin.Database` — SQL Query Tools mit Safety Checks
- Community Plugins via NuGet

## Was muss getan werden?

### Neues Package: `Nexus.Plugins`

### 1. Plugin Abstractions

```csharp
public interface INexusPlugin
{
    string Name { get; }
    string Description { get; }
    string Version { get; }

    /// Registriert die Plugin-Services im DI Container.
    void Configure(INexusPluginBuilder builder);
}

public interface INexusPluginBuilder
{
    /// Registriert Tools die das Plugin bereitstellt.
    INexusPluginBuilder AddTool(ITool tool);
    INexusPluginBuilder AddTools(IEnumerable<ITool> tools);

    /// Registriert Commands die das Plugin bereitstellt.
    INexusPluginBuilder AddCommand(ICommand command);

    /// Registriert Middleware.
    INexusPluginBuilder AddAgentMiddleware<T>() where T : class, IAgentMiddleware;
    INexusPluginBuilder AddToolMiddleware<T>() where T : class, IToolMiddleware;

    /// Registriert MCP Server die das Plugin mitbringt.
    INexusPluginBuilder AddMcpServer(McpServerConfig config);

    /// Registriert beliebige Services.
    INexusPluginBuilder AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;
}
```

### 2. Plugin Discovery & Loading

```csharp
public interface IPluginLoader
{
    /// Entdeckt und lädt Plugins aus verschiedenen Quellen.
    IReadOnlyList<PluginInfo> DiscoverPlugins(PluginDiscoveryOptions options);

    /// Aktiviert ein Plugin.
    void Enable(PluginInfo plugin, INexusPluginBuilder builder);

    /// Deaktiviert ein Plugin.
    void Disable(string pluginName);
}

public record PluginDiscoveryOptions
{
    /// Verzeichnis für projektlokale Plugins.
    public string? ProjectPluginDir { get; init; }  // .nexus/plugins/

    /// Verzeichnis für User-Plugins.
    public string? UserPluginDir { get; init; }      // ~/.nexus/plugins/

    /// Ob NuGet-basierte Plugins geladen werden sollen.
    public bool LoadNuGetPlugins { get; init; } = true;
}
```

### 3. NuGet-basierte Plugins

```csharp
// Plugin-Autor erstellt ein NuGet Package:
public class GitHubPlugin : INexusPlugin
{
    public string Name => "Nexus.Plugin.GitHub";
    public string Description => "GitHub integration tools";
    public string Version => "1.0.0";

    public void Configure(INexusPluginBuilder builder)
    {
        builder
            .AddTool(new CreatePullRequestTool())
            .AddTool(new ReviewPullRequestTool())
            .AddTool(new ListIssuesTool())
            .AddCommand(new PrCommand())      // /pr
            .AddCommand(new ReviewCommand());  // /review
    }
}

// User registriert:
builder.AddPlugin<GitHubPlugin>();

// Oder automatisch via Discovery:
builder.AddPlugins(options =>
{
    options.ProjectPluginDir = ".nexus/plugins";
    options.LoadNuGetPlugins = true;
});
```

### 4. Plugin Lifecycle Management

```csharp
public interface IPluginManager
{
    IReadOnlyList<PluginInfo> InstalledPlugins { get; }
    IReadOnlyList<PluginInfo> EnabledPlugins { get; }

    Task EnableAsync(string pluginName, CancellationToken ct = default);
    Task DisableAsync(string pluginName, CancellationToken ct = default);

    /// Persistiert Plugin-Status (enabled/disabled) in Settings.
    Task SaveStateAsync(CancellationToken ct = default);
}
```

## Detail-Informationen

### Wie Claude Code Plugins funktionieren

- **Built-in Plugins**: Hardcoded im Binary, aber enable/disable über Settings
- **Marketplace Plugins**: Über Package-Name referenziert, automatisch geladen
- **Plugin liefert**: Skills (Commands), Hooks, MCP Server Definitions
- **Hooks Config**: Plugins können Pre/Post Hooks definieren die bei Tool-Ausführung laufen
- **Persistence**: Enabled/Disabled Status wird in User-Settings gespeichert

### Design-Entscheidungen für Nexus

- **NuGet als Plugin-Format**: Bestehendes .NET Ökosystem nutzen statt eigenes Plugin-Format
- **Interface-basiert**: Plugins implementieren `INexusPlugin` — kein Reflection-Magic
- **DI-Integration**: Plugins registrieren sich über den gleichen DI-Container wie alles andere
- **Kein Plugin-Isolation**: Plugins laufen im gleichen AppDomain — Einfachheit geht vor Isolation

### Aufwand

- Abstractions: ~100 Zeilen
- PluginBuilder: ~150 Zeilen
- PluginLoader (NuGet Discovery): ~250 Zeilen
- PluginManager: ~150 Zeilen
- Tests: ~300 Zeilen
- **Gesamt: ~1.5-2 Tage**
