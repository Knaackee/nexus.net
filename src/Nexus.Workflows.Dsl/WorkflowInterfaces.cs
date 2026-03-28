using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nexus.Workflows.Dsl;

/// <summary>Loads workflow definitions from various sources.</summary>
public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadFromFileAsync(string path, CancellationToken ct = default);
    Task<WorkflowDefinition> LoadFromStreamAsync(Stream stream, string format = "json", CancellationToken ct = default);
    WorkflowDefinition LoadFromString(string content, string format = "json");
}

/// <summary>Serializes workflow definitions to JSON/YAML.</summary>
public interface IWorkflowSerializer
{
    string Serialize(WorkflowDefinition definition, string format = "json");
    Task SerializeToFileAsync(WorkflowDefinition definition, string path, string format = "json", CancellationToken ct = default);
}

/// <summary>Validates a workflow definition.</summary>
public interface IWorkflowValidator
{
    ValidationResult Validate(WorkflowDefinition definition);
}

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok() => new(true, []);
    public static ValidationResult Fail(params string[] errors) => new(false, errors);
}

/// <summary>Resolves ${variable} references in strings.</summary>
public interface IVariableResolver
{
    string Resolve(string template, IReadOnlyDictionary<string, object> variables);
}

/// <summary>Evaluates edge condition expressions.</summary>
public interface IConditionEvaluator
{
    bool Evaluate(string expression, Nexus.Core.Agents.AgentResult result);
}

/// <summary>Registry for custom agent types.</summary>
public interface IAgentTypeRegistry
{
    void Register(string typeName, Func<AgentConfig, IServiceProvider, Nexus.Core.Agents.IAgent> factory);
    Nexus.Core.Agents.IAgent Create(AgentConfig config, IServiceProvider sp);
}

/// <summary>Default loader supporting JSON and YAML.</summary>
public sealed class DefaultWorkflowLoader : IWorkflowLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public async Task<WorkflowDefinition> LoadFromFileAsync(string path, CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (format is "yml") format = "yaml";
        return LoadFromString(content, format);
    }

    public async Task<WorkflowDefinition> LoadFromStreamAsync(Stream stream, string format = "json", CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return LoadFromString(content, format);
    }

    public WorkflowDefinition LoadFromString(string content, string format = "json")
    {
        return format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Deserialize<WorkflowDefinition>(content, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize JSON workflow."),
            "yaml" or "yml" => DeserializeYaml(content),
            _ => throw new ArgumentException($"Unsupported format: {format}"),
        };
    }

    private static WorkflowDefinition DeserializeYaml(string yaml)
    {
        // Convert YAML to JSON, then deserialize
        var yamlObject = YamlDeserializer.Deserialize<object>(yaml);
        var json = JsonSerializer.Serialize(yamlObject, JsonOptions);
        return JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize YAML workflow.");
    }
}

/// <summary>Default variable resolver replacing ${variable} references.</summary>
public sealed class DefaultVariableResolver : IVariableResolver
{
    public string Resolve(string template, IReadOnlyDictionary<string, object> variables)
    {
        if (string.IsNullOrEmpty(template)) return template;

        foreach (var (key, value) in variables)
        {
            template = template.Replace($"${{{key}}}", value?.ToString() ?? "", StringComparison.Ordinal);
        }

        return template;
    }
}

