using System.Collections.Concurrent;
using System.Text.Json;

namespace Nexus.Orchestration.Checkpointing;

/// <summary>Serializes snapshots to/from bytes using System.Text.Json.</summary>
public interface ISnapshotSerializer
{
    byte[] Serialize(OrchestrationSnapshot snapshot);
    OrchestrationSnapshot Deserialize(byte[] data);
}

public sealed class JsonSnapshotSerializer : ISnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public byte[] Serialize(OrchestrationSnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);

    public OrchestrationSnapshot Deserialize(byte[] data) =>
        JsonSerializer.Deserialize<OrchestrationSnapshot>(data, Options)
        ?? throw new InvalidOperationException("Failed to deserialize snapshot.");
}

/// <summary>In-memory checkpoint store for development and testing.</summary>
public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<CheckpointId, OrchestrationSnapshot> _snapshots = new();

    public Task<CheckpointId> SaveAsync(OrchestrationSnapshot snapshot, CancellationToken ct = default)
    {
        _snapshots[snapshot.Id] = snapshot;
        return Task.FromResult(snapshot.Id);
    }

    public Task<OrchestrationSnapshot?> LoadAsync(CheckpointId id, CancellationToken ct = default)
    {
        _snapshots.TryGetValue(id, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<OrchestrationSnapshot?> LoadLatestAsync(TaskGraphId graphId, CancellationToken ct = default)
    {
        var latest = _snapshots.Values
            .Where(s => s.GraphId == graphId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public Task<IReadOnlyList<CheckpointId>> ListAsync(TaskGraphId graphId, CancellationToken ct = default)
    {
        IReadOnlyList<CheckpointId> ids = _snapshots.Values
            .Where(s => s.GraphId == graphId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Id)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task DeleteAsync(CheckpointId id, CancellationToken ct = default)
    {
        _snapshots.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public int Count => _snapshots.Count;

    public void Clear() => _snapshots.Clear();
}
