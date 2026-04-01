using Microsoft.Extensions.AI;

namespace Nexus.Protocols.Mcp;

/// <summary>Manages connections to MCP servers and discovers tools/resources.</summary>
public interface IMcpHostManager : IAsyncDisposable
{
    /// <summary>Connect to an MCP server.</summary>
    Task<IMcpConnection> ConnectAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>All active connections.</summary>
    IReadOnlyList<IMcpConnection> Connections { get; }

    /// <summary>Discover all tools across all connected servers.</summary>
    Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(CancellationToken ct = default);

    /// <summary>Discover all MCP tools as AI functions ready for tool registration.</summary>
    Task<IReadOnlyList<AIFunction>> DiscoverFunctionsAsync(CancellationToken ct = default);

    /// <summary>Discover all resources across all connected servers.</summary>
    Task<IReadOnlyList<McpResourceDescriptor>> DiscoverResourcesAsync(CancellationToken ct = default);

    /// <summary>Discover all prompts across all connected servers.</summary>
    Task<IReadOnlyList<McpPromptDescriptor>> DiscoverPromptsAsync(CancellationToken ct = default);

    /// <summary>Disconnect from a specific server.</summary>
    Task DisconnectAsync(string serverName, CancellationToken ct = default);
}

/// <summary>Represents an active connection to an MCP server.</summary>
public interface IMcpConnection : IAsyncDisposable
{
    string ServerName { get; }
    bool IsConnected { get; }
    IReadOnlyList<McpToolDescriptor> Tools { get; }
    IReadOnlyList<McpResourceDescriptor> Resources { get; }
    IReadOnlyList<McpPromptDescriptor> Prompts { get; }
}
