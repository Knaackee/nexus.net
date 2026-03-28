using Microsoft.Extensions.AI;
using Nexus.Core.Agents;

namespace Nexus.Core.Tools;

public interface IToolRegistry
{
    void Register(ITool tool);
    void Register(AIFunction function);
    ITool? Resolve(string name);
    IReadOnlyList<ITool> ListAll();
    IReadOnlyList<ITool> ListForAgent(AgentId agentId);
    IReadOnlyList<AIFunction> AsAIFunctions();
}
