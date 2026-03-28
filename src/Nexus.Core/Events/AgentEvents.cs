using System.Text.Json;
using Nexus.Core.Agents;
using Nexus.Core.Tools;

namespace Nexus.Core.Events;

// Base
public abstract record AgentEvent(AgentId AgentId, DateTimeOffset Timestamp)
{
    protected AgentEvent(AgentId agentId) : this(agentId, DateTimeOffset.UtcNow) { }
}

// LLM Streaming
public record TextChunkEvent(AgentId AgentId, string Text)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

public record ReasoningChunkEvent(AgentId AgentId, string Text)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

// Tool Lifecycle
public record ToolCallStartedEvent(AgentId AgentId, string ToolCallId, string ToolName, JsonElement Arguments)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

public record ToolCallProgressEvent(AgentId AgentId, string ToolCallId, string Message, double? Progress)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

public record ToolCallCompletedEvent(AgentId AgentId, string ToolCallId, ToolResult Result)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

// Agent Lifecycle
public record AgentStateChangedEvent(AgentId AgentId, AgentState OldState, AgentState NewState)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

public record AgentIterationEvent(AgentId AgentId, int Iteration, int MaxIterations)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

// Human-in-the-Loop
public record ApprovalRequestedEvent(AgentId AgentId, string ApprovalId, string Description)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

// Sub-Agents
public record SubAgentSpawnedEvent(AgentId AgentId, AgentId ChildAgentId, string ChildName)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

// Cost
public record TokenUsageEvent(AgentId AgentId, int InputTokens, int OutputTokens, decimal? EstimatedCost)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

// Completion
public record AgentCompletedEvent(AgentId AgentId, AgentResult Result)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);

public record AgentFailedEvent(AgentId AgentId, Exception Error)
    : AgentEvent(AgentId, DateTimeOffset.UtcNow);
