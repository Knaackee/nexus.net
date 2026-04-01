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

    public string Description => "Spawns a sub-agent and returns its result as a tool output.";

    public ToolAnnotations? Annotations => new()
    {
        IsReadOnly = false,
        RequiresApproval = false,
        IsIdempotent = false,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        var task = ToolJson.GetRequiredString(input, "task");
        var agentName = ToolJson.GetOptionalString(input, "agent") ?? "SubAgent";

        var definition = new AgentDefinition
        {
            Name = agentName,
            SystemPrompt = ToolJson.GetOptionalString(input, "systemPrompt"),
            ModelId = ToolJson.GetOptionalString(input, "modelId"),
            ChatClientName = ToolJson.GetOptionalString(input, "chatClientName"),
            ToolNames = ToolJson.GetOptionalStringArray(input, "toolNames"),
        };

        var agent = await _agentPool.SpawnAsync(definition, ct).ConfigureAwait(false);
        try
        {
            if (context.Tools is DefaultToolRegistry registry && definition.ToolNames.Count > 0)
                registry.BindToolsToAgent(agent.Id, definition.ToolNames);

            var result = await agent.ExecuteAsync(AgentTask.Create(task), new ToolInvokedAgentContext(agent, context, _services), ct).ConfigureAwait(false);
            return ToolResult.Success(new AgentToolResult(result.Text, result.Status.ToString(), result.EstimatedCost));
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
        finally
        {
            await _agentPool.KillAsync(agent.Id, ct).ConfigureAwait(false);
        }
    }

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