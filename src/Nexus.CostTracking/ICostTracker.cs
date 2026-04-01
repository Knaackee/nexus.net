namespace Nexus.CostTracking;

public interface ICostTracker
{
    Task RecordUsageAsync(string modelId, UsageSnapshot usage, CancellationToken ct = default);
    Task<CostTrackingSnapshot> GetSnapshotAsync(CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}