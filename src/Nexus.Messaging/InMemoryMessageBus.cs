using System.Collections.Concurrent;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;

namespace Nexus.Messaging;

public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly ConcurrentDictionary<string, List<Subscription>> _subscriptions = new();
    private readonly ConcurrentDictionary<AgentId, List<Subscription>> _directSubscriptions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentMessage>> _pendingRequests = new();

    public async Task SendAsync(AgentId target, AgentMessage message, CancellationToken ct = default)
    {
        // Check if there's a pending request-reply
        if (message.CorrelationId is { } correlationId &&
            _pendingRequests.TryRemove(correlationId.ToString(), out var tcs))
        {
            tcs.TrySetResult(message);
            return;
        }

        if (_directSubscriptions.TryGetValue(target, out var handlers))
        {
            foreach (var sub in handlers.ToList())
            {
                await sub.Handler(message).ConfigureAwait(false);
            }
        }
    }

    public async Task PublishAsync(string topic, AgentMessage message, CancellationToken ct = default)
    {
        if (_subscriptions.TryGetValue(topic, out var handlers))
        {
            foreach (var sub in handlers.ToList())
            {
                await sub.Handler(message).ConfigureAwait(false);
            }
        }
    }

    public async Task<AgentMessage> RequestAsync(
        AgentId target, AgentMessage request, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<AgentMessage>();
        var requestId = request.Id.ToString();
        _pendingRequests[requestId] = tcs;

        await SendAsync(target, request, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

        return await tcs.Task.ConfigureAwait(false);
    }

    public IDisposable Subscribe(AgentId subscriber, string topic, Func<AgentMessage, Task> handler)
    {
        var sub = new Subscription(subscriber, handler);

        var topicSubs = _subscriptions.GetOrAdd(topic, _ => []);
        lock (topicSubs) { topicSubs.Add(sub); }

        var directSubs = _directSubscriptions.GetOrAdd(subscriber, _ => []);
        lock (directSubs) { directSubs.Add(sub); }

        return new SubscriptionHandle(() =>
        {
            lock (topicSubs) { topicSubs.Remove(sub); }
            lock (directSubs) { directSubs.Remove(sub); }
        });
    }

    public async Task BroadcastAsync(AgentMessage message, CancellationToken ct = default)
    {
        foreach (var (_, handlers) in _subscriptions)
        {
            foreach (var sub in handlers.ToList())
            {
                await sub.Handler(message).ConfigureAwait(false);
            }
        }
    }

    private sealed record Subscription(AgentId Subscriber, Func<AgentMessage, Task> Handler);

    private sealed class SubscriptionHandle(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
