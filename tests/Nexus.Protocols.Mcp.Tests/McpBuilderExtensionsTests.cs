using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.Core.Configuration;
using Nexus.Core.Tools;
using Xunit;

namespace Nexus.Protocols.Mcp.Tests;

public sealed class McpBuilderExtensionsTests
{
    [Fact]
    public async Task UseDefaults_Registers_Host_Manager_And_Options()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);

        builder.UseDefaults();
        await using var provider = services.BuildServiceProvider();

        provider.GetService<IMcpHostManager>().Should().NotBeNull();
        provider.GetService<IOptions<McpOptions>>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddServer_Adds_Server_Configuration()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);

        builder.UseDefaults();
        builder.AddServer("filesystem", new StdioTransport("npx", ["-y", "@modelcontextprotocol/server-filesystem"]));

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpOptions>>().Value;

        options.Servers.Should().ContainSingle();
        options.Servers[0].Name.Should().Be("filesystem");
        options.Servers[0].Transport.Should().BeOfType<StdioTransport>();
    }

    [Fact]
    public async Task DiscoverFunctionsAsync_Without_Configured_Servers_Returns_Empty()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);

        builder.UseDefaults();
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IMcpHostManager>();

        var functions = await manager.DiscoverFunctionsAsync();

        functions.Should().BeEmpty();
    }

    [Fact]
    public async Task AddServer_Multiple_Servers_Registered()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);

        builder.UseDefaults();
        builder.AddServer("server-a", new StdioTransport("cmd", ["a"]));
        builder.AddServer("server-b", new StdioTransport("cmd", ["b"]));

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpOptions>>().Value;

        options.Servers.Should().HaveCount(2);
        options.Servers.Select(s => s.Name).Should().Contain(["server-a", "server-b"]);
    }

    [Fact]
    public async Task AddServer_Config_Overload_Preserves_AllowedTools()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);

        builder.UseDefaults();
        builder.AddServer(new McpServerConfig
        {
            Name = "filtered",
            Transport = new StdioTransport("cmd"),
            AllowedTools = new Nexus.Core.Agents.ToolFilter { Include = ["tool-a"] },
        });

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpOptions>>().Value;

        options.Servers[0].AllowedTools.Should().NotBeNull();
        options.Servers[0].AllowedTools!.Include.Should().Contain("tool-a");
    }

    [Fact]
    public async Task Configure_Action_Applies_To_Options()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);

        builder.UseDefaults();
        builder.Configure(opts =>
        {
            opts.Servers.Add(new McpServerConfig
            {
                Name = "via-configure",
                Transport = new StdioTransport("echo"),
            });
        });

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpOptions>>().Value;

        options.Servers.Should().ContainSingle(s => s.Name == "via-configure");
    }

    [Fact]
    public async Task DiscoverToolsAsync_Without_Servers_Returns_Empty()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);
        builder.UseDefaults();

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IMcpHostManager>();

        var tools = await manager.DiscoverToolsAsync();
        tools.Should().BeEmpty();
    }

    [Fact]
    public async Task Connections_Initially_Empty()
    {
        var services = new ServiceCollection();
        var builder = new McpBuilder(services);
        builder.UseDefaults();

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IMcpHostManager>();

        manager.Connections.Should().BeEmpty();
    }

    [Fact]
    public void McpServerConfig_Default_ConnectionTimeout_Is_30s()
    {
        var config = new McpServerConfig
        {
            Name = "test",
            Transport = new StdioTransport("cmd"),
        };

        config.ConnectionTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void McpToolAdapter_Exposes_Name_And_Description()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "read_file",
            Description = "Reads a file from disk",
            ServerName = "filesystem",
        };

        var adapter = new McpToolAdapter(
            descriptor,
            null!, // connection not needed for property access
            (_, _, _) => Task.FromResult(default(JsonElement)));

        adapter.Name.Should().Be("read_file");
        adapter.Description.Should().Be("Reads a file from disk");
        adapter.Annotations.Should().BeNull();
    }
}