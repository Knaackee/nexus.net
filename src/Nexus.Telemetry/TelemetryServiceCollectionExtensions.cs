using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;

namespace Nexus.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    public static TelemetryBuilder UseOpenTelemetry(this TelemetryBuilder builder)
    {
        builder.Services.AddSingleton<TelemetryAgentMiddleware>();
        builder.Services.AddSingleton<TelemetryToolMiddleware>();
        return builder;
    }
}
