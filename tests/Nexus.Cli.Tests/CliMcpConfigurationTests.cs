using FluentAssertions;
using Nexus.Protocols.Mcp;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliMcpConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nexus-cli-mcp-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Load_Merges_User_And_Project_Configurations_With_Project_Precedence()
    {
        var workspace = CreateWorkspace();

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.UserMcpConfigPath)!);
        File.WriteAllText(workspace.UserMcpConfigPath, """
        {
          "servers": {
            "shared": {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-memory"],
              "allowedTools": {
                "include": ["search"]
              }
            },
            "user-only": {
              "endpoint": "https://example.com/mcp"
            }
          }
        }
        """);

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.ProjectMcpConfigPath)!);
        File.WriteAllText(workspace.ProjectMcpConfigPath, """
        {
          "servers": {
            "shared": {
              "command": "uvx",
              "args": ["acme-server"],
              "workingDirectory": "tools/mcp",
              "allowedTools": {
                "exclude": ["dangerous"]
              }
            },
            "project-only": {
              "command": "node",
              "args": ["server.js"]
            }
          }
        }
        """);

        var servers = CliMcpConfiguration.Load(workspace);

        servers.Should().HaveCount(3);
        servers.Select(server => server.Name).Should().BeEquivalentTo(["shared", "user-only", "project-only"]);

        var shared = servers.Single(server => server.Name == "shared");
        shared.Transport.Should().BeOfType<StdioTransport>();
        var sharedTransport = (StdioTransport)shared.Transport;
        sharedTransport.Command.Should().Be("uvx");
        sharedTransport.Arguments.Should().Equal(["acme-server"]);
        sharedTransport.WorkingDirectory.Should().Be(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(workspace.ProjectMcpConfigPath)!, "tools/mcp")));
        shared.AllowedTools.Should().NotBeNull();
        shared.AllowedTools!.Exclude.Should().Equal(["dangerous"]);

        var userOnly = servers.Single(server => server.Name == "user-only");
        userOnly.Transport.Should().BeOfType<HttpSseTransport>();

        var projectOnly = servers.Single(server => server.Name == "project-only");
        projectOnly.Transport.Should().BeOfType<StdioTransport>();
    }

    [Fact]
    public void Load_Returns_Empty_When_No_Config_File_Exists()
    {
        var workspace = CreateWorkspace();

        var servers = CliMcpConfiguration.Load(workspace);

        servers.Should().BeEmpty();
    }

    private CliWorkspaceOptions CreateWorkspace()
    {
        var projectRoot = Path.Combine(_root, "project");
        var projectNexus = Path.Combine(projectRoot, ".nexus");
        var userNexus = Path.Combine(_root, "user", ".nexus");

        return new CliWorkspaceOptions(
            projectRoot,
            projectNexus,
            Path.Combine(projectNexus, "sessions"),
            Path.Combine(projectNexus, "mcp.json"),
            Path.Combine(userNexus, "mcp.json"),
            Path.Combine(projectNexus, "skills"),
            Path.Combine(userNexus, "skills"),
            Path.Combine(projectNexus, "commands"),
            Path.Combine(userNexus, "commands"),
            [],
            []);
    }
}