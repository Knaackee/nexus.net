using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;

namespace Nexus.Core.Tools;

public class DefaultToolRegistry : IToolRegistry
{
    public static readonly DefaultToolRegistry Empty = new();

    private readonly ConcurrentDictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<AgentId, List<string>> _agentToolBindings = new();

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
    }

    public void Register(AIFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        _tools[function.Name] = function.AsNexusTool();
    }

    public ITool? Resolve(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    public IReadOnlyList<ITool> ListAll() => _tools.Values.ToList().AsReadOnly();

    public IReadOnlyList<ITool> ListForAgent(AgentId agentId)
    {
        if (_agentToolBindings.TryGetValue(agentId, out var bindings))
        {
            return bindings
                .Select(name => _tools.GetValueOrDefault(name))
                .Where(t => t is not null)
                .ToList()
                .AsReadOnly()!;
        }

        return ListAll();
    }

    public IReadOnlyList<AIFunction> AsAIFunctions()
        => _tools.Values.Select(t => t.AsAIFunction()).ToList().AsReadOnly();

    public void BindToolsToAgent(AgentId agentId, IEnumerable<string> toolNames)
    {
        _agentToolBindings[agentId] = toolNames.ToList();
    }
}
