using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;

namespace Nexus.Permissions;

public static class PermissionServiceCollectionExtensions
{
    public static PermissionBuilder Configure(this PermissionBuilder builder, Action<PermissionOptions> configure)
    {
        var options = GetOrCreateOptions(builder.Services);
        configure(options);
        RegisterCore(builder.Services);
        return builder;
    }

    public static PermissionBuilder UsePreset(this PermissionBuilder builder, PermissionPreset preset)
        => builder.Configure(options => PermissionPresets.Apply(preset, options));

    public static PermissionBuilder UseConsolePrompt(this PermissionBuilder builder)
    {
        RegisterCore(builder.Services);
        builder.Services.Replace(ServiceDescriptor.Singleton<IPermissionPrompt, ConsolePermissionPrompt>());
        return builder;
    }

    public static PermissionBuilder AddRule(this PermissionBuilder builder, ToolPermissionRule rule)
        => builder.Configure(options => options.Rules.Add(rule));

    private static void RegisterCore(IServiceCollection services)
    {
        services.TryAddSingleton(GetOrCreateOptions(services));
        services.TryAddSingleton<IPermissionPrompt, NullPermissionPrompt>();
        services.TryAddSingleton<IToolPermissionHandler, RuleBasedPermissionHandler>();
        services.TryAddSingleton<PermissionToolMiddleware>();
        services.Replace(ServiceDescriptor.Singleton<IApprovalGate, RuleBasedApprovalGate>());
    }

    private static PermissionOptions GetOrCreateOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(s => s.ServiceType == typeof(PermissionOptions))?.ImplementationInstance as PermissionOptions;
        if (existing is not null)
            return existing;

        var created = new PermissionOptions();
        services.AddSingleton(created);
        return created;
    }
}