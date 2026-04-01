using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Configuration;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class StandardToolBuilder
{
    private readonly IServiceCollection _services;
    private readonly StandardToolOptions _options;

    internal StandardToolBuilder(IServiceCollection services, StandardToolOptions options)
    {
        _services = services;
        _options = options;
    }

    internal bool IncludeFileSystem { get; private set; } = true;
    internal bool IncludeSearch { get; private set; } = true;
    internal bool IncludeShell { get; private set; } = true;
    internal bool IncludeWeb { get; private set; } = true;
    internal bool IncludeInteraction { get; private set; } = true;
    internal bool IncludeAgents { get; private set; } = true;

    public StandardToolBuilder Configure(Action<StandardToolOptions> configure)
    {
        configure(_options);
        return this;
    }

    public StandardToolBuilder FileSystem() { IncludeFileSystem = true; return this; }
    public StandardToolBuilder Search() { IncludeSearch = true; return this; }
    public StandardToolBuilder Shell() { IncludeShell = true; return this; }
    public StandardToolBuilder Web() { IncludeWeb = true; return this; }
    public StandardToolBuilder Interaction() { IncludeInteraction = true; return this; }
    public StandardToolBuilder Agents() { IncludeAgents = true; return this; }

    public StandardToolBuilder Only(params StandardToolCategory[] categories)
    {
        IncludeFileSystem = categories.Contains(StandardToolCategory.FileSystem);
        IncludeSearch = categories.Contains(StandardToolCategory.Search);
        IncludeShell = categories.Contains(StandardToolCategory.Shell);
        IncludeWeb = categories.Contains(StandardToolCategory.Web);
        IncludeInteraction = categories.Contains(StandardToolCategory.Interaction);
        IncludeAgents = categories.Contains(StandardToolCategory.Agents);
        return this;
    }

    internal void RegisterSelectedTools()
    {
        _services.TryAddSingleton(_options);
        _services.TryAddSingleton(sp => new HttpClient { Timeout = sp.GetRequiredService<StandardToolOptions>().HttpTimeout });

        if (IncludeFileSystem)
        {
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, FileReadTool>());
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, FileWriteTool>());
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, FileEditTool>());
        }

        if (IncludeSearch)
        {
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, GlobTool>());
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, GrepTool>());
        }

        if (IncludeShell)
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, ShellTool>());

        if (IncludeWeb)
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, WebFetchTool>());

        if (IncludeInteraction)
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, AskUserTool>());

        if (IncludeAgents)
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITool, AgentTool>());

        StandardToolServiceCollectionExtensions.EnsureToolRegistryUsesDiscoveredTools(_services);
    }

    public StandardToolBuilder UseConsoleInteraction()
    {
        _services.Replace(ServiceDescriptor.Singleton<IUserInteraction, ConsoleUserInteraction>());
        return this;
    }
}

public enum StandardToolCategory
{
    FileSystem,
    Search,
    Shell,
    Web,
    Interaction,
    Agents,
}

public static class StandardToolServiceCollectionExtensions
{
    public static NexusBuilder AddStandardTools(this NexusBuilder builder, Action<StandardToolBuilder>? configure = null)
    {
        builder.AddTools(toolBuilder => toolBuilder.AddStandardTools(configure));
        return builder;
    }

    public static ToolBuilder AddStandardTools(this ToolBuilder builder, Action<StandardToolBuilder>? configure = null)
    {
        var options = GetOrCreateOptions(builder.Services);
        var standardBuilder = new StandardToolBuilder(builder.Services, options);
        configure?.Invoke(standardBuilder);
        standardBuilder.RegisterSelectedTools();
        return builder;
    }

    private static StandardToolOptions GetOrCreateOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(StandardToolOptions))?.ImplementationInstance as StandardToolOptions;
        if (existing is not null)
            return existing;

        var created = new StandardToolOptions();
        services.AddSingleton(created);
        return created;
    }

    internal static void EnsureToolRegistryUsesDiscoveredTools(IServiceCollection services)
    {
        var existing = services.LastOrDefault(service => service.ServiceType == typeof(IToolRegistry));
        if (existing is not null && existing.ImplementationType is not null && existing.ImplementationType != typeof(DefaultToolRegistry))
            return;

        services.Replace(ServiceDescriptor.Singleton<IToolRegistry>(sp =>
        {
            var registry = new DefaultToolRegistry();
            foreach (var tool in sp.GetServices<ITool>())
                registry.Register(tool);

            return registry;
        }));
    }
}