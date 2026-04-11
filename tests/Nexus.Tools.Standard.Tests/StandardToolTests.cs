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
        var interaction = new StubUserInteraction(new UserResponse("yes"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "type": "confirm", "question": "Proceed?" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ((UserResponse)result.Value!).Answer.Should().Be("yes");
        interaction.LastQuestion.Should().BeOfType<ConfirmQuestion>();
    }

    [Fact]
    public async Task AskUserTool_InputType_Select_With_Options_Creates_SelectQuestion()
    {
        var interaction = new StubUserInteraction(new UserResponse("Program.cs"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "inputType": "select", "question": "Pick file", "options": ["Program.cs", "Startup.cs"] }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var question = interaction.LastQuestion.Should().BeOfType<SelectQuestion>().Subject;
        question.Options.Should().ContainInOrder("Program.cs", "Startup.cs");
    }

    [Fact]
    public async Task AskUserTool_Type_MultiSelect_With_Options_Creates_MultiSelectQuestion()
    {
        var interaction = new StubUserInteraction(new UserResponse("Program.cs,Startup.cs"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "type": "multiSelect", "question": "Pick files", "options": ["Program.cs", "Startup.cs"] }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var question = interaction.LastQuestion.Should().BeOfType<MultiSelectQuestion>().Subject;
        question.Options.Should().ContainInOrder("Program.cs", "Startup.cs");
    }

    [Fact]
    public async Task AskUserTool_Type_Secret_Creates_SecretQuestion()
    {
        var interaction = new StubUserInteraction(new UserResponse("token"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "type": "secret", "question": "API key?" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        interaction.LastQuestion.Should().BeOfType<SecretQuestion>();
    }

    [Fact]
    public async Task AskUserTool_Unknown_Type_Fails_With_Actionable_Error()
    {
        var interaction = new StubUserInteraction(new UserResponse("ignored"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "type": "dropdown", "question": "Pick one" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unsupported ask_user type");
    }

    [Theory]
    [InlineData("select")]
    [InlineData("multiSelect")]
    public async Task AskUserTool_Select_Modes_Without_Options_Fail(string inputType)
    {
        var interaction = new StubUserInteraction(new UserResponse("ignored"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse($$"""
            { "type": "{{inputType}}", "question": "Pick one" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("options");
    }

    [Fact]
    public async Task AskUserTool_Type_Takes_Precedence_When_InputType_Mismatches()
    {
        var interaction = new StubUserInteraction(new UserResponse("yes"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "type": "confirm", "inputType": "select", "question": "Proceed?", "options": ["yes", "no"] }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        interaction.LastQuestion.Should().BeOfType<ConfirmQuestion>();
    }

    [Theory]
    [InlineData("SELECT", typeof(SelectQuestion))]
    [InlineData("MultiSelect", typeof(MultiSelectQuestion))]
    [InlineData("CONFIRM", typeof(ConfirmQuestion))]
    [InlineData("Secret", typeof(SecretQuestion))]
    [InlineData("freetext", typeof(FreeTextQuestion))]
    public async Task AskUserTool_Normalizes_Input_Types_Case_Insensitively(string inputType, Type expectedQuestionType)
    {
        var interaction = new StubUserInteraction(new UserResponse("ok"));
        var services = new ServiceCollection();
        services.AddSingleton<IUserInteraction>(interaction);
        using var serviceProvider = services.BuildServiceProvider();
        var tool = new AskUserTool(serviceProvider);

        var json = inputType.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            || inputType.Equals("MultiSelect", StringComparison.OrdinalIgnoreCase)
            ? $$"""{ "type": "{{inputType}}", "question": "Q", "options": ["one"] }"""
            : $$"""{ "type": "{{inputType}}", "question": "Q" }""";

        var result = await tool.ExecuteAsync(Parse(json), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        interaction.LastQuestion.Should().BeOfType(expectedQuestionType);
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
    public async Task AgentTool_Can_Run_Multiple_SubAgents_In_Parallel()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var pool = new RecordingAgentPool(TimeSpan.FromMilliseconds(75));
        var tool = new AgentTool(pool, serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            {
              "maxConcurrency": 2,
              "tasks": [
                { "agent": "Researcher", "task": "Collect facts" },
                { "agent": "Reviewer", "task": "Review draft" }
              ]
            }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var batch = (AgentBatchToolResult)result.Value!;
        batch.Status.Should().Be("Success");
        batch.CompletedCount.Should().Be(2);
        batch.FailedCount.Should().Be(0);
        batch.Results.Should().HaveCount(2);
        batch.Results.Select(item => item.Text).Should().Contain(["Researcher:Collect facts", "Reviewer:Review draft"]);
        pool.MaxObservedConcurrency.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task AgentTool_Batch_Reports_Failures_Without_Failing_Whole_Tool()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var pool = new RecordingAgentPool(failingAgents: ["Reviewer"]);
        var tool = new AgentTool(pool, serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            {
              "tasks": [
                { "agent": "Researcher", "task": "Collect facts" },
                { "agent": "Reviewer", "task": "Review draft" }
              ]
            }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var batch = (AgentBatchToolResult)result.Value!;
        batch.Status.Should().Be("PartialSuccess");
        batch.CompletedCount.Should().Be(1);
        batch.FailedCount.Should().Be(1);
        batch.Results.Should().Contain(item => item.Agent == "Reviewer" && item.IsSuccess == false);
    }

    [Fact]
    public async Task AgentTool_Single_Request_Failure_Returns_Tool_Failure()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var pool = new RecordingAgentPool(failingAgents: ["Reviewer"]);
        var tool = new AgentTool(pool, serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "agent": "Reviewer", "task": "Review draft" }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Reviewer failed");
    }

    [Fact]
    public async Task AgentTool_Empty_Batch_Returns_Tool_Failure()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var tool = new AgentTool(new RecordingAgentPool(), serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            { "tasks": [] }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("at least one entry");
    }

    [Fact]
    public async Task AgentTool_Batch_Uses_Default_ToolNames_For_Requests()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var pool = new RecordingAgentPool();
        var registry = new DefaultToolRegistry();
        var tool = new AgentTool(pool, serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            {
              "toolNames": ["grep", "file_read"],
              "tasks": [
                { "agent": "Researcher", "task": "Collect facts" },
                { "agent": "Reviewer", "task": "Review draft", "toolNames": ["shell"] }
              ]
            }
            """), CreateToolContext(registry), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        pool.SpawnedDefinitions.Should().HaveCount(2);
        pool.SpawnedDefinitions[0].ToolNames.Should().ContainInOrder("grep", "file_read");
        pool.SpawnedDefinitions[1].ToolNames.Should().ContainSingle().Which.Should().Be("shell");
    }

    [Fact]
    public async Task AgentTool_MaxConcurrency_Lower_Than_One_Is_Coerced_To_One()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var pool = new RecordingAgentPool(TimeSpan.FromMilliseconds(50));
        var tool = new AgentTool(pool, serviceProvider);

        var result = await tool.ExecuteAsync(Parse("""
            {
              "maxConcurrency": 0,
              "tasks": [
                { "agent": "A", "task": "First" },
                { "agent": "B", "task": "Second" }
              ]
            }
            """), CreateToolContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        pool.MaxObservedConcurrency.Should().Be(1);
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
        public UserQuestion? LastQuestion { get; private set; }
        public UserInteractionOptions? LastOptions { get; private set; }

        public StubUserInteraction(UserResponse response)
        {
            _response = response;
        }

        public Task<UserResponse> AskAsync(UserQuestion question, UserInteractionOptions? options = null, CancellationToken ct = default)
        {
            LastQuestion = question;
            LastOptions = options;
            return Task.FromResult(_response);
        }
    }

    private sealed class RecordingAgentPool : IAgentPool
    {
        private readonly TimeSpan _delay;
        private readonly HashSet<string> _failingAgents;
        private int _activeExecutions;

        public RecordingAgentPool(TimeSpan? delay = null, IReadOnlyCollection<string>? failingAgents = null)
        {
            _delay = delay ?? TimeSpan.Zero;
            _failingAgents = failingAgents is null
                ? []
                : new HashSet<string>(failingAgents, StringComparer.Ordinal);
        }

        public int MaxObservedConcurrency { get; private set; }

        public List<AgentDefinition> SpawnedDefinitions { get; } = [];

        public IReadOnlyList<IAgent> ActiveAgents => [];

        public IObservable<AgentLifecycleEvent> Lifecycle => throw new NotSupportedException();

        public Task CheckpointAndStopAllAsync(ICheckpointStore store, CancellationToken ct = default) => Task.CompletedTask;

        public Task DrainAsync(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;

        public Task KillAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task PauseAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task ResumeAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IAgent> SpawnAsync(AgentDefinition definition, CancellationToken ct = default)
        {
            SpawnedDefinitions.Add(definition);
            return Task.FromResult<IAgent>(new RecordingAgent(definition.Name, definition.Name, _delay, _failingAgents.Contains(definition.Name), this));
        }

        private void Enter()
        {
            var current = Interlocked.Increment(ref _activeExecutions);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
        }

        private void Exit() => Interlocked.Decrement(ref _activeExecutions);

        private sealed class RecordingAgent : IAgent
        {
            private readonly string _responsePrefix;
            private readonly TimeSpan _delay;
            private readonly bool _shouldFail;
            private readonly RecordingAgentPool _owner;

            public RecordingAgent(string name, string responsePrefix, TimeSpan delay, bool shouldFail, RecordingAgentPool owner)
            {
                Name = name;
                _responsePrefix = responsePrefix;
                _delay = delay;
                _shouldFail = shouldFail;
                _owner = owner;
            }

            public AgentId Id { get; } = AgentId.New();
            public string Name { get; }
            public AgentState State => AgentState.Idle;

            public async Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
            {
                _owner.Enter();
                try
                {
                    if (_delay > TimeSpan.Zero)
                        await Task.Delay(_delay, ct);

                    if (_shouldFail)
                        throw new InvalidOperationException($"{Name} failed");

                    return AgentResult.Success($"{_responsePrefix}:{task.Description}");
                }
                finally
                {
                    _owner.Exit();
                }
            }

            public async IAsyncEnumerable<Nexus.Core.Events.AgentEvent> ExecuteStreamingAsync(AgentTask task, IAgentContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return new Nexus.Core.Events.AgentCompletedEvent(Id, await ExecuteAsync(task, context, ct));
            }
        }
    }
}