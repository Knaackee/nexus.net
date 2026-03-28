# Nexus.Workflows.Dsl API Reference

## Namespace: Nexus.Workflows.Dsl

### WorkflowDefinition

```csharp
public record WorkflowDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Version { get; init; }     // Default: "1.0.0"
    public required IReadOnlyList<NodeDefinition> Nodes { get; init; }
    public IReadOnlyList<EdgeDefinition> Edges { get; init; }
    public IReadOnlyDictionary<string, object> Variables { get; init; }
    public WorkflowOptions? Options { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; }
}
```

### NodeDefinition

```csharp
public record NodeDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public AgentConfig Agent { get; init; }
    public ErrorPolicyConfig? ErrorPolicy { get; init; }
    public NodePosition? Position { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; }
}
```

### AgentConfig

```csharp
public record AgentConfig
{
    public string Type { get; init; }              // Default: "chat"
    public string? SystemPrompt { get; init; }
    public string? ChatClient { get; init; }
    public string? ModelId { get; init; }
    public IReadOnlyList<string> Tools { get; init; }
    public IReadOnlyList<string> McpServers { get; init; }
    public BudgetConfig? Budget { get; init; }
    public ContextWindowConfig? ContextWindow { get; init; }
}
```

### BudgetConfig

```csharp
public record BudgetConfig
{
    public int? MaxInputTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public decimal? MaxCostUsd { get; init; }
    public int? MaxIterations { get; init; }
    public int? MaxToolCalls { get; init; }
}
```

### ContextWindowConfig

```csharp
public record ContextWindowConfig
{
    public int MaxTokens { get; init; }          // Default: 128_000
    public int TargetTokens { get; init; }       // Default: 100_000
    public string TrimStrategy { get; init; }    // Default: "SlidingWindow"
    public int ReservedForOutput { get; init; }  // Default: 8_000
}
```

### ErrorPolicyConfig

```csharp
public record ErrorPolicyConfig
{
    public int? MaxRetries { get; init; }
    public string? BackoffType { get; init; }
    public string? FallbackChatClient { get; init; }
    public string? FallbackModelId { get; init; }
    public bool EscalateToHuman { get; init; }
    public bool SendToDeadLetter { get; init; }
    public int MaxIterations { get; init; }      // Default: 25
    public int TimeoutSeconds { get; init; }     // Default: 300
}
```

### EdgeDefinition

```csharp
public record EdgeDefinition
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string? Condition { get; init; }
    public ContextPropagationConfig? ContextPropagation { get; init; }
}
```

### ContextPropagationConfig

```csharp
public record ContextPropagationConfig
{
    public string Strategy { get; init; }     // Default: "full"
    public int? MaxTokens { get; init; }
    public string? JsonPath { get; init; }
}
```

### WorkflowOptions

```csharp
public record WorkflowOptions
{
    public int? MaxConcurrentNodes { get; init; }
    public int? GlobalTimeoutSeconds { get; init; }
    public decimal? MaxTotalCostUsd { get; init; }
    public string? CheckpointStrategy { get; init; }
}
```

### IWorkflowLoader

```csharp
public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadFromFileAsync(string path, CancellationToken ct = default);
    Task<WorkflowDefinition> LoadFromStreamAsync(Stream stream, string format = "json", CancellationToken ct = default);
    WorkflowDefinition LoadFromString(string content, string format = "json");
}
```

### IWorkflowSerializer

```csharp
public interface IWorkflowSerializer
{
    string Serialize(WorkflowDefinition definition, string format = "json");
    Task SerializeToFileAsync(WorkflowDefinition definition, string path, string format = "json", CancellationToken ct = default);
}
```

### IWorkflowValidator

```csharp
public interface IWorkflowValidator
{
    ValidationResult Validate(WorkflowDefinition definition);
}

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok();
    public static ValidationResult Fail(params string[] errors);
}
```

### IVariableResolver

```csharp
public interface IVariableResolver
{
    string Resolve(string template, IReadOnlyDictionary<string, object> variables);
}
```

### IConditionEvaluator

```csharp
public interface IConditionEvaluator
{
    bool Evaluate(string expression, AgentResult result);
}
```

### IAgentTypeRegistry

```csharp
public interface IAgentTypeRegistry
{
    void Register(string typeName, Func<AgentConfig, IServiceProvider, IAgent> factory);
    IAgent Create(AgentConfig config, IServiceProvider sp);
}
```

## Default Implementations

| Interface | Implementation | Notes |
|-----------|---------------|-------|
| `IWorkflowLoader` | `DefaultWorkflowLoader` | JSON + YAML (via YamlDotNet) |
| `IWorkflowValidator` | `DefaultWorkflowValidator` | Cycle detection, referential integrity |
| `IVariableResolver` | `DefaultVariableResolver` | `${variable}` substitution |
| `IConditionEvaluator` | `SimpleConditionEvaluator` | `result.status == 'Success'`, `result.text.contains('...')` |
| `IAgentTypeRegistry` | `DefaultAgentTypeRegistry` | Factory-based type registration |
