using Nexus.Core.Contracts;

namespace Nexus.Permissions;

public sealed class NullPermissionPrompt : IPermissionPrompt
{
    public Task<ApprovalResult> PromptAsync(
        ApprovalRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => Task.FromResult(new ApprovalResult(false, Comment: "No permission prompt registered"));
}