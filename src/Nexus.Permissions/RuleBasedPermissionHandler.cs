using System.Text.Json;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Permissions;

public sealed class RuleBasedPermissionHandler : IToolPermissionHandler
{
    private readonly PermissionOptions _options;

    public RuleBasedPermissionHandler(PermissionOptions options)
    {
        _options = options;
    }

    public Task<PermissionDecision> EvaluateAsync(
        ITool tool,
        JsonElement input,
        IToolContext context,
        CancellationToken ct = default)
    {
        var permissionContext = new ToolPermissionContext(
            tool.Name,
            input,
            tool.Annotations,
            context.AgentId,
            context.Correlation);

        var rule = _options.Rules
            .Where(r => r.Matches(permissionContext))
            .OrderByDescending(r => r.Source)
            .ThenByDescending(r => r.Priority)
            .FirstOrDefault();

        return Task.FromResult(ToDecision(rule?.Action ?? _options.DefaultAction, rule?.Reason, _options.AskTimeout));
    }

    private static PermissionDecision ToDecision(PermissionAction action, string? reason, TimeSpan? timeout)
        => action switch
        {
            PermissionAction.Allow => new PermissionGranted(reason),
            PermissionAction.Deny => new PermissionDenied(reason ?? "Denied by permission rule"),
            PermissionAction.Ask => new PermissionAsk(reason ?? "Approval required", timeout),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
}