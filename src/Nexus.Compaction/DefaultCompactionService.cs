using Microsoft.Extensions.AI;
using Nexus.Core.Agents;

namespace Nexus.Compaction;

public sealed class DefaultCompactionService : ICompactionService
{
    private readonly IEnumerable<ICompactionStrategy> _strategies;
    private readonly IContextWindowMonitor _monitor;
    private readonly ITokenCounter _tokenCounter;
    private readonly CompactionOptions _options;

    public DefaultCompactionService(
        IEnumerable<ICompactionStrategy> strategies,
        IContextWindowMonitor monitor,
        ITokenCounter tokenCounter,
        CompactionOptions options)
    {
        _strategies = strategies.OrderBy(strategy => strategy.Priority).ToArray();
        _monitor = monitor;
        _tokenCounter = tokenCounter;
        _options = options;
    }

    public bool ShouldCompact(
        IReadOnlyList<ChatMessage> messages,
        ContextWindowOptions windowOptions,
        string? systemPrompt = null,
        string? modelId = null)
    {
        var snapshot = _monitor.Measure(messages, windowOptions, systemPrompt, modelId);
        return snapshot.CurrentTokenCount >= windowOptions.TargetTokens
            || snapshot.FillRatio >= _options.AutoCompactThreshold;
    }

    public async Task<CompactionResult> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        ContextWindowOptions windowOptions,
        IChatClient chatClient,
        string? systemPrompt = null,
        string? modelId = null,
        CancellationToken ct = default)
    {
        var snapshot = _monitor.Measure(messages, windowOptions, systemPrompt, modelId);
        var context = new CompactionContext
        {
            Messages = messages,
            WindowOptions = windowOptions,
            Snapshot = snapshot,
            TokenCounter = _tokenCounter,
            ChatClient = chatClient,
            Options = _options,
            SystemPrompt = systemPrompt,
            ModelId = modelId,
        };

        foreach (var strategy in _strategies)
        {
            if (!strategy.ShouldCompact(context))
                continue;

            var result = await strategy.CompactAsync(context, ct).ConfigureAwait(false);
            if (result.TokensAfter < result.TokensBefore)
                return result;
        }

        return new CompactionResult(messages, snapshot.CurrentTokenCount, snapshot.CurrentTokenCount, "none");
    }
}