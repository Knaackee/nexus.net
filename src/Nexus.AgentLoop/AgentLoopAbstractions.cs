using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Tools;
using Nexus.Sessions;

namespace Nexus.AgentLoop;

public interface IAgentLoop
{
    IAsyncEnumerable<AgentLoopEvent> RunAsync(AgentLoopOptions options, CancellationToken ct = default);
}

public sealed record AgentLoopOptions
{
    public IAgent? Agent { get; init; }
    public AgentDefinition? AgentDefinition { get; init; }
    public IRoutingStrategy? RoutingStrategy { get; init; }
    public IList<ChatMessage> Messages { get; init; } = [];
    public string? UserInput { get; init; }
    public AgentBudget? Budget { get; init; }
    public int MaxTurns { get; init; } = 50;
    public ContextWindowOptions? ContextWindow { get; init; }
    public Func<AgentResult, bool>? StopWhen { get; init; }
    public SessionId? SessionId { get; init; }
    public bool ResumeLastSession { get; init; }
    public string? SessionTitle { get; init; }
    public IReadOnlyDictionary<string, string> SessionMetadata { get; init; } = new Dictionary<string, string>();
}

public abstract record AgentLoopEvent(SessionId? SessionId, AgentId AgentId, DateTimeOffset Timestamp)
{
    protected AgentLoopEvent(SessionId? sessionId, AgentId agentId) : this(sessionId, agentId, DateTimeOffset.UtcNow) { }
}

public sealed record LoopStartedEvent(SessionId? SessionId, AgentId AgentId, int MessageCount)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record TextChunkLoopEvent(SessionId? SessionId, AgentId AgentId, string Text)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record ReasoningChunkLoopEvent(SessionId? SessionId, AgentId AgentId, string Text)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record ToolCallStartedLoopEvent(SessionId? SessionId, AgentId AgentId, string ToolCallId, string ToolName)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record ToolCallProgressLoopEvent(SessionId? SessionId, AgentId AgentId, string ToolCallId, string Message, double? Progress)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record ToolCallCompletedLoopEvent(SessionId? SessionId, AgentId AgentId, string ToolCallId, ToolResult Result)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record ApprovalRequestedLoopEvent(SessionId? SessionId, AgentId AgentId, string ApprovalId, string Description)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record CompactionTriggeredLoopEvent(SessionId? SessionId, AgentId AgentId, string StrategyUsed, int TokensBefore, int TokensAfter)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record TokenUsageLoopEvent(SessionId? SessionId, AgentId AgentId, int InputTokens, int OutputTokens, decimal? EstimatedCost)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record TurnCompletedLoopEvent(SessionId? SessionId, AgentId AgentId, AgentResult Result)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record LoopCompletedEvent(SessionId? SessionId, AgentId AgentId, LoopStopReason Reason, AgentResult FinalResult)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public sealed record LoopErrorEvent(SessionId? SessionId, AgentId AgentId, Exception Error)
    : AgentLoopEvent(SessionId, AgentId, DateTimeOffset.UtcNow);

public enum LoopStopReason
{
    AgentCompleted,
    MaxTurnsReached,
    BudgetExhausted,
    UserCancelled,
    StepRejected,
    StopConditionMet,
    CompactionFailed,
    Error,
}