namespace Nexus.CostTracking;

/// <summary>
/// Tracks token usage and estimated cost per model and session.
/// </summary>
public interface ICostTracker
{
    /// <summary>Records a usage snapshot for the specified model.</summary>
    Task RecordUsageAsync(string modelId, UsageSnapshot usage, CancellationToken ct = default);
    /// <summary>Returns the current aggregated cost tracking snapshot.</summary>
    Task<CostTrackingSnapshot> GetSnapshotAsync(CancellationToken ct = default);
    /// <summary>Resets all tracked usage and cost data.</summary>
    Task ResetAsync(CancellationToken ct = default);
}