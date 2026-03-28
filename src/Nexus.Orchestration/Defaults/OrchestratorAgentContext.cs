using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;

namespace Nexus.Orchestration.Defaults;

internal sealed class OrchestratorAgentContext : IAgentContext
{
    private readonly IServiceProvider _services;

    public OrchestratorAgentContext(IAgent agent, IServiceProvider services)
    {
        Agent = agent;
        _services = services;
    }

    public IAgent Agent { get; }

    public IChatClient GetChatClient(string? name = null)
    {
        return name is not null
            ? _services.GetRequiredKeyedService<IChatClient>(name)
            : _services.GetRequiredService<IChatClient>();
    }

    public IToolRegistry Tools => _services.GetRequiredService<IToolRegistry>();
    public IConversationStore? Conversations => _services.GetService<IConversationStore>();
    public IWorkingMemory? WorkingMemory => _services.GetService<IWorkingMemory>();
    public IMessageBus? MessageBus => _services.GetService<IMessageBus>();
    public IApprovalGate? ApprovalGate => _services.GetService<IApprovalGate>();
    public IBudgetTracker? Budget => _services.GetService<IBudgetTracker>();
    public ISecretProvider? Secrets => _services.GetService<ISecretProvider>();

    public CorrelationContext Correlation { get; } = CorrelationContext.New();

    public async Task<IAgent> SpawnChildAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var pool = _services.GetRequiredService<IAgentPool>();
        return await pool.SpawnAsync(definition, ct).ConfigureAwait(false);
    }
}
