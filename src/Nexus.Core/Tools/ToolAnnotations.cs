namespace Nexus.Core.Tools;

public record ToolAnnotations
{
    public bool IsReadOnly { get; init; }
    public bool IsIdempotent { get; init; }
    public bool IsDestructive { get; init; }
    public bool IsOpenWorld { get; init; }
    public bool RequiresApproval { get; init; }
    public TimeSpan? EstimatedDuration { get; init; }
    public ToolCostCategory CostCategory { get; init; } = ToolCostCategory.Free;
}

public enum ToolCostCategory
{
    Free,
    Low,
    Medium,
    High,
    RequiresBudgetApproval
}
