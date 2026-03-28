using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Protocols.Mcp;

/// <summary>
/// Adapts an MCP server tool into a Nexus ITool so it can be used in agents.
/// </summary>
public sealed class McpToolAdapter : ITool
{
    private readonly IMcpConnection _connection;
    private readonly Func<string, JsonElement, CancellationToken, Task<JsonElement>> _callTool;

    public string Name { get; }
    public string Description { get; }
    public ToolAnnotations? Annotations { get; }

    public McpToolAdapter(
        McpToolDescriptor descriptor,
        IMcpConnection connection,
        Func<string, JsonElement, CancellationToken, Task<JsonElement>> callTool)
    {
        Name = descriptor.Name;
        Description = descriptor.Description;
        Annotations = null;
        _connection = connection;
        _callTool = callTool;
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var result = await _callTool(Name, input, ct).ConfigureAwait(false);
            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"MCP tool '{Name}' failed: {ex.Message}");
        }
    }
}
