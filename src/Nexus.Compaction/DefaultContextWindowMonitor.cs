using Microsoft.Extensions.AI;
using Nexus.Core.Agents;

namespace Nexus.Compaction;

public sealed class DefaultContextWindowMonitor : IContextWindowMonitor
{
    private readonly ITokenCounter _tokenCounter;

    public DefaultContextWindowMonitor(ITokenCounter tokenCounter)
    {
        _tokenCounter = tokenCounter;
    }

    public ContextWindowSnapshot Measure(
        IReadOnlyList<ChatMessage> messages,
        ContextWindowOptions windowOptions,
        string? systemPrompt = null,
        string? modelId = null)
    {
        var currentTokens = _tokenCounter.CountTokens(messages, systemPrompt, modelId);
        var effectiveMaxTokens = Math.Max(1, windowOptions.MaxTokens - windowOptions.ReservedForOutput - windowOptions.ReservedForTools);
        var availableTokens = Math.Max(0, effectiveMaxTokens - currentTokens);
        var fillRatio = effectiveMaxTokens == 0
            ? 1.0
            : Math.Clamp((double)currentTokens / effectiveMaxTokens, 0.0, double.MaxValue);

        return new ContextWindowSnapshot(
            currentTokens,
            effectiveMaxTokens,
            windowOptions.ReservedForOutput,
            windowOptions.ReservedForTools,
            availableTokens,
            fillRatio);
    }
}