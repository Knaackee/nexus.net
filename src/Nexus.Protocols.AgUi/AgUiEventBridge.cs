using System.Runtime.CompilerServices;
using System.Text.Json;
using Nexus.Core.Events;
using Nexus.Orchestration;

namespace Nexus.Protocols.AgUi;

/// <summary>
/// Bridges Nexus orchestration events to AG-UI protocol events for frontend consumption.
/// </summary>
public sealed class AgUiEventBridge
{
    /// <summary>
    /// Converts a stream of OrchestrationEvent to AG-UI events.
    /// </summary>
    public static async IAsyncEnumerable<AgUiEvent> BridgeAsync(
        IAsyncEnumerable<OrchestrationEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new AgUiRunStartedEvent();

        await foreach (var evt in events.WithCancellation(ct))
        {
            foreach (var agUiEvent in MapEvent(evt))
            {
                yield return agUiEvent;
            }
        }

        yield return new AgUiRunFinishedEvent();
    }

    private static IEnumerable<AgUiEvent> MapEvent(OrchestrationEvent evt)
    {
        switch (evt)
        {
            case NodeStartedEvent nodeStart:
                yield return new AgUiStepStartedEvent(
                    nodeStart.NodeId.Value.ToString(),
                    nodeStart.NodeId.Value.ToString());
                break;

            case NodeCompletedEvent nodeComplete:
                yield return new AgUiStepFinishedEvent(nodeComplete.NodeId.Value.ToString());
                break;

            case NodeFailedEvent nodeFailed:
                yield return new AgUiErrorEvent(nodeFailed.Error.Message);
                yield return new AgUiStepFinishedEvent(nodeFailed.NodeId.Value.ToString());
                break;

            case AgentEventInGraph agentEvt:
                foreach (var mapped in MapAgentEvent(agentEvt.InnerEvent))
                    yield return mapped;
                break;

            case OrchestrationCompletedEvent completed:
                // Final event - the RunFinished will be emitted by BridgeAsync
                break;
        }
    }

    private static IEnumerable<AgUiEvent> MapAgentEvent(AgentEvent agentEvent)
    {
        switch (agentEvent)
        {
            case TextChunkEvent textChunk:
                yield return new AgUiTextChunkEvent(textChunk.Text);
                break;

            case ReasoningChunkEvent reasoningChunk:
                yield return new AgUiReasoningChunkEvent(reasoningChunk.Text);
                break;

            case ToolCallStartedEvent toolStart:
                yield return new AgUiToolCallStartEvent(
                    toolStart.ToolCallId,
                    toolStart.ToolName,
                    toolStart.Arguments);
                break;

            case ToolCallCompletedEvent toolComplete:
                var resultJson = toolComplete.Result is not null
                    ? JsonSerializer.SerializeToElement(toolComplete.Result)
                    : JsonSerializer.SerializeToElement<object?>(null);
                yield return new AgUiToolCallEndEvent(toolComplete.ToolCallId, resultJson);
                break;

            case ApprovalRequestedEvent approvalRequested:
                yield return new AgUiApprovalRequestedEvent(approvalRequested.ApprovalId, approvalRequested.Description);
                break;

            case UserInputRequestedEvent userInputRequested:
                yield return new AgUiUserInputRequestEvent(userInputRequested.RequestId, userInputRequested.Request);
                break;
        }
    }
}
