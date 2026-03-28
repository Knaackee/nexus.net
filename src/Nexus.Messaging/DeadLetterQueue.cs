using System.Runtime.CompilerServices;
using Nexus.Core.Agents;

namespace Nexus.Messaging;

public interface IDeadLetterQueue
{
    Task EnqueueAsync(FailedTask task, CancellationToken ct = default);
    IAsyncEnumerable<FailedTask> DequeueAsync(CancellationToken ct = default);
    Task RetryAsync(FailedTask task, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public record FailedTask(AgentTask OriginalTask, Exception Error, DateTimeOffset FailedAt, int RetryCount);

public sealed class InMemoryDeadLetterQueue : IDeadLetterQueue, IDisposable
{
    private readonly Queue<FailedTask> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void Dispose() => _signal.Dispose();

    public Task EnqueueAsync(FailedTask task, CancellationToken ct = default)
    {
        lock (_queue)
        {
            _queue.Enqueue(task);
        }

        _signal.Release();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<FailedTask> DequeueAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            await _signal.WaitAsync(ct).ConfigureAwait(false);
            FailedTask? task;
            lock (_queue)
            {
                _queue.TryDequeue(out task);
            }

            if (task is not null)
                yield return task;
        }
    }

    public Task RetryAsync(FailedTask task, CancellationToken ct = default) =>
        EnqueueAsync(task with { RetryCount = task.RetryCount + 1 }, ct);

    public Task<int> CountAsync(CancellationToken ct = default)
    {
        lock (_queue)
        {
            return Task.FromResult(_queue.Count);
        }
    }
}
