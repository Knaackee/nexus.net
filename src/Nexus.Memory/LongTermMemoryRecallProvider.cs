using Microsoft.Extensions.AI;
using Nexus.Compaction;

namespace Nexus.Memory;

public sealed class LongTermMemoryRecallProvider : ICompactionRecallProvider
{
    private readonly ILongTermMemory _longTermMemory;
    private readonly LongTermMemoryRecallOptions _options;

    public LongTermMemoryRecallProvider(ILongTermMemory longTermMemory, LongTermMemoryRecallOptions options)
    {
        _longTermMemory = longTermMemory;
        _options = options;
    }

    public int Priority => _options.Priority;

    public async Task<IReadOnlyList<ChatMessage>> RecallAsync(CompactionRecallContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var query = _options.QueryFactory(context);
        if (string.IsNullOrWhiteSpace(query))
            return context.ActiveMessages;

        var recalled = await _longTermMemory.RecallAsync(query, _options.MaxResults, ct).ConfigureAwait(false);
        var relevant = recalled
            .Where(result => result.Relevance >= _options.MinimumRelevance)
            .Where(result => !string.IsNullOrWhiteSpace(result.Content))
            .Where(result => context.ActiveMessages.All(message => !string.Equals(message.Text, result.Content, StringComparison.Ordinal)))
            .ToArray();

        if (relevant.Length == 0)
            return context.ActiveMessages;

        var recalledMessage = new ChatMessage(_options.MessageRole, _options.FormatMessage(relevant));
        return [recalledMessage, .. context.ActiveMessages];
    }
}

public sealed class LongTermMemoryRecallOptions
{
    public int Priority { get; set; } = 100;
    public int MaxResults { get; set; } = 3;
    public double MinimumRelevance { get; set; } = 0.05;
    public ChatRole MessageRole { get; set; } = ChatRole.System;
    public Func<CompactionRecallContext, string> QueryFactory { get; set; } = static context =>
        context.OriginalMessages
            .Where(message => message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text))
            .Select(message => message.Text!)
            .LastOrDefault()
        ?? context.ActiveMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.Text))
            .Select(message => message.Text!)
            .LastOrDefault()
        ?? string.Empty;

    public Func<IReadOnlyList<MemoryResult>, string> FormatMessage { get; set; } = static results =>
    {
        var lines = new List<string>(results.Count + 1)
        {
            "[Recalled memory]",
        };

        lines.AddRange(results.Select(result => $"- {result.Content}"));
        return string.Join(Environment.NewLine, lines);
    };
}