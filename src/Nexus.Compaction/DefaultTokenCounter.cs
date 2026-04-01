using Microsoft.Extensions.AI;

namespace Nexus.Compaction;

public sealed class DefaultTokenCounter : ITokenCounter
{
    private const double CharsPerToken = 4.0;

    public int CountTokens(ChatMessage message, string? modelId = null)
        => CountTokens([message], modelId: modelId);

    public int CountTokens(IEnumerable<ChatMessage> messages, string? systemPrompt = null, string? modelId = null)
    {
        var totalCharacters = messages.Sum(message => message.Text?.Length ?? 0);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            totalCharacters += systemPrompt.Length;

        return (int)Math.Ceiling(totalCharacters / CharsPerToken);
    }
}