using Microsoft.Extensions.AI;

namespace Nexus.Core.Routing;

public interface IChatClientRouter : IChatClient
{
    void Register(string name, IChatClient client);
    IChatClient Resolve(string? name = null);
}

public enum RoutingStrategy
{
    Named,
    RoundRobin,
    LeastBusy,
    ModelBased,
    CostOptimized,
    Custom
}
