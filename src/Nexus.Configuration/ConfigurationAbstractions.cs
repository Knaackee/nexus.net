namespace Nexus.Configuration;

public enum SettingSource
{
    Default,
    Project,
    User,
    Managed,
    Runtime,
}

public sealed record SettingValue<T>(T? Value, SettingSource Source, bool IsManagedOverride);

public interface INexusSettingsStore
{
    Task<NexusSettings?> LoadAsync(SettingSource source, string? projectRoot = null, CancellationToken ct = default);

    Task SaveAsync(NexusSettings settings, SettingSource source, string? projectRoot = null, CancellationToken ct = default);
}

public interface INexusConfigurationProvider
{
    Task<NexusSettings> LoadAsync(string? projectRoot = null, CancellationToken ct = default);

    SettingValue<T> GetEffective<T>(string key);
}

public sealed record SettingsStoreRegistration(Type StoreType, IReadOnlyList<SettingSource> Sources);

public sealed class NexusConfigurationOptions
{
    public NexusSettings DefaultSettings { get; } = NexusSettings.CreateDefault();

    public NexusSettings RuntimeSettings { get; } = new();

    public string? ProjectRoot { get; set; }

    public IList<SettingsStoreRegistration> StoreRegistrations { get; } = [];
}

public sealed class FileBasedSettingsStoreOptions
{
    public string DirectoryName { get; set; } = ".nexus";

    public string FileName { get; set; } = "settings.json";

    public string? ProjectRoot { get; set; }

    public string? UserSettingsPath { get; set; }

    public string? ManagedSettingsPath { get; set; }
}