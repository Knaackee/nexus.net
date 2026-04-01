using Microsoft.Extensions.DependencyInjection;

namespace Nexus.Configuration;

public sealed class DefaultConfigurationProvider : INexusConfigurationProvider
{
    private static readonly SettingSource[] MergeOrder =
    [
        SettingSource.Project,
        SettingSource.User,
        SettingSource.Managed,
    ];

    private readonly IServiceProvider _services;
    private readonly NexusConfigurationOptions _options;
    private readonly object _sync = new();

    private NexusSettings _lastLoaded = NexusSettings.CreateDefault();
    private Dictionary<string, SettingSource> _effectiveSources = new(StringComparer.OrdinalIgnoreCase);

    public DefaultConfigurationProvider(IServiceProvider services, NexusConfigurationOptions options)
    {
        _services = services;
        _options = options;

        (_lastLoaded, _effectiveSources) = CreateSnapshot(options.DefaultSettings, options.RuntimeSettings);
    }

    public async Task<NexusSettings> LoadAsync(string? projectRoot = null, CancellationToken ct = default)
    {
        var settings = Clone(_options.DefaultSettings);
        var effectiveSources = InitializeEffectiveSources(settings, SettingSource.Default);

        foreach (var source in MergeOrder)
        {
            foreach (var registration in _options.StoreRegistrations.Where(r => r.Sources.Contains(source)))
            {
                var store = (INexusSettingsStore)_services.GetRequiredService(registration.StoreType);
                var loaded = await store.LoadAsync(source, projectRoot ?? _options.ProjectRoot, ct).ConfigureAwait(false);
                if (loaded is null)
                    continue;

                settings = NexusSettingsMerge.Merge(settings, loaded, source, effectiveSources);
            }
        }

        settings = NexusSettingsMerge.Merge(settings, _options.RuntimeSettings, SettingSource.Runtime, effectiveSources);

        lock (_sync)
        {
            _lastLoaded = settings;
            _effectiveSources = effectiveSources;
        }

        return settings;
    }

    public SettingValue<T> GetEffective<T>(string key)
    {
        object? value;
        SettingSource source;

        lock (_sync)
        {
            if (!NexusSettingsPathAccessor.TryGetValue(_lastLoaded, key, out value))
                throw new KeyNotFoundException($"Unknown Nexus setting '{key}'.");

            source = _effectiveSources.TryGetValue(key, out var resolved)
                ? resolved
                : SettingSource.Default;
        }

        if (value is null)
            return new SettingValue<T>(default, source, source == SettingSource.Managed);

        if (value is T typed)
            return new SettingValue<T>(typed, source, source == SettingSource.Managed);

        var converted = (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        return new SettingValue<T>(converted, source, source == SettingSource.Managed);
    }

    private static (NexusSettings Settings, Dictionary<string, SettingSource> Sources) CreateSnapshot(
        NexusSettings defaultSettings,
        NexusSettings runtimeSettings)
    {
        var settings = Clone(defaultSettings);
        var sources = InitializeEffectiveSources(settings, SettingSource.Default);
        settings = NexusSettingsMerge.Merge(settings, runtimeSettings, SettingSource.Runtime, sources);
        return (settings, sources);
    }

    private static Dictionary<string, SettingSource> InitializeEffectiveSources(NexusSettings settings, SettingSource source)
    {
        var effectiveSources = new Dictionary<string, SettingSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in NexusSettingsPathAccessor.GetKnownPaths())
        {
            if (NexusSettingsPathAccessor.TryGetValue(settings, path, out var value) && value is not null)
                effectiveSources[path] = source;
        }

        return effectiveSources;
    }

    private static NexusSettings Clone(NexusSettings settings)
        => new()
        {
            Permissions = settings.Permissions with { Rules = settings.Permissions.Rules.ToArray() },
            Models = settings.Models with { },
            Budget = settings.Budget with { },
            Tools = settings.Tools with { CompactableTools = settings.Tools.CompactableTools.ToArray() },
            Memory = settings.Memory with { },
        };
}

