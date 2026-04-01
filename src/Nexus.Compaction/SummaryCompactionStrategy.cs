using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace Nexus.Compaction;

public sealed class SummaryCompactionStrategy : ICompactionStrategy
{
    public int Priority => 50;

    public bool ShouldCompact(CompactionContext context)
    {
        var olderMessages = GetOlderMessages(context);
        return olderMessages.Count >= context.Options.MinimumSummaryCandidateMessages;
    }

    public async Task<CompactionResult> CompactAsync(CompactionContext context, CancellationToken ct = default)
    {
        var systemMessage = context.Messages.FirstOrDefault(static message => message.Role == ChatRole.System);
        var recentMessages = GetRecentMessages(context);
        var olderMessages = GetOlderMessages(context);
        if (olderMessages.Count == 0)
            return new CompactionResult(context.Messages, context.Snapshot.CurrentTokenCount, context.Snapshot.CurrentTokenCount, "summary");

        var summaryPrompt = BuildPrompt(context, olderMessages);
        var response = await context.ChatClient.GetResponseAsync([summaryPrompt], cancellationToken: ct).ConfigureAwait(false);
        var summaryText = response.Text ?? "Conversation history compacted.";

        var compacted = new List<ChatMessage>();
        if (systemMessage is not null)
            compacted.Add(systemMessage);

        compacted.Add(new ChatMessage(ChatRole.Assistant, $"[Conversation summary]\n{summaryText}"));
        compacted.AddRange(recentMessages);

        var tokensBefore = context.Snapshot.CurrentTokenCount;
        var tokensAfter = context.TokenCounter.CountTokens(compacted, context.SystemPrompt, context.ModelId);
        return new CompactionResult(compacted, tokensBefore, tokensAfter, "summary");
    }

    private static List<ChatMessage> GetRecentMessages(CompactionContext context)
    {
        var nonSystem = context.Messages.Where(static message => message.Role != ChatRole.System).ToList();
        return nonSystem.TakeLast(context.Options.RecentMessagesToKeep).ToList();
    }

    private static List<ChatMessage> GetOlderMessages(CompactionContext context)
    {
        var nonSystem = context.Messages.Where(static message => message.Role != ChatRole.System).ToList();
        var olderCount = Math.Max(0, nonSystem.Count - context.Options.RecentMessagesToKeep);
        return nonSystem.Take(olderCount).ToList();
    }

    private static ChatMessage BuildPrompt(CompactionContext context, IReadOnlyList<ChatMessage> olderMessages)
    {
        var builder = new StringBuilder();
        builder.AppendLine(context.Options.SummaryInstruction);
        builder.AppendLine();
        foreach (var message in olderMessages)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", message.Role, message.Text));
            builder.AppendLine();
        }

        return new ChatMessage(ChatRole.User, builder.ToString());
    }
}