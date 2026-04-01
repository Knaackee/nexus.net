using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Pipeline;
using Nexus.Core.Configuration;
using Nexus.Orchestration.Defaults;
using Nexus.Orchestration.Middleware;

namespace Nexus.Orchestration;

public static class OrchestrationServiceCollectionExtensions
{
    public static OrchestrationBuilder UseDefaults(this OrchestrationBuilder builder)
    {
        builder.Services.AddSingleton<ToolExecutorOptions>();
        builder.Services.AddSingleton<IToolExecutor>(sp =>
        {
            // Permission prompts currently run through ChatAgent + IApprovalGate so they can emit
            // agent state transitions/events. Exclude that middleware here to avoid double prompts.
            var middlewares = sp.GetServices<IToolMiddleware>()
                .Where(m => !string.Equals(
                    m.GetType().FullName,
                    "Nexus.Permissions.PermissionToolMiddleware",
                    StringComparison.Ordinal))
                .ToList();

            return new PartitionedToolExecutor(middlewares, sp.GetRequiredService<ToolExecutorOptions>());
        });
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMiddleware, BudgetGuardMiddleware>());
        builder.Services.AddSingleton<IAgentPool, DefaultAgentPool>();
        builder.Services.AddSingleton<IOrchestrator, DefaultOrchestrator>();
        return builder;
    }

    public static OrchestrationBuilder ConfigureToolExecutor(
        this OrchestrationBuilder builder,
        Action<ToolExecutorOptions> configure)
    {
        var existing = builder.Services
            .FirstOrDefault(s => s.ServiceType == typeof(ToolExecutorOptions))?
            .ImplementationInstance as ToolExecutorOptions;

        var options = existing ?? new ToolExecutorOptions();
        configure(options);

        if (existing is null)
            builder.Services.AddSingleton(options);

        return builder;
    }
}
