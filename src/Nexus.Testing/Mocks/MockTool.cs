using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Testing.Mocks;

/// <summary>
/// A mock tool for testing tool calling in agents and orchestration.
/// </summary>
public sealed class MockTool : ITool
{
    private readonly Func<JsonElement, ToolResult> _handler;

    public string Name { get; }
    public string Description { get; }
    public ToolAnnotations? Annotations => null;
    public List<JsonElement> ReceivedInputs { get; } = [];

    private MockTool(string name, string description, Func<JsonElement, ToolResult> handler)
    {
        Name = name;
        Description = description;
        _handler = handler;
    }

    public static MockTool AlwaysReturns(string name, object result, string? description = null)
    {
        return new MockTool(name, description ?? $"Mock {name}",
            _ => ToolResult.Success(result));
    }

    public static MockTool AlwaysFails(string name, string error, string? description = null)
    {
        return new MockTool(name, description ?? $"Mock {name} (fails)",
            _ => ToolResult.Failure(error));
    }

    public static MockTool WithHandler(string name, Func<JsonElement, ToolResult> handler, string? description = null)
    {
        return new MockTool(name, description ?? $"Mock {name}", handler);
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        ReceivedInputs.Add(input);
        return Task.FromResult(_handler(input));
    }
}
