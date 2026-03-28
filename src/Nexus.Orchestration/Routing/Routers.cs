using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Nexus.Core.Routing;

namespace Nexus.Orchestration.Routing;

public class RoundRobinRouter : IChatClientRouter
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new();
    private readonly object _lock = new();
    private int _index;
    private string[] _keys = [];

    public void Register(string name, IChatClient client)
    {
        _clients[name] = client;
        lock (_lock) { _keys = _clients.Keys.ToArray(); }
    }

    public IChatClient Resolve(string? name = null)
    {
        if (name is not null && _clients.TryGetValue(name, out var specific))
            return specific;

        if (_keys.Length == 0)
            throw new InvalidOperationException("No chat clients registered");

        var idx = Interlocked.Increment(ref _index) % _keys.Length;
        return _clients[_keys[idx]];
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Resolve().GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Resolve().GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { GC.SuppressFinalize(this); }
}

public class FallbackRouter : IChatClientRouter
{
    private readonly List<(string Name, IChatClient Client)> _orderedClients = [];

    public void Register(string name, IChatClient client) =>
        _orderedClients.Add((name, client));

    public IChatClient Resolve(string? name = null)
    {
        if (name is not null)
        {
            var match = _orderedClients.Find(c => c.Name == name);
            return match.Client ?? throw new InvalidOperationException($"Client '{name}' not found");
        }

        return _orderedClients.Count > 0
            ? _orderedClients[0].Client
            : throw new InvalidOperationException("No chat clients registered");
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();
        Exception? lastException = null;

        foreach (var (_, client) in _orderedClients)
        {
            try
            {
                return await client.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException ?? new InvalidOperationException("No chat clients registered");
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();

        // Try each client; on the first one that produces at least one update, stream it fully.
        // We cannot yield inside try-catch in C#, so we buffer per-client attempt on failure.
        foreach (var (_, client) in _orderedClients)
        {
            var updates = new List<ChatResponseUpdate>();
            bool failed = false;

            try
            {
                await foreach (var update in client.GetStreamingResponseAsync(messageList, options, cancellationToken))
                {
                    updates.Add(update);
                }
            }
            catch
            {
                failed = true;
            }

            if (!failed || updates.Count > 0)
            {
                foreach (var update in updates)
                    yield return update;

                if (!failed)
                    yield break;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { GC.SuppressFinalize(this); }
}
