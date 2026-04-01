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

    internal TaskNodeSchedulingPlan CreateSchedulingPlan(
        ISet<TaskId> terminalIds,
        IReadOnlyDictionary<TaskId, AgentResult> completedResults,
        ISet<TaskId> skippedIds)
    {
        var ready = new List<ITaskNode>();
        var skipped = new List<(ITaskNode Node, string Reason)>();

        foreach (var node in _nodes)
        {
            if (terminalIds.Contains(node.TaskId))
                continue;

            if (node.Dependencies.Count == 0)
            {
                ready.Add(node);
                continue;
            }

            if (node.Dependencies.Any(d => !terminalIds.Contains(d.TaskId)))
                continue;

            var taskNode = (DefaultTaskNode)node;
            var activeIncomingEdges = 0;

            foreach (var dependency in node.Dependencies)
            {
                if (skippedIds.Contains(dependency.TaskId))
                    continue;

                if (taskNode.ConditionMap.TryGetValue(dependency.TaskId, out var condition))
                {
                    if (completedResults.TryGetValue(dependency.TaskId, out var dependencyResult) && condition(dependencyResult))
                        activeIncomingEdges++;

                    continue;
                }

                activeIncomingEdges++;
            }

            if (activeIncomingEdges == 0)
            {
                skipped.Add((node, "No incoming edge conditions matched."));
                continue;
            }

            ready.Add(node);
        }

        return new TaskNodeSchedulingPlan(ready, skipped);
    }
}

internal sealed record TaskNodeSchedulingPlan(
    IReadOnlyList<ITaskNode> ReadyNodes,
    IReadOnlyList<(ITaskNode Node, string Reason)> SkippedNodes);

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
