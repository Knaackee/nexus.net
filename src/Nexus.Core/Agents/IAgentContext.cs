using Microsoft.Extensions.AI;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;

namespace Nexus.Core.Agents;

public interface IAgentContext
{
    IAgent Agent { get; }
    IChatClient GetChatClient(string? name = null);
    IToolRegistry Tools { get; }
    IConversationStore? Conversations { get; }
    IWorkingMemory? WorkingMemory { get; }
    IMessageBus? MessageBus { get; }
    IApprovalGate? ApprovalGate { get; }
    IBudgetTracker? Budget { get; }
    ISecretProvider? Secrets { get; }
    CorrelationContext Correlation { get; }
    Task<IAgent> SpawnChildAsync(AgentDefinition definition, CancellationToken ct = default);
}
