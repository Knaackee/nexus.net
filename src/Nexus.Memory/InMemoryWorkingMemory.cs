using System.Collections.Concurrent;
using System.Text.Json;
using Nexus.Core.Contracts;

namespace Nexus.Memory;

public sealed class InMemoryWorkingMemory : IWorkingMemory
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var bytes))
            return Task.FromResult(JsonSerializer.Deserialize<T>(bytes));
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        _store[key] = JsonSerializer.SerializeToUtf8Bytes(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _store.Clear();
        return Task.CompletedTask;
    }
}
