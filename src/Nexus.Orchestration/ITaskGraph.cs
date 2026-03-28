using Nexus.Core.Agents;

namespace Nexus.Orchestration;

public interface ITaskGraph
{
    TaskGraphId Id { get; }
    ITaskNode AddTask(AgentTask task);
    void AddDependency(ITaskNode source, ITaskNode target);
    void AddDependency(ITaskNode source, ITaskNode target, EdgeOptions? options);
    void AddConditionalEdge(ITaskNode source, ITaskNode target, Func<AgentResult, bool> condition);
    ValidationResult Validate();
    IReadOnlyList<ITaskNode> Nodes { get; }
}

public interface ITaskNode
{
    TaskId TaskId { get; }
    AgentTask Task { get; }
    IReadOnlyList<ITaskNode> Dependencies { get; }
    IReadOnlyList<ITaskNode> Dependants { get; }
}

public record EdgeOptions
{
    public IContextPropagator? ContextPropagator { get; init; }
    public TimeSpan? Timeout { get; init; }
}

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success => new(true, []);
    public static ValidationResult Fail(params string[] errors) => new(false, errors);
}
