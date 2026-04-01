namespace Nexus.Workflows.Dsl;

/// <summary>Complete workflow definition — serializable to JSON/YAML.</summary>
public record WorkflowDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Version { get; init; } = "1.0.0";
    public required IReadOnlyList<NodeDefinition> Nodes { get; init; }
    public IReadOnlyList<EdgeDefinition> Edges { get; init; } = [];
    public IReadOnlyDictionary<string, object> Variables { get; init; } = new Dictionary<string, object>();
    public WorkflowOptions? Options { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

public record NodeDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool RequiresApproval { get; init; }
    public AgentConfig Agent { get; init; } = new();
    public ErrorPolicyConfig? ErrorPolicy { get; init; }
    public NodePosition? Position { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

public record AgentConfig
{
    public string Type { get; init; } = "chat";
    public string? SystemPrompt { get; init; }
    public string? ChatClient { get; init; }
    public string? ModelId { get; init; }
    public IReadOnlyList<string> Tools { get; init; } = [];
    public IReadOnlyList<string> McpServers { get; init; } = [];
    public BudgetConfig? Budget { get; init; }
    public ContextWindowConfig? ContextWindow { get; init; }
}

public record BudgetConfig
{
    public int? MaxInputTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public decimal? MaxCostUsd { get; init; }
    public int? MaxIterations { get; init; }
    public int? MaxToolCalls { get; init; }
}

public record ContextWindowConfig
{
    public int MaxTokens { get; init; } = 128_000;
    public int TargetTokens { get; init; } = 100_000;
    public string TrimStrategy { get; init; } = "SlidingWindow";
    public int ReservedForOutput { get; init; } = 8_000;
}

public record ErrorPolicyConfig
{
    public int? MaxRetries { get; init; }
    public string? BackoffType { get; init; }
    public string? FallbackChatClient { get; init; }
    public string? FallbackModelId { get; init; }
    public bool EscalateToHuman { get; init; }
    public bool SendToDeadLetter { get; init; }
    public int MaxIterations { get; init; } = 25;
    public int TimeoutSeconds { get; init; } = 300;
}

public record EdgeDefinition
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string? Condition { get; init; }
    public ContextPropagationConfig? ContextPropagation { get; init; }
}

public record ContextPropagationConfig
{
    public string Strategy { get; init; } = "full";
    public int? MaxTokens { get; init; }
    public string? JsonPath { get; init; }
}

public record WorkflowOptions
{
    public int? MaxConcurrentNodes { get; init; }
    public int? GlobalTimeoutSeconds { get; init; }
    public decimal? MaxTotalCostUsd { get; init; }
    public string? CheckpointStrategy { get; init; }
}

public record NodePosition(double X, double Y);
