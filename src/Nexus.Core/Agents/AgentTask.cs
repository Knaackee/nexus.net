namespace Nexus.Core.Agents;

public record AgentTask
{
    public required TaskId Id { get; init; }
    public required string Description { get; init; }
    public AgentId? AssignedAgent { get; init; }
    public AgentDefinition? AgentDefinition { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    public TaskErrorPolicy? ErrorPolicy { get; init; }

    public static AgentTask Create(string description) => new()
    {
        Id = TaskId.New(),
        Description = description
    };
}
