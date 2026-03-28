using System.Text.Json;
using Nexus.Core.Agents;

namespace Nexus.Core.Contracts;

public interface IApprovalGate
{
    Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default);
}

public record ApprovalRequest(
    string Description,
    AgentId RequestingAgent,
    string? ToolName = null,
    JsonElement? Context = null);

public record ApprovalResult(
    bool IsApproved,
    string? ApprovedBy = null,
    string? Comment = null,
    JsonElement? ModifiedContext = null);

public class AutoApproveGate : IApprovalGate
{
    public Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
        => Task.FromResult(new ApprovalResult(true, "auto-approve"));
}
