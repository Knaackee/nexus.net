using System.Text.Json;
using Nexus.Core.Events;

namespace Nexus.Protocols.AgUi;

/// <summary>Base record for all AG-UI events sent to the frontend.</summary>
public abstract record AgUiEvent(string EventType)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record AgUiRunStartedEvent() : AgUiEvent("RUN_STARTED");
public record AgUiRunFinishedEvent(string? Error = null) : AgUiEvent("RUN_FINISHED");
public record AgUiTextChunkEvent(string Text) : AgUiEvent("TEXT_CHUNK");
public record AgUiReasoningChunkEvent(string Text) : AgUiEvent("REASONING_CHUNK");
public record AgUiToolCallStartEvent(string ToolCallId, string ToolName, JsonElement Arguments) : AgUiEvent("TOOL_CALL_START");
public record AgUiToolCallEndEvent(string ToolCallId, JsonElement Result) : AgUiEvent("TOOL_CALL_END");
public record AgUiApprovalRequestedEvent(string ApprovalId, string Description) : AgUiEvent("APPROVAL_REQUESTED");
public record AgUiUserInputRequestEvent(string RequestId, UserInputRequest Request) : AgUiEvent("USER_INPUT_REQUEST");
public record AgUiStateDeltaEvent(JsonElement Delta) : AgUiEvent("STATE_DELTA");
public record AgUiStateSnapshotEvent(JsonElement State) : AgUiEvent("STATE_SNAPSHOT");
public record AgUiStepStartedEvent(string StepId, string StepName) : AgUiEvent("STEP_STARTED");
public record AgUiStepFinishedEvent(string StepId) : AgUiEvent("STEP_FINISHED");
public record AgUiCustomEvent(string Name, JsonElement Data) : AgUiEvent("CUSTOM");
public record AgUiErrorEvent(string Message) : AgUiEvent("ERROR");
