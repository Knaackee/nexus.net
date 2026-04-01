using Nexus.Core.Contracts;

namespace Nexus.Permissions;

public interface IPermissionPrompt
{
    Task<ApprovalResult> PromptAsync(
        ApprovalRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}