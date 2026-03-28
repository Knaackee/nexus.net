using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;

namespace Nexus.Protocols.Mcp;

public static class McpBuilderExtensions
{
    public static McpBuilder AddServer(this McpBuilder builder, string name, McpTransport transport)
    {
        builder.Services.Configure<McpOptions>(o => o.Servers.Add(new McpServerConfig
        {
            Name = name,
            Transport = transport,
        }));
        return builder;
    }

    public static McpBuilder AddServer(this McpBuilder builder, McpServerConfig config)
    {
        builder.Services.Configure<McpOptions>(o => o.Servers.Add(config));
        return builder;
    }
}

/// <summary>Options for MCP configuration collected by the builder.</summary>
public sealed class McpOptions
{
    public List<McpServerConfig> Servers { get; } = [];
}
