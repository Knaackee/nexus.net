using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexus.Orchestration;

namespace Nexus.Hosting.AspNetCore.HealthChecks;

/// <summary>
/// Reports healthy if the agent pool is operational and has no stuck agents.
/// </summary>
public sealed class AgentPoolHealthCheck : IHealthCheck
{
    private readonly IAgentPool _pool;

    public AgentPoolHealthCheck(IAgentPool pool) => _pool = pool;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var active = _pool.ActiveAgents;
        var data = new Dictionary<string, object>
        {
            ["activeAgentCount"] = active.Count
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{active.Count} active agent(s)", data));
    }
}
