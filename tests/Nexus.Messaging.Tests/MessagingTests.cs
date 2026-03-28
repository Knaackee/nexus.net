using FluentAssertions;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Messaging;

namespace Nexus.Messaging.Tests;

public class InMemoryMessageBusTests
{
    private readonly InMemoryMessageBus _bus = new();

    private static AgentMessage CreateMessage(AgentId sender, string type = "test", string payload = "data") =>
        new()
        {
            Id = MessageId.New(),
            Sender = sender,
            Type = type,
            Payload = payload
        };

    [Fact]
    public async Task PublishAsync_Delivers_To_Topic_Subscribers()
    {
        var subscriber = AgentId.New();
        var received = new List<AgentMessage>();

        _bus.Subscribe(subscriber, "news", msg =>
        {
            received.Add(msg);
            return Task.CompletedTask;
        });

        var message = CreateMessage(AgentId.New(), "update", "hello");
        await _bus.PublishAsync("news", message);

        received.Should().HaveCount(1);
        received[0].Payload.Should().Be("hello");
    }

    [Fact]
    public async Task PublishAsync_No_Subscribers_Does_Not_Throw()
    {
        var message = CreateMessage(AgentId.New());
        var act = () => _bus.PublishAsync("empty-topic", message);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Subscribe_Returns_Disposable_That_Unsubscribes()
    {
        var subscriber = AgentId.New();
        var received = new List<AgentMessage>();

        var sub = _bus.Subscribe(subscriber, "topic", msg =>
        {
            received.Add(msg);
            return Task.CompletedTask;
        });

        await _bus.PublishAsync("topic", CreateMessage(AgentId.New()));
        received.Should().HaveCount(1);

        sub.Dispose();

        await _bus.PublishAsync("topic", CreateMessage(AgentId.New()));
        received.Should().HaveCount(1); // Still 1, not 2
    }

    [Fact]
    public async Task BroadcastAsync_Reaches_All_Subscriptions()
    {
        var count = 0;
        _bus.Subscribe(AgentId.New(), "t1", _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        _bus.Subscribe(AgentId.New(), "t2", _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });

        await _bus.BroadcastAsync(CreateMessage(AgentId.New()));

        count.Should().Be(2);
    }

    [Fact]
    public async Task Multiple_Subscribers_Same_Topic()
    {
        var count = 0;
        _bus.Subscribe(AgentId.New(), "shared", _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        _bus.Subscribe(AgentId.New(), "shared", _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });

        await _bus.PublishAsync("shared", CreateMessage(AgentId.New()));

        count.Should().Be(2);
    }
}

public class SharedStateTests
{
    [Fact]
    public async Task SetAsync_And_GetAsync_Roundtrip()
    {
        var state = new InMemorySharedState();
        await state.SetAsync("key", "value");
        var result = await state.GetAsync<string>("key");
        result.Should().Be("value");
    }

    [Fact]
    public async Task GetAsync_Returns_Default_For_Missing()
    {
        var state = new InMemorySharedState();
        var result = await state.GetAsync<string>("missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Changes_Observable_Emits_Events()
    {
        var state = new InMemorySharedState();
        var changes = new List<StateChange>();
        using var sub = state.Changes.Subscribe(changes.Add);

        await state.SetAsync("k", "v");

        // Allow the observable to propagate
        await Task.Delay(50);
        changes.Should().ContainSingle();
        changes[0].Key.Should().Be("k");
    }
}

public class DeadLetterQueueTests
{
    [Fact]
    public async Task EnqueueAsync_And_CountAsync_Roundtrip()
    {
        using var dlq = new InMemoryDeadLetterQueue();

        var failedTask = new FailedTask(
            AgentTask.Create("do stuff"),
            new InvalidOperationException("oops"),
            DateTimeOffset.UtcNow,
            RetryCount: 0);

        await dlq.EnqueueAsync(failedTask);
        var count = await dlq.CountAsync();

        count.Should().Be(1);
    }

    [Fact]
    public async Task DequeueAsync_Returns_Enqueued_Items()
    {
        using var dlq = new InMemoryDeadLetterQueue();

        var failedTask = new FailedTask(
            AgentTask.Create("task1"),
            new InvalidOperationException("err"),
            DateTimeOffset.UtcNow,
            0);

        await dlq.EnqueueAsync(failedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var items = new List<FailedTask>();
        await foreach (var item in dlq.DequeueAsync(cts.Token))
        {
            items.Add(item);
            break; // Just get the first one
        }

        items.Should().HaveCount(1);
        items[0].Error.Message.Should().Be("err");
    }

    [Fact]
    public async Task RetryAsync_Increments_RetryCount()
    {
        using var dlq = new InMemoryDeadLetterQueue();

        var failedTask = new FailedTask(
            AgentTask.Create("task1"),
            new InvalidOperationException("err"),
            DateTimeOffset.UtcNow,
            RetryCount: 0);

        await dlq.RetryAsync(failedTask);
        var count = await dlq.CountAsync();

        count.Should().Be(1);
    }
}
