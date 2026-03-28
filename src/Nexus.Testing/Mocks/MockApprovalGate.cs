using Nexus.Core.Contracts;
using Nexus.Core.Agents;
using System.Text.Json;

namespace Nexus.Testing.Mocks;

/// <summary>
/// A mock approval gate for testing human-in-the-loop scenarios.
/// </summary>
public sealed class MockApprovalGate : IApprovalGate
{
    private readonly Func<ApprovalRequest, bool> _policy;
    public List<ApprovalRequest> ReceivedRequests { get; } = [];

    private MockApprovalGate(Func<ApprovalRequest, bool> policy)
    {
        _policy = policy;
    }

    public static MockApprovalGate AutoApprove() => new(_ => true);

    public static MockApprovalGate AutoDeny() => new(_ => false);

    public static MockApprovalGate ApproveNth(int n)
    {
        var count = 0;
        return new MockApprovalGate(_ => ++count == n);
    }

    public static MockApprovalGate WithPolicy(Func<ApprovalRequest, bool> policy) => new(policy);

    public Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);
        var approved = _policy(request);
        return Task.FromResult(new ApprovalResult(approved, approved ? "mock-approve" : null));
    }
}
