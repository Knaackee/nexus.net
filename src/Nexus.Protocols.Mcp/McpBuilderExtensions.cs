using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Configuration;

namespace Nexus.Protocols.Mcp;

public static class McpBuilderExtensions
{
    public static McpBuilder UseDefaults(this McpBuilder builder)
    {
        builder.Services.AddOptions<McpOptions>();
        builder.Services.TryAddSingleton<IMcpHostManager, DefaultMcpHostManager>();
        return builder;
    }

    public static McpBuilder Configure(this McpBuilder builder, Action<McpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure(configure);
        return builder;
    }

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
