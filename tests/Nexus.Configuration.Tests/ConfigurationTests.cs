using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Configuration;
using Nexus.Core.Configuration;

namespace Nexus.Configuration.Tests;

public sealed class ConfigurationProviderTests
{
    [Fact]
    public async Task LoadAsync_Merges_Project_User_Managed_And_Runtime_In_Order()
    {
        var root = CreateTempDirectory();
        var projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".nexus"));

        var userPath = Path.Combine(root, "user-settings.json");
        var managedPath = Path.Combine(root, "managed-settings.json");

        await File.WriteAllTextAsync(Path.Combine(projectRoot, ".nexus", "settings.json"), JsonSerializer.Serialize(new NexusSettings
        {
            Budget = new BudgetSettings { MaxTurns = 10 },
            Models = new ModelSettings { Default = "project-model" },
        }, CreateJsonOptions()));
        await File.WriteAllTextAsync(userPath, JsonSerializer.Serialize(new NexusSettings
        {
            Budget = new BudgetSettings { MaxTurns = 20 },
            Memory = new MemorySettings { Directory = ".nexus/user-memory" },
        }, CreateJsonOptions()));
        await File.WriteAllTextAsync(managedPath, JsonSerializer.Serialize(new NexusSettings
        {
            Budget = new BudgetSettings { MaxTurns = 30 },
            Models = new ModelSettings { Default = "managed-model" },
        }, CreateJsonOptions()));

        var services = new ServiceCollection();
        services.AddNexus(builder => builder.AddConfiguration(configuration =>
        {
            configuration.UseDefaults();
            configuration.SetProjectRoot(projectRoot);
            configuration.SetUserSettingsPath(userPath);
            configuration.SetManagedSettingsPath(managedPath);
            configuration.ConfigureRuntime(settings =>
            {
                settings.Tools = new ToolSettings { MaxConcurrency = 9 };
            });
        }));

        await using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<INexusConfigurationProvider>();

        var settings = await provider.LoadAsync();

        settings.Budget.MaxTurns.Should().Be(30);
        settings.Models.Default.Should().Be("managed-model");
        settings.Memory.Directory.Should().Be(".nexus/user-memory");
        settings.Tools.MaxConcurrency.Should().Be(9);

        provider.GetEffective<int?>("budget.maxTurns").Should().Be(new SettingValue<int?>(30, SettingSource.Managed, true));
        provider.GetEffective<string>("memory.directory").Should().Be(new SettingValue<string>(".nexus/user-memory", SettingSource.User, false));
        provider.GetEffective<int?>("tools.maxConcurrency").Should().Be(new SettingValue<int?>(9, SettingSource.Runtime, false));
    }

    [Fact]
    public async Task LoadAsync_Uses_Custom_Store_Registration()
    {
        var stubStore = new StubSettingsStore();
        var services = new ServiceCollection();
        services.AddSingleton(stubStore);
        services.AddNexus(builder => builder.AddConfiguration(configuration =>
        {
            configuration.UseStore<StubSettingsStore>(SettingSource.Project);
        }));

        await using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<INexusConfigurationProvider>();

        var settings = await provider.LoadAsync();

        stubStore.LoadCalls.Should().ContainSingle(call => call == SettingSource.Project);
        settings.Models.Default.Should().Be("stub-model");
    }

    [Fact]
    public async Task LoadAsync_Missing_Files_Returns_Default_Settings()
    {
        var root = CreateTempDirectory();
        var services = new ServiceCollection();
        services.AddNexus(builder => builder.AddConfiguration(configuration =>
        {
            configuration.UseDefaults();
            configuration.SetProjectRoot(root);
            configuration.SetUserSettingsPath(Path.Combine(root, "missing-user.json"));
            configuration.SetManagedSettingsPath(Path.Combine(root, "missing-managed.json"));
        }));

        await using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<INexusConfigurationProvider>();

        var settings = await provider.LoadAsync();

        settings.Budget.MaxTurns.Should().Be(50);
        settings.Memory.MaxIndexLines.Should().Be(200);
    }

    private static JsonSerializerOptions CreateJsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexus-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubSettingsStore : INexusSettingsStore
    {
        public List<SettingSource> LoadCalls { get; } = [];

        public Task<NexusSettings?> LoadAsync(SettingSource source, string? projectRoot = null, CancellationToken ct = default)
        {
            LoadCalls.Add(source);
            return Task.FromResult<NexusSettings?>(new NexusSettings
            {
                Models = new ModelSettings { Default = "stub-model" },
            });
        }

        public Task SaveAsync(NexusSettings settings, SettingSource source, string? projectRoot = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}