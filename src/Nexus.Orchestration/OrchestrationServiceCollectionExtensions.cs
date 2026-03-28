using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;
using Nexus.Orchestration.Defaults;

namespace Nexus.Orchestration;

public static class OrchestrationServiceCollectionExtensions
{
    public static OrchestrationBuilder UseDefaults(this OrchestrationBuilder builder)
    {
        builder.Services.AddSingleton<IAgentPool, DefaultAgentPool>();
        builder.Services.AddSingleton<IOrchestrator, DefaultOrchestrator>();
        return builder;
    }
}
