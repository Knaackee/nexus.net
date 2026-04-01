using System.Text.Json;

namespace Nexus.Configuration;

public sealed class FileBasedSettingsStore : INexusSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly FileBasedSettingsStoreOptions _options;

    public FileBasedSettingsStore(FileBasedSettingsStoreOptions options)
    {
        _options = options;
    }

    public async Task<NexusSettings?> LoadAsync(SettingSource source, string? projectRoot = null, CancellationToken ct = default)
    {
        var path = GetPathForSource(source, projectRoot);
        if (path is null || !File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<NexusSettings>(json, SerializerOptions);
    }

    public async Task SaveAsync(NexusSettings settings, SettingSource source, string? projectRoot = null, CancellationToken ct = default)
    {
        var path = GetPathForSource(source, projectRoot)
            ?? throw new InvalidOperationException($"Setting source '{source}' is not file-backed.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    private string? GetPathForSource(SettingSource source, string? projectRoot)
        => source switch
        {
            SettingSource.Project => ResolveProjectPath(projectRoot),
            SettingSource.User => ResolveUserPath(),
            SettingSource.Managed => _options.ManagedSettingsPath,
            _ => null,
        };

    private string? ResolveProjectPath(string? projectRoot)
    {
        var root = projectRoot ?? _options.ProjectRoot;
        return root is null
            ? null
            : Path.Combine(root, _options.DirectoryName, _options.FileName);
    }

    private string ResolveUserPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.UserSettingsPath))
            return _options.UserSettingsPath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            _options.DirectoryName,
            _options.FileName);
    }
}