/// <summary>Validates workflow definitions for common errors.</summary>
public sealed class DefaultWorkflowValidator : IWorkflowValidator
{
    public ValidationResult Validate(WorkflowDefinition definition)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Id))
            errors.Add("Workflow ID is required.");

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("Workflow name is required.");

        if (definition.Nodes.Count == 0)
            errors.Add("Workflow must have at least one node.");

        // Check for duplicate node IDs
        var nodeIds = new HashSet<string>();
        foreach (var node in definition.Nodes)
        {
            if (!nodeIds.Add(node.Id))
                errors.Add($"Duplicate node ID: '{node.Id}'.");
        }

        // Check edges reference existing nodes
        foreach (var edge in definition.Edges)
        {
            if (!nodeIds.Contains(edge.From))
                errors.Add($"Edge references non-existing source node: '{edge.From}'.");
            if (!nodeIds.Contains(edge.To))
                errors.Add($"Edge references non-existing target node: '{edge.To}'.");
            if (edge.From == edge.To)
                errors.Add($"Self-referencing edge: '{edge.From}'.");
        }

        // Check for cycles using DFS
        if (HasCycle(definition.Nodes, definition.Edges))
            errors.Add("Workflow graph contains a cycle.");

        // Validate budgets are positive
        foreach (var node in definition.Nodes)
        {
            if (node.Agent.Budget?.MaxCostUsd < 0)
                errors.Add($"Node '{node.Id}' has negative budget.");
            if (node.Agent.Budget?.MaxIterations < 0)
                errors.Add($"Node '{node.Id}' has negative max iterations.");
        }

        return errors.Count == 0 ? ValidationResult.Ok() : new ValidationResult(false, errors);
    }

    private static bool HasCycle(IReadOnlyList<NodeDefinition> nodes, IReadOnlyList<EdgeDefinition> edges)
    {
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var node in nodes)
            adjacency[node.Id] = [];
        foreach (var edge in edges)
        {
            if (adjacency.TryGetValue(edge.From, out var list))
                list.Add(edge.To);
        }

        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();

        foreach (var node in nodes)
        {
            if (DfsCycleDetect(node.Id, adjacency, visited, inStack))
                return true;
        }

        return false;
    }

    private static bool DfsCycleDetect(
        string nodeId,
        Dictionary<string, List<string>> adjacency,
        HashSet<string> visited,
        HashSet<string> inStack)
    {
        if (inStack.Contains(nodeId)) return true;
        if (visited.Contains(nodeId)) return false;

        visited.Add(nodeId);
        inStack.Add(nodeId);

        if (adjacency.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (DfsCycleDetect(neighbor, adjacency, visited, inStack))
                    return true;
            }
        }

        inStack.Remove(nodeId);
        return false;
    }
}

/// <summary>Simple condition evaluator for common expressions.</summary>
public sealed class SimpleConditionEvaluator : IConditionEvaluator
{
    public bool Evaluate(string expression, Nexus.Core.Agents.AgentResult result)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;

        // Support basic patterns:
        // "result.status == 'Success'"
        // "result.text.contains('approved')"
        var trimmed = expression.Trim();

        if (trimmed.Contains("result.status", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.Contains("'Success'", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("\"Success\"", StringComparison.OrdinalIgnoreCase))
                return result.Status == Core.Agents.AgentResultStatus.Success;
            if (trimmed.Contains("'Failed'", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("\"Failed\"", StringComparison.OrdinalIgnoreCase))
                return result.Status == Core.Agents.AgentResultStatus.Failed;
        }

        if (trimmed.Contains("result.text.contains(", StringComparison.OrdinalIgnoreCase))
        {
            var start = trimmed.IndexOf('\'') + 1;
            var end = trimmed.LastIndexOf('\'');
            if (start > 0 && end > start)
            {
                var needle = trimmed[start..end];
                return result.Text?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;
            }
        }

        // Default: treat as always true for unrecognized expressions
        return true;
    }
}

/// <summary>Registry for custom agent types used in DSL workflows.</summary>
public sealed class DefaultAgentTypeRegistry : IAgentTypeRegistry
{
    private readonly Dictionary<string, Func<AgentConfig, IServiceProvider, Core.Agents.IAgent>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string typeName, Func<AgentConfig, IServiceProvider, Core.Agents.IAgent> factory)
    {
        _factories[typeName] = factory;
    }

    public Core.Agents.IAgent Create(AgentConfig config, IServiceProvider sp)
    {
        if (_factories.TryGetValue(config.Type, out var factory))
            return factory(config, sp);

        throw new InvalidOperationException($"Unknown agent type: '{config.Type}'. Register it via IAgentTypeRegistry.");
    }
}
