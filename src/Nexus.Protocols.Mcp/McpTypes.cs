using System.Text.Json;
using Nexus.Core.Agents;
using Nexus.Core.Tools;

namespace Nexus.Protocols.Mcp;

/// <summary>Configuration for an MCP server connection.</summary>
public record McpServerConfig
{
    public required string Name { get; init; }
    public required McpTransport Transport { get; init; }
    public ToolFilter? AllowedTools { get; init; }
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Base transport configuration.</summary>
public abstract record McpTransport;

/// <summary>Stdio transport — launches a process and communicates via stdin/stdout.</summary>
public record StdioTransport(string Command, string[]? Arguments = null, string? WorkingDirectory = null) : McpTransport;

/// <summary>HTTP+SSE transport — connects to an HTTP endpoint that streams via Server-Sent Events.</summary>
public record HttpSseTransport(Uri Endpoint) : McpTransport;

/// <summary>Descriptor for a tool discovered from an MCP server.</summary>
public record McpToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public JsonElement? InputSchema { get; init; }
    public string? ServerName { get; init; }
}

/// <summary>Descriptor for a resource discovered from an MCP server.</summary>
public record McpResourceDescriptor
{
    public required string Uri { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? MimeType { get; init; }
    public string? ServerName { get; init; }
}

/// <summary>Descriptor for a prompt discovered from an MCP server.</summary>
public record McpPromptDescriptor
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ServerName { get; init; }
}
