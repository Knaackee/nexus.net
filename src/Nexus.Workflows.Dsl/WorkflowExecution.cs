using Nexus.Core.Agents;
using Nexus.Orchestration;
using Nexus.Orchestration.Propagators;

namespace Nexus.Workflows.Dsl;

public interface IWorkflowGraphCompiler
{
    CompiledWorkflow Compile(WorkflowDefinition definition, IReadOnlyDictionary<string, object>? variables = null);
}

public interface IWorkflowExecutor
{
    Task<OrchestrationResult> ExecuteAsync(
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object>? variables = null,
        CancellationToken ct = default);
}

public sealed record CompiledWorkflow(ITaskGraph Graph, OrchestrationOptions Options);

public sealed class DefaultWorkflowGraphCompiler : IWorkflowGraphCompiler
{
    private readonly IOrchestrator _orchestrator;
    private readonly IVariableResolver _variableResolver;
    private readonly IConditionEvaluator _conditionEvaluator;

    public DefaultWorkflowGraphCompiler(
        IOrchestrator orchestrator,
        IVariableResolver variableResolver,
        IConditionEvaluator conditionEvaluator)
    {
        _orchestrator = orchestrator;
        _variableResolver = variableResolver;
        _conditionEvaluator = conditionEvaluator;
    }

    public CompiledWorkflow Compile(WorkflowDefinition definition, IReadOnlyDictionary<string, object>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var resolvedVariables = MergeVariables(definition.Variables, variables);
        var graph = _orchestrator.CreateGraph();
        var nodes = new Dictionary<string, ITaskNode>(StringComparer.Ordinal);

        foreach (var node in definition.Nodes)
        {
            var task = BuildTask(node, resolvedVariables);
            nodes[node.Id] = graph.AddTask(task);
        }

        foreach (var edge in definition.Edges)
        {
            var source = nodes[edge.From];
            var target = nodes[edge.To];

            if (!string.IsNullOrWhiteSpace(edge.Condition))
            {
                var expression = _variableResolver.Resolve(edge.Condition, resolvedVariables);
                graph.AddConditionalEdge(source, target, result => _conditionEvaluator.Evaluate(expression, result));
                continue;
            }

            var options = BuildEdgeOptions(edge.ContextPropagation);
            graph.AddDependency(source, target, options);
        }

        return new CompiledWorkflow(graph, BuildOptions(definition.Options));
    }

    private AgentTask BuildTask(NodeDefinition node, IReadOnlyDictionary<string, object> variables)
    {
        var description = _variableResolver.Resolve(node.Description, variables);
        return AgentTask.Create(description) with
        {
            AgentDefinition = new AgentDefinition
            {
                Name = node.Name,
                Role = node.Agent.Type,
                SystemPrompt = ResolveOptional(node.Agent.SystemPrompt, variables),
                ModelId = node.Agent.ModelId,
                ChatClientName = node.Agent.ChatClient,
                ToolNames = node.Agent.Tools,
                Budget = MapBudget(node.Agent.Budget),
                Timeout = node.ErrorPolicy is null ? null : TimeSpan.FromSeconds(node.ErrorPolicy.TimeoutSeconds),
                ErrorPolicy = MapErrorPolicy(node.ErrorPolicy),
                ContextWindow = MapContextWindow(node.Agent.ContextWindow),
            },
            Metadata = BuildMetadata(node),
            ErrorPolicy = MapErrorPolicy(node.ErrorPolicy),
        };
    }

    private static Dictionary<string, object> BuildMetadata(NodeDefinition node)
    {
        var metadata = new Dictionary<string, object>(node.Metadata, StringComparer.Ordinal)
        {
            ["workflowNodeId"] = node.Id,
            ["requiresApproval"] = node.RequiresApproval,
        };

        return metadata;
    }

    private static AgentBudget? MapBudget(BudgetConfig? budget)
        => budget is null
            ? null
            : new AgentBudget(
                budget.MaxInputTokens,
                budget.MaxOutputTokens,
                budget.MaxCostUsd,
                budget.MaxIterations,
                budget.MaxToolCalls);

    private static ContextWindowOptions? MapContextWindow(ContextWindowConfig? contextWindow)
    {
        if (contextWindow is null)
            return null;

        return new ContextWindowOptions
        {
            MaxTokens = contextWindow.MaxTokens,
            TargetTokens = contextWindow.TargetTokens,
            ReservedForOutput = contextWindow.ReservedForOutput,
            TrimStrategy = ParseTrimStrategy(contextWindow.TrimStrategy),
        };
    }

