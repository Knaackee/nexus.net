using Microsoft.Extensions.AI;

namespace Nexus.Compaction;

public sealed class MicroCompactionStrategy : ICompactionStrategy
{
    public int Priority => 10;

    public bool ShouldCompact(CompactionContext context)
        => context.Messages.Count > context.Options.RecentMessagesToKeep
            && context.Messages.Any(static message => message.Role == ChatRole.Tool && !string.IsNullOrWhiteSpace(message.Text));

    public Task<CompactionResult> CompactAsync(CompactionContext context, CancellationToken ct = default)
    {
        var preservedTailCount = Math.Min(context.Options.RecentMessagesToKeep, context.Messages.Count);
        var olderBoundary = context.Messages.Count - preservedTailCount;
        var compacted = new List<ChatMessage>(context.Messages.Count);

        for (int index = 0; index < context.Messages.Count; index++)
        {
            var message = context.Messages[index];
            if (index < olderBoundary
                && message.Role == ChatRole.Tool
                && !string.IsNullOrWhiteSpace(message.Text)
                && message.Text.Length >= context.Options.MinimumToolContentLength)
            {
                compacted.Add(new ChatMessage(ChatRole.Tool,
                    $"[Compacted tool output: removed {message.Text.Length} chars from an older tool result.]"));
                continue;
            }

            compacted.Add(message);
        }

        var tokensBefore = context.Snapshot.CurrentTokenCount;
        var tokensAfter = context.TokenCounter.CountTokens(compacted, context.SystemPrompt, context.ModelId);
        return Task.FromResult(new CompactionResult(compacted, tokensBefore, tokensAfter, "micro"));
    }
}