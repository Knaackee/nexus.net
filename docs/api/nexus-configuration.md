# Nexus.Configuration API Reference

`Nexus.Configuration` provides layered settings loading for Nexus applications.

Use it when settings need a predictable precedence model across shipped defaults, project files, user files, managed overrides, and runtime overrides.

## Layer Model

`SettingSource` defines the precedence tiers:

- `Default`
- `Project`
- `User`
- `Managed`
- `Runtime`

## Key Types

### `INexusSettingsStore`

Storage abstraction for loading and saving settings per source.

```csharp
public interface INexusSettingsStore
{
    Task<NexusSettings?> LoadAsync(SettingSource source, string? projectRoot = null, CancellationToken ct = default);
    Task SaveAsync(NexusSettings settings, SettingSource source, string? projectRoot = null, CancellationToken ct = default);
}
```

### `INexusConfigurationProvider`

Loads the merged effective settings and exposes typed effective values.

### `SettingValue<T>`

Returns the resolved value plus its source and whether a managed override was involved.

### `NexusConfigurationOptions`

Holds default settings, runtime settings, project root, and registered stores.

### `FileBasedSettingsStoreOptions`

Controls `.nexus/settings.json` layout and user or managed settings paths.

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddConfiguration(configuration =>
    {
        configuration.UseDefaults();
    });
});
```

## Typical Use

Use this package when:

- project and user settings should merge predictably
- host configuration should stay outside ad hoc JSON parsing
- CLI, local apps, and managed environments need one shared precedence model

## Related Packages

- `Nexus.Defaults`
- `Nexus.Commands`
- `Nexus.Skills`

## Related Docs

- [Nexus CLI](../examples/nexus-cli.md)