    private static ContextTrimStrategy ParseTrimStrategy(string? strategy)
        => strategy?.Trim().ToLowerInvariant() switch
        {
            "summarizeandtruncate" or "summarize_and_truncate" => ContextTrimStrategy.SummarizeAndTruncate,
            "keepfirstandlast" or "keep_first_and_last" => ContextTrimStrategy.KeepFirstAndLast,
            "tokenbudget" or "token_budget" => ContextTrimStrategy.TokenBudget,
            _ => ContextTrimStrategy.SlidingWindow,
        };

    private static TaskErrorPolicy? MapErrorPolicy(ErrorPolicyConfig? config)
    {
        if (config is null)
            return null;

        return new TaskErrorPolicy
        {
            Retry = config.MaxRetries is null
                ? null
                : new RetryOptions
                {
                    MaxRetries = config.MaxRetries.Value,
                    BackoffType = ParseBackoff(config.BackoffType),
                },
            Fallback = config.FallbackChatClient is null && config.FallbackModelId is null
                ? null
                : new FallbackOptions
                {
                    AlternateChatClientName = config.FallbackChatClient,
                    AlternateModelId = config.FallbackModelId,
                },
            EscalateToHuman = config.EscalateToHuman,
            SendToDeadLetter = config.SendToDeadLetter,
            MaxIterations = config.MaxIterations,
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        };
    }

    private static BackoffType ParseBackoff(string? backoffType)
        => backoffType?.Trim().ToLowerInvariant() switch
        {
            "constant" => BackoffType.Constant,
            "linear" => BackoffType.Linear,
            "exponential" => BackoffType.Exponential,
            _ => BackoffType.ExponentialWithJitter,
        };

    private static EdgeOptions? BuildEdgeOptions(ContextPropagationConfig? contextPropagation)
    {
        if (contextPropagation is null)
            return null;

        return new EdgeOptions
        {
            ContextPropagator = CreatePropagator(contextPropagation),
        };
    }

    private static IContextPropagator CreatePropagator(ContextPropagationConfig config)
        => config.Strategy.Trim().ToLowerInvariant() switch
        {
            "structured" or "structured-only" => new StructuredOnlyPropagator(),
            _ => new FullPassthroughPropagator(),
        };

    private static OrchestrationOptions BuildOptions(WorkflowOptions? options)
        => new()
        {
            MaxConcurrentNodes = options?.MaxConcurrentNodes ?? 10,
            GlobalTimeout = TimeSpan.FromSeconds(options?.GlobalTimeoutSeconds ?? (int)TimeSpan.FromMinutes(30).TotalSeconds),
            MaxTotalCostUsd = options?.MaxTotalCostUsd,
            CheckpointStrategy = ParseCheckpointStrategy(options?.CheckpointStrategy),
        };

    private static CheckpointStrategy ParseCheckpointStrategy(string? strategy)
        => strategy?.Trim().ToLowerInvariant() switch
        {
            "none" => CheckpointStrategy.None,
            "onerror" or "on_error" => CheckpointStrategy.OnError,
            "manual" => CheckpointStrategy.Manual,
            _ => CheckpointStrategy.AfterEachNode,
        };

    private string? ResolveOptional(string? template, IReadOnlyDictionary<string, object> variables)
        => template is null ? null : _variableResolver.Resolve(template, variables);

    private static IReadOnlyDictionary<string, object> MergeVariables(
        IReadOnlyDictionary<string, object> definitionVariables,
        IReadOnlyDictionary<string, object>? runtimeVariables)
    {
        if (runtimeVariables is null || runtimeVariables.Count == 0)
            return definitionVariables;

        var merged = new Dictionary<string, object>(definitionVariables, StringComparer.Ordinal);
        foreach (var pair in runtimeVariables)
            merged[pair.Key] = pair.Value;

        return merged;
    }
}

public sealed class DefaultWorkflowExecutor : IWorkflowExecutor
{
    private readonly IWorkflowValidator _validator;
    private readonly IWorkflowGraphCompiler _compiler;
    private readonly IOrchestrator _orchestrator;

    public DefaultWorkflowExecutor(
        IWorkflowValidator validator,
        IWorkflowGraphCompiler compiler,
        IOrchestrator orchestrator)
    {
        _validator = validator;
        _compiler = compiler;
        _orchestrator = orchestrator;
    }

    public async Task<OrchestrationResult> ExecuteAsync(
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object>? variables = null,
        CancellationToken ct = default)
    {
        var validation = _validator.Validate(definition);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Workflow validation failed: {string.Join(", ", validation.Errors)}");

        var compiled = _compiler.Compile(definition, variables);
        return await _orchestrator.ExecuteGraphAsync(compiled.Graph, compiled.Options, ct).ConfigureAwait(false);
    }
}