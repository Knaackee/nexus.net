using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.Orchestration;

namespace Nexus.Tools.Standard;

public sealed class AgentTool : ITool
{
    private readonly IAgentPool _agentPool;
    private readonly IServiceProvider _services;

    public AgentTool(IAgentPool agentPool, IServiceProvider services)
    {
        _agentPool = agentPool;
        _services = services;
    }

    public string Name => "agent";

    public string Description => "Spawns one or more sub-agents and returns their results as tool output.";

    public ToolAnnotations? Annotations => new()
    {
        IsReadOnly = false,
        RequiresApproval = false,
        IsIdempotent = false,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var requests = ParseRequests(input);
            if (requests.Count == 1)
            {
                var single = await ExecuteRequestAsync(requests[0], context, ct).ConfigureAwait(false);
                if (!single.IsSuccess)
                    return ToolResult.Failure(single.Text ?? "Sub-agent execution failed.");

                return ToolResult.Success(new AgentToolResult(single.Text, single.Status, single.EstimatedCost));
            }

            var maxConcurrency = Math.Max(1, ToolJson.GetOptionalInt(input, "maxConcurrency") ?? requests.Count);
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = requests.Select(async request =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await ExecuteRequestAsync(request, context, ct).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var completedCount = results.Count(result => result.IsSuccess);
            var failedCount = results.Length - completedCount;
            var totalCost = results.Sum(result => result.EstimatedCost ?? 0m);
            var summary = string.Join(Environment.NewLine + Environment.NewLine, results.Select(FormatSummary));

            return ToolResult.Success(new AgentBatchToolResult(
                results,
                failedCount == 0 ? "Success" : completedCount == 0 ? "Failed" : "PartialSuccess",
                summary,
                results.Any(result => result.EstimatedCost.HasValue) ? totalCost : null,
                completedCount,
                failedCount));
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }

    private async Task<AgentToolInvocationResult> ExecuteRequestAsync(AgentToolRequest request, IToolContext context, CancellationToken ct)
    {
        var definition = new AgentDefinition
        {
            Name = request.Agent,
            SystemPrompt = request.SystemPrompt,
            ModelId = request.ModelId,
            ChatClientName = request.ChatClientName,
            ToolNames = request.ToolNames,
        };

        var agent = await _agentPool.SpawnAsync(definition, ct).ConfigureAwait(false);
        try
        {
            if (context.Tools is DefaultToolRegistry registry && definition.ToolNames.Count > 0)
                registry.BindToolsToAgent(agent.Id, definition.ToolNames);

            var result = await agent.ExecuteAsync(AgentTask.Create(request.Task), new ToolInvokedAgentContext(agent, context, _services), ct).ConfigureAwait(false);
            return new AgentToolInvocationResult(
                request.Agent,
                request.Task,
                result.Status.ToString(),
                result.Text,
                result.EstimatedCost,
                result.Status == AgentResultStatus.Success);
        }
        catch (Exception ex)
        {
            return new AgentToolInvocationResult(
                request.Agent,
                request.Task,
                AgentResultStatus.Failed.ToString(),
                ex.Message,
                null,
                false);
        }
        finally
        {
            await _agentPool.KillAsync(agent.Id, ct).ConfigureAwait(false);
        }
    }

    private static List<AgentToolRequest> ParseRequests(JsonElement input)
    {
        if (input.TryGetProperty("tasks", out var tasksProperty) && tasksProperty.ValueKind == JsonValueKind.Array)
        {
            var requests = new List<AgentToolRequest>();
            foreach (var item in tasksProperty.EnumerateArray())
                requests.Add(ParseRequest(item, input));

            if (requests.Count == 0)
                throw new InvalidOperationException("Property 'tasks' must contain at least one entry.");

            return requests;
        }

        return [ParseRequest(input, input)];
    }

    private static AgentToolRequest ParseRequest(JsonElement item, JsonElement defaults)
        => new(
            ToolJson.GetOptionalString(item, "agent") ?? ToolJson.GetOptionalString(defaults, "agent") ?? "SubAgent",
            ToolJson.GetRequiredString(item, "task"),
            ToolJson.GetOptionalString(item, "systemPrompt") ?? ToolJson.GetOptionalString(defaults, "systemPrompt"),
            ToolJson.GetOptionalString(item, "modelId") ?? ToolJson.GetOptionalString(defaults, "modelId"),
            ToolJson.GetOptionalString(item, "chatClientName") ?? ToolJson.GetOptionalString(defaults, "chatClientName"),
            MergeToolNames(item, defaults));

    private static IReadOnlyList<string> MergeToolNames(JsonElement item, JsonElement defaults)
    {
        var requestTools = ToolJson.GetOptionalStringArray(item, "toolNames");
        return requestTools.Count > 0 ? requestTools : ToolJson.GetOptionalStringArray(defaults, "toolNames");
    }

    private static string FormatSummary(AgentToolInvocationResult result)
        => $"[{result.Status}] {result.Agent}: {result.Task}{Environment.NewLine}{result.Text}";

    private sealed record AgentToolRequest(
        string Agent,
        string Task,
        string? SystemPrompt,
        string? ModelId,
        string? ChatClientName,
        IReadOnlyList<string> ToolNames);

    private sealed class ToolInvokedAgentContext : IAgentContext
    {
        private readonly IToolContext _toolContext;
        private readonly IServiceProvider _services;

        public ToolInvokedAgentContext(IAgent agent, IToolContext toolContext, IServiceProvider services)
        {
            Agent = agent;
            _toolContext = toolContext;
            _services = services;
        }

        public IAgent Agent { get; }

        public IChatClient GetChatClient(string? name = null)
            => name is not null
                ? _services.GetRequiredKeyedService<IChatClient>(name)
                : _services.GetRequiredService<IChatClient>();

        public IToolRegistry Tools => _toolContext.Tools;
        public IConversationStore? Conversations => _services.GetService<IConversationStore>();
        public IWorkingMemory? WorkingMemory => _services.GetService<IWorkingMemory>();
        public IMessageBus? MessageBus => _services.GetService<IMessageBus>();
        public IApprovalGate? ApprovalGate => _services.GetService<IApprovalGate>();
        public IBudgetTracker? Budget => _toolContext.Budget;
        public ISecretProvider? Secrets => _toolContext.Secrets;
        public CorrelationContext Correlation => _toolContext.Correlation.CreateChild();

        public Task<IAgent> SpawnChildAsync(AgentDefinition definition, CancellationToken ct = default)
            => _services.GetRequiredService<IAgentPool>().SpawnAsync(definition, ct);
    }
}