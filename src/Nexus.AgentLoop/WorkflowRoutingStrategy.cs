using System.Text.Json;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Workflows.Dsl;

namespace Nexus.AgentLoop;

public sealed class WorkflowRoutingStrategy : IRoutingStrategy
{
    private readonly WorkflowDefinition _workflow;
    private readonly Dictionary<string, NodeDefinition> _nodesById;

    public WorkflowRoutingStrategy(WorkflowDefinition workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _nodesById = workflow.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask<RoutingDecision> NextAsync(RoutingContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_workflow.Nodes.Count == 0)
            return new StopRoutingDecision(LoopStopReason.AgentCompleted, "Workflow has no nodes.");

        if (context.PreviousStep is not null
            && _nodesById.TryGetValue(context.PreviousStep.StepId, out var previousNode)
            && previousNode.RequiresApproval)
        {
            var approval = await RequestStepApprovalAsync(previousNode, context.PreviousStep, context.ApprovalGate, ct).ConfigureAwait(false);
            if (!approval.IsApproved)
                return new StopRoutingDecision(LoopStopReason.StepRejected, $"Workflow step '{previousNode.Name}' was rejected.");

            var nextNodeAfterApproval = FindNextReadyNode(context.CompletedSteps, context.PreviousStep.StepId, ExtractModifiedText(approval.ModifiedContext));
            if (nextNodeAfterApproval is null)
                return new StopRoutingDecision(LoopStopReason.AgentCompleted, context.PreviousStep.Result.Text);

            return BuildRunDecision(nextNodeAfterApproval, context, ExtractModifiedText(approval.ModifiedContext));
        }

        var nextNode = FindNextReadyNode(context.CompletedSteps, context.PreviousStep?.StepId, modifiedPreviousText: null);
        if (nextNode is null)
        {
            var finalText = context.PreviousStep?.Result.Text ?? "Workflow completed.";
            return new StopRoutingDecision(LoopStopReason.AgentCompleted, finalText);
        }

        return BuildRunDecision(nextNode, context, modifiedPreviousText: null);
    }

    private static async Task<ApprovalResult> RequestStepApprovalAsync(
        NodeDefinition node,
        RoutingStepResult previousStep,
        IApprovalGate? approvalGate,
        CancellationToken ct)
    {
        if (approvalGate is null)
            return new ApprovalResult(true, "no-approval-gate");

        var context = JsonSerializer.SerializeToElement(new
        {
            StepId = previousStep.StepId,
            StepName = previousStep.StepName,
            Output = previousStep.Result.Text,
            Status = previousStep.Result.Status.ToString(),
        });

        return await approvalGate.RequestApprovalAsync(
            new ApprovalRequest($"Approve workflow step '{node.Name}' before continuing.", previousStep.AgentId, node.Id, context),
            ct: ct).ConfigureAwait(false);
    }

    private NodeDefinition? FindNextReadyNode(
        IReadOnlyDictionary<string, AgentResult> completedSteps,
        string? previousStepId,
        string? modifiedPreviousText)
    {
        foreach (var node in _workflow.Nodes)
        {
            if (completedSteps.ContainsKey(node.Id))
                continue;

            var incomingEdges = _workflow.Edges.Where(edge => string.Equals(edge.To, node.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (incomingEdges.Count == 0)
                return node;

            if (incomingEdges.All(edge => EdgeSatisfied(edge, completedSteps, previousStepId, modifiedPreviousText)))
                return node;
        }

        return null;
    }

    private static bool EdgeSatisfied(
        EdgeDefinition edge,
        IReadOnlyDictionary<string, AgentResult> completedSteps,
        string? previousStepId,
        string? modifiedPreviousText)
    {
        if (!completedSteps.TryGetValue(edge.From, out var result))
            return false;

        if (string.IsNullOrWhiteSpace(edge.Condition))
            return true;

        var status = result.Status;
        var condition = edge.Condition.Trim().ToLowerInvariant();
        return condition switch
        {
            "success" or "succeeded" => status == AgentResultStatus.Success,
            "failed" or "failure" or "error" => status == AgentResultStatus.Failed,
            "has-output" => !string.IsNullOrWhiteSpace(edge.From == previousStepId ? modifiedPreviousText ?? result.Text : result.Text),
            _ => true,
        };
    }

    private static RunAgentRoutingDecision BuildRunDecision(NodeDefinition node, RoutingContext context, string? modifiedPreviousText)
    {
        var previousText = modifiedPreviousText ?? context.PreviousStep?.Result.Text;
        var input = BuildInputText(node, context.Options.UserInput, previousText);
        return new RunAgentRoutingDecision(node.Id, node.Name, MapAgentDefinition(node), input);
    }

    private static string BuildInputText(NodeDefinition node, string? originalInput, string? previousText)
    {
        var description = node.Description;
        if (!string.IsNullOrWhiteSpace(originalInput))
            description = description.Replace("{input}", originalInput, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(previousText))
            description = description.Replace("{previous}", previousText, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(previousText) && !node.Description.Contains("{previous}", StringComparison.OrdinalIgnoreCase))
            return $"{description}\n\nPrevious step output:\n{previousText}";

        if (!string.IsNullOrWhiteSpace(previousText))
            return description;

        if (!string.IsNullOrWhiteSpace(originalInput) && !node.Description.Contains("{input}", StringComparison.OrdinalIgnoreCase))
            return $"{description}\n\nOriginal user request:\n{originalInput}";

        return description;
    }

    private static string? ExtractModifiedText(JsonElement? modifiedContext)
    {
        if (modifiedContext is null)
            return null;

        return modifiedContext.Value.ValueKind == JsonValueKind.String
            ? modifiedContext.Value.GetString()
            : modifiedContext.Value.ToString();
    }

    private static AgentDefinition MapAgentDefinition(NodeDefinition node)
    {
        var budget = node.Agent.Budget;
        var contextWindow = node.Agent.ContextWindow;

        return new AgentDefinition
        {
            Name = string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name,
            SystemPrompt = node.Agent.SystemPrompt,
            ModelId = node.Agent.ModelId,
            ChatClientName = node.Agent.ChatClient,
            ToolNames = node.Agent.Tools,
            Budget = budget is null ? null : new AgentBudget
            {
                MaxInputTokens = budget.MaxInputTokens,
                MaxOutputTokens = budget.MaxOutputTokens,
                MaxCostUsd = budget.MaxCostUsd,
                MaxIterations = budget.MaxIterations,
                MaxToolCalls = budget.MaxToolCalls,
            },
            ContextWindow = contextWindow is null ? null : new ContextWindowOptions
            {
                MaxTokens = contextWindow.MaxTokens,
                TargetTokens = contextWindow.TargetTokens,
                ReservedForOutput = contextWindow.ReservedForOutput,
            },
        };
    }
}