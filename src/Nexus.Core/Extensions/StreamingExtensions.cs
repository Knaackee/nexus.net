using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Core.Extensions;

public static class StreamingExtensions
{
    public static async IAsyncEnumerable<string> TextChunksOnly(
        this IAsyncEnumerable<AgentEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in events.WithCancellation(ct))
        {
            if (evt is TextChunkEvent text)
                yield return text.Text;
        }
    }

    public static async Task<AgentResult> ToResultAsync(
        this IAsyncEnumerable<AgentEvent> events,
        CancellationToken ct = default)
    {
        AgentResult? result = null;
        await foreach (var evt in events.WithCancellation(ct))
        {
            if (evt is AgentCompletedEvent completed)
                result = completed.Result;
            else if (evt is AgentFailedEvent failed)
                return AgentResult.Failed(failed.Error.Message);
        }

        return result ?? AgentResult.Failed("Stream ended without a completion event");
    }

    public static async Task<string> CollectTextAsync(
        this IAsyncEnumerable<AgentEvent> events,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var evt in events.WithCancellation(ct))
        {
            if (evt is TextChunkEvent text)
                sb.Append(text.Text);
        }

        return sb.ToString();
    }

    public static async IAsyncEnumerable<AgentEvent> WithSideEffect(
        this IAsyncEnumerable<AgentEvent> events,
        Action<AgentEvent> sideEffect,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in events.WithCancellation(ct))
        {
            sideEffect(evt);
            yield return evt;
        }
    }

    public static (IAsyncEnumerable<T> first, IAsyncEnumerable<T> second) Tee<T>(
        this IAsyncEnumerable<T> source)
    {
        var ch1 = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });
        var ch2 = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in source)
                {
                    await ch1.Writer.WriteAsync(item);
                    await ch2.Writer.WriteAsync(item);
                }
            }
            finally
            {
                ch1.Writer.Complete();
                ch2.Writer.Complete();
            }
        });

        return (ch1.Reader.ReadAllAsync(), ch2.Reader.ReadAllAsync());
    }
}
