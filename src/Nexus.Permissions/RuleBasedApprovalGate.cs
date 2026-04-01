using System.Text.Json;
using Nexus.Core.Contracts;

namespace Nexus.Permissions;

public sealed class RuleBasedApprovalGate : IApprovalGate
{
    private readonly PermissionOptions _options;
    private readonly IPermissionPrompt _prompt;
    private readonly IAuditLog _auditLog;

    public RuleBasedApprovalGate(
        PermissionOptions options,
        IPermissionPrompt prompt,
        IAuditLog auditLog)
    {
        _options = options;
        _prompt = prompt;
        _auditLog = auditLog;
    }

    public async Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var context = new ToolPermissionContext(
            request.ToolName ?? string.Empty,
            request.Context ?? JsonDocument.Parse("{}").RootElement,
            Annotations: null,
            request.RequestingAgent,
            new CorrelationContext
            {
                TraceId = request.RequestingAgent.ToString(),
                SpanId = "permissions"
            });

        var rule = _options.Rules
            .Where(r => r.Matches(context))
            .OrderByDescending(r => r.Source)
            .ThenByDescending(r => r.Priority)
            .FirstOrDefault();

        var action = rule?.Action ?? _options.DefaultAction;
        var result = action switch
        {
            PermissionAction.Allow => new ApprovalResult(true, "permissions", rule?.Reason ?? "Allowed by permission rule"),
            PermissionAction.Deny => new ApprovalResult(false, Comment: rule?.Reason ?? "Denied by permission rule"),
            PermissionAction.Ask => await _prompt.PromptAsync(request, timeout ?? _options.AskTimeout, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported permission action: {action}"),
        };

        await _auditLog.RecordAsync(new AuditEntry(
            DateTimeOffset.UtcNow,
            "tool.permission",
            request.RequestingAgent,
            ApprovedBy(result),
            request.RequestingAgent.ToString(),
            JsonSerializer.SerializeToElement(new
            {
                request.ToolName,
                request.Description,
                result.IsApproved,
                result.Comment,
                Action = action.ToString(),
            }),
            result.IsApproved ? AuditSeverity.Info : AuditSeverity.Warning), ct).ConfigureAwait(false);

        return result;
    }

    private static string? ApprovedBy(ApprovalResult result) => result.ApprovedBy;
}