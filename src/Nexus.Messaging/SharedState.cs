using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Text.Json;

namespace Nexus.Messaging;

public interface ISharedState
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);
    Task<bool> CompareAndSwapAsync<T>(string key, T expected, T replacement, CancellationToken ct = default);
    IObservable<StateChange> Changes { get; }
}

public record StateChange(string Key, object? OldValue, object? NewValue, DateTimeOffset Timestamp);

public sealed class InMemorySharedState : ISharedState, IDisposable
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();
    private readonly Subject<StateChange> _changes = new();

    public IObservable<StateChange> Changes => _changes;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var bytes))
            return Task.FromResult(JsonSerializer.Deserialize<T>(bytes));
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var newBytes = JsonSerializer.SerializeToUtf8Bytes(value);
        _store.AddOrUpdate(key,
            _ =>
            {
                _changes.OnNext(new StateChange(key, null, value, DateTimeOffset.UtcNow));
                return newBytes;
            },
            (_, old) =>
            {
                var oldValue = JsonSerializer.Deserialize<T>(old);
                _changes.OnNext(new StateChange(key, oldValue, value, DateTimeOffset.UtcNow));
                return newBytes;
            });
        return Task.CompletedTask;
    }

    public Task<bool> CompareAndSwapAsync<T>(string key, T expected, T replacement, CancellationToken ct = default)
    {
        var expectedBytes = JsonSerializer.SerializeToUtf8Bytes(expected);
        var replacementBytes = JsonSerializer.SerializeToUtf8Bytes(replacement);

        if (_store.TryGetValue(key, out var current) &&
            current.AsSpan().SequenceEqual(expectedBytes.AsSpan()))
        {
            _store[key] = replacementBytes;
            _changes.OnNext(new StateChange(key, expected, replacement, DateTimeOffset.UtcNow));
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public void Dispose()
    {
        _changes.OnCompleted();
        _changes.Dispose();
    }
}
