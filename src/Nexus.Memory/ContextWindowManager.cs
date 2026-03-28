using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using System.Diagnostics.CodeAnalysis;

namespace Nexus.Memory;

public interface IContextWindowManager
{
    int EstimateTokens(IEnumerable<ChatMessage> messages, string? modelId = null);
    IReadOnlyList<ChatMessage> Trim(IReadOnlyList<ChatMessage> messages, int maxTokens, ContextTrimStrategy strategy);
    Task<IReadOnlyList<ChatMessage>> CompressAsync(IReadOnlyList<ChatMessage> messages, int targetTokens, IChatClient summarizer, CancellationToken ct = default);
}

public sealed class DefaultContextWindowManager : IContextWindowManager
{
    private const double CharsPerToken = 4.0;

    public int EstimateTokens(IEnumerable<ChatMessage> messages, string? modelId = null)
    {
        return EstimateTokenCount(messages);
    }

    private static int EstimateTokenCount(IEnumerable<ChatMessage> messages)
    {
        return messages.Sum(m => (int)Math.Ceiling((m.Text?.Length ?? 0) / CharsPerToken));
    }

    public IReadOnlyList<ChatMessage> Trim(
        IReadOnlyList<ChatMessage> messages, int maxTokens, ContextTrimStrategy strategy)
    {
        if (EstimateTokenCount(messages) <= maxTokens)
            return messages;

        return strategy switch
        {
            ContextTrimStrategy.SlidingWindow => TrimSlidingWindow(messages, maxTokens),
            ContextTrimStrategy.KeepFirstAndLast => TrimKeepFirstAndLast(messages, maxTokens),
            _ => TrimSlidingWindow(messages, maxTokens),
        };
    }

    public async Task<IReadOnlyList<ChatMessage>> CompressAsync(
        IReadOnlyList<ChatMessage> messages, int targetTokens, IChatClient summarizer, CancellationToken ct = default)
    {
        if (EstimateTokenCount(messages) <= targetTokens)
            return messages;

        var systemMsg = messages.FirstOrDefault(m => m.Role == ChatRole.System);
        var nonSystem = messages.Where(m => m.Role != ChatRole.System).ToList();

        var textToSummarize = string.Join("\n", nonSystem.Select(m => $"{m.Role}: {m.Text}"));
        var summaryPrompt = new ChatMessage(ChatRole.User,
            $"Summarize this conversation concisely:\n{textToSummarize}");

        var response = await summarizer.GetResponseAsync([summaryPrompt], cancellationToken: ct).ConfigureAwait(false);

        var result = new List<ChatMessage>();
        if (systemMsg is not null)
            result.Add(systemMsg);
        result.Add(new ChatMessage(ChatRole.Assistant, $"[Summary of prior conversation]: {response.Text}"));

        // Keep last few messages
        var recentCount = Math.Min(3, nonSystem.Count);
        result.AddRange(nonSystem.TakeLast(recentCount));
        return result;
    }

    private static List<ChatMessage> TrimSlidingWindow(IReadOnlyList<ChatMessage> messages, int maxTokens)
    {
        var result = new List<ChatMessage>();
        var systemMsg = messages.FirstOrDefault(m => m.Role == ChatRole.System);
        if (systemMsg is not null)
            result.Add(systemMsg);

        int tokenBudget = maxTokens - EstimateTokenCount(systemMsg is not null ? [systemMsg] : []);

        // Take from the end
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.System) continue;
            var tokens = EstimateTokenCount([messages[i]]);
            if (tokenBudget - tokens < 0 && result.Count > 1) break;
            tokenBudget -= tokens;
            result.Insert(systemMsg is not null ? 1 : 0, messages[i]);
        }

        return result;
    }

    private static List<ChatMessage> TrimKeepFirstAndLast(IReadOnlyList<ChatMessage> messages, int maxTokens)
    {
        if (messages.Count <= 4)
            return messages.ToList();

        var result = new List<ChatMessage>();
        result.Add(messages[0]); // System or first user
        if (messages.Count > 1)
            result.Add(messages[1]); // First exchange

        result.AddRange(messages.TakeLast(2));
        return result;
    }
}
