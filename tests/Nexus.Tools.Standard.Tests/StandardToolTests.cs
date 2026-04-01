using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Testing.Mocks;
using Nexus.Tools.Standard;

namespace Nexus.Tools.Standard.Tests;

public sealed class StandardToolTests
{
    [Fact]
    public async Task FileReadTool_Reads_Line_Range()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "note.txt"), "one\ntwo\nthree\nfour");
        var tool = new FileReadTool(new StandardToolOptions { BaseDirectory = root });

        var result = await tool.ExecuteAsync(Parse("""
            { "path": "note.txt", "startLine": 2, "endLine": 3 }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("two" + Environment.NewLine + "three");
    }

    [Fact]
    public async Task FileWrite_And_Edit_Tools_Update_Sandboxed_File()
    {
        var root = CreateTempDirectory();
        var write = new FileWriteTool(new StandardToolOptions { BaseDirectory = root });
        var edit = new FileEditTool(new StandardToolOptions { BaseDirectory = root });

        var writeResult = await write.ExecuteAsync(Parse("""
            { "path": "work/out.txt", "content": "hello world" }
            """), CreateToolContext(), CancellationToken.None);
        writeResult.IsSuccess.Should().BeTrue();

        var editResult = await edit.ExecuteAsync(Parse("""
            { "path": "work/out.txt", "oldText": "world", "newText": "nexus" }
            """), CreateToolContext(), CancellationToken.None);

        editResult.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(root, "work", "out.txt"))).Should().Be("hello nexus");
    }

    [Fact]
    public async Task GlobTool_Finds_Files_By_Pattern()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "a.cs"), "class A {}");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "b.txt"), "text");
        var tool = new GlobTool(new StandardToolOptions { BaseDirectory = root });

        var result = await tool.ExecuteAsync(Parse("""
            { "pattern": "src/**/*.cs" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ((string[])result.Value!).Should().ContainSingle().Which.Should().Be("src/a.cs");
    }

    [Fact]
    public async Task GrepTool_Searches_File_Content()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "log.txt"), "alpha\nbeta\ngamma\nalpha beta");
        var tool = new GrepTool(new StandardToolOptions { BaseDirectory = root });

        var result = await tool.ExecuteAsync(Parse("""
            { "pattern": "alpha", "include": "**/*.txt" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ((GrepMatch[])result.Value!).Should().HaveCount(2);
    }

    [Fact]
    public async Task WebFetchTool_Returns_Response_Content()
    {
        var client = new HttpClient(new StubHttpHandler("hello from web"));
        var tool = new WebFetchTool(client, new StandardToolOptions());

        var result = await tool.ExecuteAsync(Parse("""
            { "url": "https://example.test/page" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ((WebFetchResult)result.Value!).Content.Should().Be("hello from web");
    }

    [Fact]
    public async Task ShellTool_Executes_Command()
    {
        var root = CreateTempDirectory();
        var tool = new ShellTool(new StandardToolOptions { BaseDirectory = root, WorkingDirectory = "." });

        var result = await tool.ExecuteAsync(Parse("""
            { "command": "dotnet", "arguments": ["--version"] }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ((ShellCommandResult)result.Value!).StandardOutput.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AskUserTool_Returns_Response_From_Interaction_Service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(new StubUserInteraction(new UserResponse("yes")));
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "type": "confirm", "question": "Proceed?" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ((UserResponse)result.Value!).Answer.Should().Be("yes");
    }

    [Fact]
    public async Task AskUserTool_Without_Interaction_Service_Fails_Clearly()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);
        var result = await tool.ExecuteAsync(Parse("""
            { "question": "Need input" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("IUserInteraction");
    }

    [Fact]
    public async Task AgentTool_Spawns_SubAgent_And_Returns_Result()
    {
        var services = new ServiceCollection();
        services.AddNexus(builder =>
        {
            builder.UseChatClient(_ => new FakeChatClient("sub-agent response"));
            builder.AddOrchestration(o => o.UseDefaults());
            builder.AddStandardTools(tools => tools.Only(StandardToolCategory.Agents));
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var tool = serviceProvider.GetRequiredService<IToolRegistry>().Resolve("agent");

        var result = await tool!.ExecuteAsync(Parse("""
            { "agent": "Reviewer", "task": "Review this change" }
            """), CreateToolContext(serviceProvider.GetRequiredService<IToolRegistry>()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ((AgentToolResult)result.Value!).Text.Should().Be("sub-agent response");
    }

    [Fact]
    public void AddStandardTools_Registers_Discovered_Tools_In_Registry()
    {
        var services = new ServiceCollection();
        services.AddNexus(builder =>
        {
            builder.UseChatClient(_ => new FakeChatClient("ok"));
            builder.AddOrchestration(o => o.UseDefaults());
            builder.AddStandardTools();
        });

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<IToolRegistry>();

        registry.Resolve("file_read").Should().NotBeNull();
        registry.Resolve("shell").Should().NotBeNull();
        registry.Resolve("web_fetch").Should().NotBeNull();
        registry.Resolve("agent").Should().NotBeNull();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static TestToolContext CreateToolContext(IToolRegistry? registry = null)
        => new(registry ?? new DefaultToolRegistry());

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-tools-standard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class TestToolContext : IToolContext
    {
        public TestToolContext(IToolRegistry registry)
        {
            Tools = registry;
        }

        public AgentId AgentId { get; } = AgentId.New();
        public IToolRegistry Tools { get; }
        public ISecretProvider? Secrets => null;
        public IBudgetTracker? Budget => null;
        public CorrelationContext Correlation { get; } = CorrelationContext.New();
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly string _content;

        public StubHttpHandler(string content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content, Encoding.UTF8, "text/plain"),
            });
    }

    private sealed class StubUserInteraction : IUserInteraction
    {
        private readonly UserResponse _response;

        public StubUserInteraction(UserResponse response)
        {
            _response = response;
        }

        public Task<UserResponse> AskAsync(UserQuestion question, UserInteractionOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(_response);
    }
}