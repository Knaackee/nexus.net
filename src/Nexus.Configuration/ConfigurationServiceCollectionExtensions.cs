using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Configuration;

namespace Nexus.Configuration;

public sealed class ConfigurationBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}

public static class ConfigurationServiceCollectionExtensions
{
    public static NexusBuilder AddConfiguration(this NexusBuilder builder, Action<ConfigurationBuilder>? configure = null)
    {
        var configurationBuilder = new ConfigurationBuilder(builder.Services);
        configure?.Invoke(configurationBuilder);
        return builder;
    }

    public static ConfigurationBuilder UseDefaults(this ConfigurationBuilder builder)
    {
        RegisterCore(builder.Services);
        ConfigureDefaultFileStore(builder.Services);
        return builder;
    }

    public static ConfigurationBuilder UseStore<TStore>(this ConfigurationBuilder builder, params SettingSource[] sources)
        where TStore : class, INexusSettingsStore
    {
        RegisterCore(builder.Services);
        builder.Services.TryAddSingleton<TStore>();
        GetOrCreateOptions(builder.Services).StoreRegistrations.Add(new SettingsStoreRegistration(
            typeof(TStore),
            sources.Length == 0 ? [SettingSource.Project, SettingSource.User, SettingSource.Managed] : sources));
        return builder;
    }

    public static ConfigurationBuilder SetProjectRoot(this ConfigurationBuilder builder, string projectRoot)
    {
        RegisterCore(builder.Services);
        GetOrCreateOptions(builder.Services).ProjectRoot = projectRoot;
        GetOrCreateFileOptions(builder.Services).ProjectRoot = projectRoot;
        return builder;
    }

    public static ConfigurationBuilder SetUserSettingsPath(this ConfigurationBuilder builder, string userSettingsPath)
    {
        RegisterCore(builder.Services);
        GetOrCreateFileOptions(builder.Services).UserSettingsPath = userSettingsPath;
        return builder;
    }

    public static ConfigurationBuilder SetManagedSettingsPath(this ConfigurationBuilder builder, string managedSettingsPath)
    {
        RegisterCore(builder.Services);
        GetOrCreateFileOptions(builder.Services).ManagedSettingsPath = managedSettingsPath;
        return builder;
    }

    public static ConfigurationBuilder ConfigureDefaults(this ConfigurationBuilder builder, Action<NexusSettings> configure)
    {
        RegisterCore(builder.Services);
        configure(GetOrCreateOptions(builder.Services).DefaultSettings);
        return builder;
    }

    public static ConfigurationBuilder ConfigureRuntime(this ConfigurationBuilder builder, Action<NexusSettings> configure)
    {
        RegisterCore(builder.Services);
        configure(GetOrCreateOptions(builder.Services).RuntimeSettings);
        return builder;
    }

    private static void RegisterCore(IServiceCollection services)
    {
        services.TryAddSingleton(GetOrCreateOptions(services));
        services.TryAddSingleton(GetOrCreateFileOptions(services));
        services.TryAddSingleton<FileBasedSettingsStore>();
        services.TryAddSingleton<INexusConfigurationProvider, DefaultConfigurationProvider>();
    }

    private static void ConfigureDefaultFileStore(IServiceCollection services)
    {
        var options = GetOrCreateOptions(services);
        if (options.StoreRegistrations.Any(registration => registration.StoreType == typeof(FileBasedSettingsStore)))
            return;

        options.StoreRegistrations.Add(new SettingsStoreRegistration(
            typeof(FileBasedSettingsStore),
            [SettingSource.Project, SettingSource.User, SettingSource.Managed]));
    }

    private static NexusConfigurationOptions GetOrCreateOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(NexusConfigurationOptions))?.ImplementationInstance as NexusConfigurationOptions;
        if (existing is not null)
            return existing;

        var created = new NexusConfigurationOptions();
        services.AddSingleton(created);
        return created;
    }

    private static FileBasedSettingsStoreOptions GetOrCreateFileOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(FileBasedSettingsStoreOptions))?.ImplementationInstance as FileBasedSettingsStoreOptions;
        if (existing is not null)
            return existing;

        var created = new FileBasedSettingsStoreOptions();
        services.AddSingleton(created);
        return created;
    }
}