namespace Nexus.Orchestration;

public record OrchestrationOptions
{
    public string? ThreadId { get; init; }
    public ICheckpointStore? CheckpointStore { get; init; }
    public CheckpointStrategy CheckpointStrategy { get; init; } = CheckpointStrategy.AfterEachNode;
    public int MaxConcurrentNodes { get; init; } = 10;
    public TimeSpan GlobalTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public decimal? MaxTotalCostUsd { get; init; }
}

public enum CheckpointStrategy { None, AfterEachNode, OnError, Manual }

public record HierarchyOptions
{
    public int MaxDepth { get; init; } = 5;
    public int MaxChildAgents { get; init; } = 10;
    public TimeSpan ChildTimeout { get; init; } = TimeSpan.FromMinutes(10);
}