internal static class NexusSettingsMerge
{
    public static NexusSettings Merge(
        NexusSettings baseline,
        NexusSettings incoming,
        SettingSource source,
        IDictionary<string, SettingSource> effectiveSources)
    {
        var permissions = baseline.Permissions;
        if (!string.IsNullOrWhiteSpace(incoming.Permissions.Mode))
        {
            permissions = permissions with { Mode = incoming.Permissions.Mode };
            effectiveSources["permissions.mode"] = source;
        }

        if (incoming.Permissions.Rules.Count > 0)
        {
            permissions = permissions with { Rules = incoming.Permissions.Rules.ToArray() };
            effectiveSources["permissions.rules"] = source;
        }

        var models = baseline.Models;
        if (!string.IsNullOrWhiteSpace(incoming.Models.Default))
        {
            models = models with { Default = incoming.Models.Default };
            effectiveSources["models.default"] = source;
        }

        if (!string.IsNullOrWhiteSpace(incoming.Models.Compaction))
        {
            models = models with { Compaction = incoming.Models.Compaction };
            effectiveSources["models.compaction"] = source;
        }

        var budget = baseline.Budget;
        if (incoming.Budget.MaxCostUsd.HasValue)
        {
            budget = budget with { MaxCostUsd = incoming.Budget.MaxCostUsd };
            effectiveSources["budget.maxCostUsd"] = source;
        }

        if (incoming.Budget.MaxTurns.HasValue)
        {
            budget = budget with { MaxTurns = incoming.Budget.MaxTurns };
            effectiveSources["budget.maxTurns"] = source;
        }

        var tools = baseline.Tools;
        if (incoming.Tools.MaxConcurrency.HasValue)
        {
            tools = tools with { MaxConcurrency = incoming.Tools.MaxConcurrency };
            effectiveSources["tools.maxConcurrency"] = source;
        }

        if (incoming.Tools.CompactableTools.Count > 0)
        {
            tools = tools with { CompactableTools = incoming.Tools.CompactableTools.ToArray() };
            effectiveSources["tools.compactableTools"] = source;
        }

        var memory = baseline.Memory;
        if (!string.IsNullOrWhiteSpace(incoming.Memory.Directory))
        {
            memory = memory with { Directory = incoming.Memory.Directory };
            effectiveSources["memory.directory"] = source;
        }

        if (incoming.Memory.MaxIndexLines.HasValue)
        {
            memory = memory with { MaxIndexLines = incoming.Memory.MaxIndexLines };
            effectiveSources["memory.maxIndexLines"] = source;
        }

        return baseline with
        {
            Permissions = permissions,
            Models = models,
            Budget = budget,
            Tools = tools,
            Memory = memory,
        };
    }
}

internal static class NexusSettingsPathAccessor
{
    private static readonly string[] KnownPaths =
    [
        "permissions.mode",
        "permissions.rules",
        "models.default",
        "models.compaction",
        "budget.maxCostUsd",
        "budget.maxTurns",
        "tools.maxConcurrency",
        "tools.compactableTools",
        "memory.directory",
        "memory.maxIndexLines",
    ];

    public static IEnumerable<string> GetKnownPaths() => KnownPaths;

    public static bool TryGetValue(NexusSettings settings, string key, out object? value)
    {
        switch (key)
        {
            case "permissions.mode":
                value = settings.Permissions.Mode;
                return true;
            case "permissions.rules":
                value = settings.Permissions.Rules;
                return true;
            case "models.default":
                value = settings.Models.Default;
                return true;
            case "models.compaction":
                value = settings.Models.Compaction;
                return true;
            case "budget.maxCostUsd":
                value = settings.Budget.MaxCostUsd;
                return true;
            case "budget.maxTurns":
                value = settings.Budget.MaxTurns;
                return true;
            case "tools.maxConcurrency":
                value = settings.Tools.MaxConcurrency;
                return true;
            case "tools.compactableTools":
                value = settings.Tools.CompactableTools;
                return true;
            case "memory.directory":
                value = settings.Memory.Directory;
                return true;
            case "memory.maxIndexLines":
                value = settings.Memory.MaxIndexLines;
                return true;
            default:
                value = null;
                return false;
        }
    }
}