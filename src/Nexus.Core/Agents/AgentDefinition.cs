using Nexus.Core.Contracts;
using Nexus.Core.Tools;

namespace Nexus.Core.Agents;

public record AgentDefinition
{
    public required string Name { get; init; }
    public string? Role { get; init; }
    public string? SystemPrompt { get; init; }
    public string? ModelId { get; init; }
    public string? ChatClientName { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    public IReadOnlyList<McpServerConfig> McpServers { get; init; } = [];
    public AgentBudget? Budget { get; init; }
    public TimeSpan? Timeout { get; init; }
    public TaskErrorPolicy? ErrorPolicy { get; init; }
    public ContextWindowOptions? ContextWindow { get; init; }
}

public record McpServerConfig
{
    public required string Name { get; init; }
    public string? Command { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }
    public Uri? Endpoint { get; init; }
    public ToolFilter? AllowedTools { get; init; }
}

public record ToolFilter
{
    public IReadOnlyList<string>? Include { get; init; }
    public IReadOnlyList<string>? Exclude { get; init; }
}

public record ContextWindowOptions
{
    public int MaxTokens { get; init; } = 128_000;
    public int TargetTokens { get; init; } = 100_000;
    public ContextTrimStrategy TrimStrategy { get; init; } = ContextTrimStrategy.SlidingWindow;
    public int ReservedForOutput { get; init; } = 8_000;
    public int ReservedForTools { get; init; } = 4_000;
}

public enum ContextTrimStrategy
{
    SlidingWindow,
    SummarizeAndTruncate,
    KeepFirstAndLast,
    TokenBudget
}
