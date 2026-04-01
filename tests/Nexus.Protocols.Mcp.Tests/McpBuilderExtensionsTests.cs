using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.Core.Configuration;
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
}