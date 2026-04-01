using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;

namespace Nexus.AgentLoop;

public static class AgentLoopServiceCollectionExtensions
{
    public static AgentLoopBuilder UseDefaults(this AgentLoopBuilder builder)
    {
        builder.Services.AddSingleton<IAgentLoop, DefaultAgentLoop>();
        return builder;
    }
}