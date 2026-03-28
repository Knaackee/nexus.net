using Nexus.Core.Agents;
using Nexus.Core.Contracts;

namespace Nexus.Core.Tools;

public interface IToolContext
{
    AgentId AgentId { get; }
    IToolRegistry Tools { get; }
    ISecretProvider? Secrets { get; }
    IBudgetTracker? Budget { get; }
    CorrelationContext Correlation { get; }
}
