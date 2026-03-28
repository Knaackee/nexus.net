using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexus.Hosting.AspNetCore.Endpoints;
using Nexus.Hosting.AspNetCore.HealthChecks;

namespace Nexus.Hosting.AspNetCore;

/// <summary>
/// Extension methods for integrating Nexus with ASP.NET Core.
/// </summary>
public static class NexusEndpointExtensions
{
    /// <summary>
    /// Maps the AG-UI SSE streaming endpoint that bridges orchestration events to the frontend.
    /// </summary>
    public static IEndpointConventionBuilder MapAgUiEndpoint(
        this IEndpointRouteBuilder endpoints, string pattern = "/agent/stream")
    {
        return endpoints.MapPost(pattern, async (HttpContext ctx) =>
        {
            var orchestrator = ctx.RequestServices.GetRequiredService<Nexus.Orchestration.IOrchestrator>();
            var graph = ctx.RequestServices.GetRequiredService<Nexus.Orchestration.ITaskGraph>();
            await AgUiEndpoint.HandleAsync(ctx, orchestrator, graph, ctx.RequestAborted)
                .ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Maps the A2A JSON-RPC endpoint for agent-to-agent communication.
    /// </summary>
    public static IEndpointConventionBuilder MapA2AEndpoint(
        this IEndpointRouteBuilder endpoints, string pattern = "/a2a")
    {
        return endpoints.MapPost(pattern, async (HttpContext ctx) =>
        {
            var handler = ctx.RequestServices
                .GetRequiredService<Func<System.Text.Json.JsonElement, CancellationToken, Task<System.Text.Json.JsonElement>>>();
            await A2AEndpoint.HandleAsync(ctx, handler, ctx.RequestAborted)
                .ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Adds Nexus health checks to the health check builder.
    /// </summary>
    public static IHealthChecksBuilder AddNexusHealthChecks(this IHealthChecksBuilder builder)
    {
        builder.AddCheck<AgentPoolHealthCheck>("nexus-agents", HealthStatus.Degraded, ["nexus"]);
        return builder;
    }

    /// <summary>
    /// Registers all Nexus ASP.NET Core services and maps standard endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapNexusEndpoints(
        this IEndpointRouteBuilder endpoints,
        string agUiPattern = "/agent/stream",
        string healthPattern = "/health")
    {
        endpoints.MapAgUiEndpoint(agUiPattern);
        endpoints.MapHealthChecks(healthPattern);
        return endpoints;
    }
}
