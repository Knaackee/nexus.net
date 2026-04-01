using System.Text.Json;
using Nexus.Core.Contracts;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Permissions;

public sealed class PermissionToolMiddleware : IToolMiddleware
{
    private readonly IToolPermissionHandler _handler;
    private readonly IPermissionPrompt _prompt;
    private readonly IAuditLog _auditLog;

    public PermissionToolMiddleware(
        IToolPermissionHandler handler,
        IPermissionPrompt prompt,
        IAuditLog auditLog)
    {
        _handler = handler;
        _prompt = prompt;
        _auditLog = auditLog;
    }

    public async Task<ToolResult> InvokeAsync(
        ITool tool,
        JsonElement input,
        IToolContext ctx,
        ToolExecutionDelegate next,
        CancellationToken ct)
    {
        var decision = await _handler.EvaluateAsync(tool, input, ctx, ct).ConfigureAwait(false);

        switch (decision)
        {
            case PermissionGranted:
                await RecordAsync(tool.Name, ctx.AgentId, "allowed", ct).ConfigureAwait(false);
                return await next(tool, input, ctx, ct).ConfigureAwait(false);

            case PermissionDenied denied:
                await RecordAsync(tool.Name, ctx.AgentId, denied.Reason, ct).ConfigureAwait(false);
                return ToolResult.Denied(denied.Reason);

            case PermissionAsk ask:
            {
                var approval = await _prompt.PromptAsync(
                    new ApprovalRequest($"Execute tool '{tool.Name}'", ctx.AgentId, tool.Name, input),
                    ask.Timeout,
                    ct).ConfigureAwait(false);

                await RecordAsync(tool.Name, ctx.AgentId, approval.IsApproved ? "approved" : approval.Comment ?? "denied", ct)
                    .ConfigureAwait(false);

                return approval.IsApproved
                    ? await next(tool, input, ctx, ct).ConfigureAwait(false)
                    : ToolResult.Denied(approval.Comment ?? "Denied by permission prompt");
            }

            default:
                throw new InvalidOperationException($"Unsupported decision type: {decision.GetType().Name}");
        }
    }

    private Task RecordAsync(string toolName, Nexus.Core.Agents.AgentId agentId, string result, CancellationToken ct)
        => _auditLog.RecordAsync(new AuditEntry(
            DateTimeOffset.UtcNow,
            "tool.permission.middleware",
            agentId,
            CorrelationId: agentId.ToString(),
            Details: JsonSerializer.SerializeToElement(new { ToolName = toolName, Result = result })), ct);
}