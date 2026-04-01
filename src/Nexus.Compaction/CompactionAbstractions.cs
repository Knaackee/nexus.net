using Microsoft.Extensions.AI;
using Nexus.Core.Agents;

namespace Nexus.Compaction;

public interface ITokenCounter
{
    int CountTokens(ChatMessage message, string? modelId = null);
    int CountTokens(IEnumerable<ChatMessage> messages, string? systemPrompt = null, string? modelId = null);
}

public interface IContextWindowMonitor
{
    ContextWindowSnapshot Measure(
        IReadOnlyList<ChatMessage> messages,
        ContextWindowOptions windowOptions,
        string? systemPrompt = null,
        string? modelId = null);
}

public interface ICompactionStrategy
{
    int Priority { get; }

    bool ShouldCompact(CompactionContext context);

    Task<CompactionResult> CompactAsync(CompactionContext context, CancellationToken ct = default);
}

public interface ICompactionService
{
    bool ShouldCompact(
        IReadOnlyList<ChatMessage> messages,
        ContextWindowOptions windowOptions,
        string? systemPrompt = null,
        string? modelId = null);

    Task<CompactionResult> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        ContextWindowOptions windowOptions,
        IChatClient chatClient,
        string? systemPrompt = null,
        string? modelId = null,
        CancellationToken ct = default);
}

public interface ICompactionRecallProvider
{
    int Priority { get; }

    Task<IReadOnlyList<ChatMessage>> RecallAsync(CompactionRecallContext context, CancellationToken ct = default);
}

public interface ICompactionRecallService
{
    Task<CompactionRecallResult> RecallAsync(CompactionRecallContext context, CancellationToken ct = default);
}

public sealed record ContextWindowSnapshot(
    int CurrentTokenCount,
    int EffectiveMaxTokens,
    int ReservedForOutput,
    int ReservedForTools,
    int AvailableTokens,
    double FillRatio);

public sealed record CompactionContext
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required ContextWindowOptions WindowOptions { get; init; }
    public required ContextWindowSnapshot Snapshot { get; init; }
    public required ITokenCounter TokenCounter { get; init; }
    public required IChatClient ChatClient { get; init; }
    public required CompactionOptions Options { get; init; }
    public string? SystemPrompt { get; init; }
    public string? ModelId { get; init; }
}

public sealed record CompactionRecallContext
{
    public required IReadOnlyList<ChatMessage> OriginalMessages { get; init; }
    public required IReadOnlyList<ChatMessage> ActiveMessages { get; init; }
    public required CompactionResult Compaction { get; init; }
    public required ContextWindowOptions WindowOptions { get; init; }
    public string? SystemPrompt { get; init; }
    public string? ModelId { get; init; }
}

public sealed record CompactionResult(
    IReadOnlyList<ChatMessage> CompactedMessages,
    int TokensBefore,
    int TokensAfter,
    string StrategyUsed);

public sealed record CompactionRecallResult(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<string> ProvidersUsed);