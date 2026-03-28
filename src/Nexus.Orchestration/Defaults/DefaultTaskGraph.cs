using Nexus.Core.Agents;

namespace Nexus.Orchestration.Defaults;

public sealed class DefaultTaskGraph : ITaskGraph
{
    private readonly List<DefaultTaskNode> _nodes = [];

    public TaskGraphId Id { get; } = TaskGraphId.New();
    public IReadOnlyList<ITaskNode> Nodes => _nodes;

    public ITaskNode AddTask(AgentTask task)
    {
        var node = new DefaultTaskNode(task);
        _nodes.Add(node);
        return node;
    }

    public void AddDependency(ITaskNode source, ITaskNode target) =>
        AddDependency(source, target, null);

    public void AddDependency(ITaskNode source, ITaskNode target, EdgeOptions? options)
    {
        var src = (DefaultTaskNode)source;
        var tgt = (DefaultTaskNode)target;
        src.AddDependant(tgt);
        tgt.AddDependency(src);
        if (options is not null)
            tgt.EdgeOptionsMap[src.TaskId] = options;
    }

    public void AddConditionalEdge(ITaskNode source, ITaskNode target, Func<AgentResult, bool> condition)
    {
        var src = (DefaultTaskNode)source;
        var tgt = (DefaultTaskNode)target;
        src.AddDependant(tgt);
        tgt.AddDependency(src);
        tgt.ConditionMap[src.TaskId] = condition;
    }

    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (_nodes.Count == 0)
        {
            errors.Add("Graph has no nodes.");
            return new ValidationResult(false, errors);
        }

        // Check for cycles using DFS
        var visited = new HashSet<TaskId>();
        var inStack = new HashSet<TaskId>();

        foreach (var node in _nodes)
        {
            if (HasCycle(node, visited, inStack))
            {
                errors.Add("Graph contains a cycle.");
                break;
            }
        }

        return errors.Count == 0 ? ValidationResult.Success : new ValidationResult(false, errors);
    }

    private static bool HasCycle(DefaultTaskNode node, HashSet<TaskId> visited, HashSet<TaskId> inStack)
    {
        if (inStack.Contains(node.TaskId))
            return true;
        if (visited.Contains(node.TaskId))
            return false;

        visited.Add(node.TaskId);
        inStack.Add(node.TaskId);

        foreach (var dep in node.Dependants)
        {
            if (HasCycle((DefaultTaskNode)dep, visited, inStack))
                return true;
        }

        inStack.Remove(node.TaskId);
        return false;
    }

    internal IReadOnlyList<ITaskNode> GetReadyNodes(ISet<TaskId> completedIds)
    {
        return _nodes
            .Where(n => !completedIds.Contains(n.TaskId) &&
                        n.Dependencies.All(d => completedIds.Contains(d.TaskId)))
            .ToList<ITaskNode>();
    }
}

internal sealed class DefaultTaskNode(AgentTask task) : ITaskNode
{
    private readonly List<ITaskNode> _dependencies = [];
    private readonly List<ITaskNode> _dependants = [];

    public TaskId TaskId => task.Id;
    public AgentTask Task => task;
    public IReadOnlyList<ITaskNode> Dependencies => _dependencies;
    public IReadOnlyList<ITaskNode> Dependants => _dependants;

    internal Dictionary<TaskId, EdgeOptions> EdgeOptionsMap { get; } = new();
    internal Dictionary<TaskId, Func<AgentResult, bool>> ConditionMap { get; } = new();

    internal void AddDependency(ITaskNode node) => _dependencies.Add(node);
    internal void AddDependant(ITaskNode node) => _dependants.Add(node);
}
