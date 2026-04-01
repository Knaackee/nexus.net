using Nexus.Core.Agents;
using Nexus.Core.Contracts;

namespace Nexus.AgentLoop;

public interface IRoutingStrategy
{
    ValueTask<RoutingDecision> NextAsync(RoutingContext context, CancellationToken ct = default);
}

public sealed record RoutingContext
{
    public required AgentLoopOptions Options { get; init; }
    public required IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> History { get; init; }
    public required IReadOnlyDictionary<string, AgentResult> CompletedSteps { get; init; }
    public RoutingStepResult? PreviousStep { get; init; }
    public IApprovalGate? ApprovalGate { get; init; }
}

public sealed record RoutingStepResult(string StepId, string StepName, AgentId AgentId, AgentDefinition AgentDefinition, AgentResult Result);

public abstract record RoutingDecision;

public sealed record RunAgentRoutingDecision(string StepId, string StepName, AgentDefinition AgentDefinition, string InputText)
    : RoutingDecision;

public sealed record StopRoutingDecision(LoopStopReason Reason, string? Message = null)
    : RoutingDecision;