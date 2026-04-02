using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Nexus.Compaction;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Orchestration.Defaults;
using Nexus.Skills;
using Nexus.Testing.Mocks;
using Nexus.Sessions;
using Nexus.Workflows.Dsl;
using Xunit;

namespace Nexus.AgentLoop.Tests;

public sealed class AgentLoopTests
{
    [Fact]
    public async Task RunAsync_Streams_Text_And_Persists_Session()
    {
        var client = new FakeChatClient("hello from loop");
        using var services = BuildServices(client);
        var loop = services.GetRequiredService<IAgentLoop>();
        var sessionStore = services.GetRequiredService<InMemorySessionStore>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            UserInput = "Say hello",
            SessionTitle = "loop-test",
        }))
        {
            events.Add(evt);
        }

        events.Should().Contain(e => e is LoopStartedEvent);
        events.Should().Contain(e => e is TextChunkLoopEvent);
        events.OfType<LoopCompletedEvent>().Single().FinalResult.Status.Should().Be(AgentResultStatus.Success);

        var session = await FirstSessionAsync(sessionStore);
        session.Should().NotBeNull();
        session!.MessageCount.Should().Be(2);

        var transcript = new List<ChatMessage>();
        await foreach (var message in sessionStore.ReadAsync(session.Id))
            transcript.Add(message);

        transcript.Select(m => m.Text).Should().ContainInOrder("Say hello", "hello from loop");
    }

    [Fact]
    public async Task RunAsync_Persists_Structured_Assistant_Contents()
    {
        var client = new FakeChatClient().WithReasoningResponse("Trace the steps.", "Done");
        using var services = BuildServices(client);
        var loop = services.GetRequiredService<IAgentLoop>();
        var sessionStore = services.GetRequiredService<InMemorySessionStore>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            UserInput = "Think first",
            SessionTitle = "structured-session",
        }));

        var session = await FirstSessionAsync(sessionStore);
        session.Should().NotBeNull();

        var transcript = new List<ChatMessage>();
        await foreach (var message in sessionStore.ReadAsync(session!.Id))
            transcript.Add(message);

        var assistant = transcript.Last(message => message.Role == ChatRole.Assistant);
        assistant.Contents.Select(content => content.GetType()).Should().ContainInOrder(
            typeof(TextReasoningContent),
            typeof(TextContent));
        assistant.Text.Should().Be("Done");
    }

    [Fact]
    public async Task RunAsync_Emits_UserInputRequestedLoopEvent_For_AskUser_ToolCall()
    {
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("ask-1", "ask_user", new Dictionary<string, object?>
            {
                ["question"] = "Ship it?",
                ["type"] = "confirm"
            }))
            .WithResponse("User approved");
        using var services = BuildServices(client);
        services.GetRequiredService<IToolRegistry>().Register(MockTool.AlwaysReturns("ask_user", "yes"));
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user"] },
            UserInput = "Ask before shipping",
        }))
        {
            events.Add(evt);
        }

        var request = events.OfType<UserInputRequestedLoopEvent>().Single();
        request.RequestId.Should().Be("ask-1");
        request.Request.Question.Should().Be("Ship it?");
        request.Request.InputType.Should().Be("confirm");
    }

    [Fact]
    public async Task RunAsync_ResumeLastSession_Reuses_Transcript_History()
    {
        var client = new FakeChatClient("first answer", "second answer");
        using var services = BuildServices(client);
        var loop = services.GetRequiredService<IAgentLoop>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            UserInput = "First question",
            SessionTitle = "resume-test",
        }));

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            ResumeLastSession = true,
            UserInput = "Second question",
        }));

        client.ReceivedMessages.Should().HaveCount(2);
        client.ReceivedMessages[1].Select(m => m.Text).Should().ContainInOrder("First question", "first answer", "Second question");
    }

    [Fact]
    public async Task RunAsync_Triggers_Compaction_When_Context_Window_Is_Exceeded()
    {
        var client = new FakeChatClient("condensed response");
        using var services = BuildServices(client, configure: collection =>
        {
            collection.AddSingleton(new CompactionOptions
            {
                RecentMessagesToKeep = 2,
                MinimumToolContentLength = 40,
            });
            collection.AddSingleton<ITokenCounter, DefaultTokenCounter>();
            collection.AddSingleton<IContextWindowMonitor, DefaultContextWindowMonitor>();
            collection.AddSingleton<ICompactionStrategy, MicroCompactionStrategy>();
            collection.AddSingleton<ICompactionStrategy, SummaryCompactionStrategy>();
            collection.AddSingleton<ICompactionService, DefaultCompactionService>();
        });
        var loop = services.GetRequiredService<IAgentLoop>();

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Earlier request"),
            new(ChatRole.Tool, new string('x', 220)),
            new(ChatRole.Assistant, "Observed the tool output."),
            new(ChatRole.User, "Continue the task"),
        };

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            Messages = history,
            ContextWindow = new ContextWindowOptions { MaxTokens = 80, TargetTokens = 40, ReservedForOutput = 8, ReservedForTools = 8 },
        }))
        {
            events.Add(evt);
        }

        events.Should().Contain(e => e is CompactionTriggeredLoopEvent);
        client.ReceivedMessages.Should().ContainSingle();
        client.ReceivedMessages[0].Any(message => message.Text?.Contains("[Compacted tool output:") == true).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Applies_PostCompaction_Recall_Before_AgentExecution()
    {
        var client = new FakeChatClient("response after recall");
        using var services = BuildServices(client, configure: collection =>
        {
            collection.AddSingleton(new CompactionOptions
            {
                RecentMessagesToKeep = 2,
                MinimumToolContentLength = 40,
            });
            collection.AddSingleton<ITokenCounter, DefaultTokenCounter>();
            collection.AddSingleton<IContextWindowMonitor, DefaultContextWindowMonitor>();
            collection.AddSingleton<ICompactionStrategy, MicroCompactionStrategy>();
            collection.AddSingleton<ICompactionStrategy, SummaryCompactionStrategy>();
            collection.AddSingleton<ICompactionService, DefaultCompactionService>();
            collection.AddSingleton<ICompactionRecallProvider>(new PrefixRecallProvider("Recovered memory"));
            collection.AddSingleton<ICompactionRecallService, DefaultCompactionRecallService>();
        });
        var loop = services.GetRequiredService<IAgentLoop>();

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Earlier request"),
            new(ChatRole.Tool, new string('x', 220)),
            new(ChatRole.Assistant, "Observed the tool output."),
            new(ChatRole.User, "Continue the task"),
        };

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            Messages = history,
            ContextWindow = new ContextWindowOptions { MaxTokens = 80, TargetTokens = 40, ReservedForOutput = 8, ReservedForTools = 8 },
        }));

        client.ReceivedMessages.Should().ContainSingle();
        client.ReceivedMessages[0].Any(message => message.Text == "Recovered memory").Should().BeTrue();
        client.ReceivedMessages[0].Any(message => message.Text?.Contains("[Compacted tool output:") == true).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_MaxTurnsReached_Does_Not_Invoke_Agent()
    {
        var client = new FakeChatClient("should not be used");
        using var services = BuildServices(client);
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            Messages = [new ChatMessage(ChatRole.User, "Previous turn")],
            UserInput = "New turn",
            MaxTurns = 1,
        }))
        {
            events.Add(evt);
        }

        events.Should().ContainSingle(e => e is LoopCompletedEvent);
        events.OfType<LoopCompletedEvent>().Single().Reason.Should().Be(LoopStopReason.MaxTurnsReached);
        client.ReceivedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WithWorkflowRoutingStrategy_RunsWorkflowNodesInOrder()
    {
        var client = new FakeChatClient("research output", "implementation output");
        using var services = BuildServices(client);
        var loop = services.GetRequiredService<IAgentLoop>();

        var workflow = new WorkflowDefinition
        {
            Id = "wf-1",
            Name = "Sequential workflow",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "research",
                    Name = "Research",
                    Description = "Research the request: {input}",
                },
                new NodeDefinition
                {
                    Id = "implement",
                    Name = "Implement",
                    Description = "Implement based on: {previous}",
                },
            ],
            Edges = [new EdgeDefinition { From = "research", To = "implement" }],
        };

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            RoutingStrategy = new WorkflowRoutingStrategy(workflow),
            UserInput = "Fix auth",
            SessionTitle = "workflow-test",
        }))
        {
            events.Add(evt);
        }

        events.OfType<LoopCompletedEvent>().Single().Reason.Should().Be(LoopStopReason.AgentCompleted);
        client.ReceivedMessages.Should().HaveCount(2);
        client.ReceivedMessages[0].Select(m => m.Text).Should().Contain("Research the request: Fix auth");
        client.ReceivedMessages[1].Select(m => m.Text).Should().Contain("Implement based on: research output");
    }

    [Fact]
    public async Task RunAsync_WithWorkflowRoutingStrategy_StopsWhenApprovalIsRejected()
    {
        var client = new FakeChatClient("draft output");
        var gate = MockApprovalGate.AutoDeny();
        using var services = BuildServices(client, gate);
        var loop = services.GetRequiredService<IAgentLoop>();

        var workflow = new WorkflowDefinition
        {
            Id = "wf-2",
            Name = "Approval workflow",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "draft",
                    Name = "Draft",
                    Description = "Create a draft for: {input}",
                    RequiresApproval = true,
                },
                new NodeDefinition
                {
                    Id = "publish",
                    Name = "Publish",
                    Description = "Publish: {previous}",
                },
            ],
            Edges = [new EdgeDefinition { From = "draft", To = "publish" }],
        };

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            RoutingStrategy = new WorkflowRoutingStrategy(workflow),
            UserInput = "Write a summary",
        }))
        {
            events.Add(evt);
        }

        events.OfType<LoopCompletedEvent>().Single().Reason.Should().Be(LoopStopReason.StepRejected);
        gate.ReceivedRequests.Should().ContainSingle();
        client.ReceivedMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_WithWorkflowRoutingStrategy_Uses_Modified_Approval_Output_For_Next_Node()
    {
        var client = new FakeChatClient("research output", "plan output", "execution output");
        var gate = new ModifiedApprovalGate("approved plan with rollback");
        using var services = BuildServices(client, gate);
        var loop = services.GetRequiredService<IAgentLoop>();

        var workflow = new WorkflowDefinition
        {
            Id = "wf-3",
            Name = "Modified approval workflow",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "research",
                    Name = "Research",
                    Description = "Research: {input}",
                },
                new NodeDefinition
                {
                    Id = "plan",
                    Name = "Plan",
                    Description = "Plan: {previous}",
                    RequiresApproval = true,
                },
                new NodeDefinition
                {
                    Id = "execute",
                    Name = "Execute",
                    Description = "Execute: {previous}",
                },
            ],
            Edges =
            [
                new EdgeDefinition { From = "research", To = "plan" },
                new EdgeDefinition { From = "plan", To = "execute" },
            ],
        };

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            RoutingStrategy = new WorkflowRoutingStrategy(workflow),
            UserInput = "Prepare deployment",
        }));

        client.ReceivedMessages.Should().HaveCount(3);
        client.ReceivedMessages[2].Select(m => m.Text).Should().Contain(text => text != null && text.Contains("approved plan with rollback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_WithWorkflowRoutingStrategy_StopsImmediately_For_Empty_Workflow()
    {
        var client = new FakeChatClient("unused");
        using var services = BuildServices(client);
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            RoutingStrategy = new WorkflowRoutingStrategy(new WorkflowDefinition
            {
                Id = "wf-empty",
                Name = "Empty",
                Nodes = [],
            }),
            UserInput = "Do nothing",
        }))
        {
            events.Add(evt);
        }

        events.OfType<LoopCompletedEvent>().Single().Reason.Should().Be(LoopStopReason.AgentCompleted);
        client.ReceivedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Applies_Relevant_Skills_Through_AgentMiddleware()
    {
        var client = new FakeChatClient("skill-aware answer");
        using var services = BuildServices(client, configure: collection =>
        {
            var catalog = new SkillCatalog();
            catalog.Register(new SkillDefinition
            {
                Name = "csharp",
                SystemPrompt = "Use the repository's C# conventions.",
                WhenToUse = "When reviewing or writing C# code",
            });

            collection.AddSingleton<ISkillCatalog>(catalog);
            collection.AddSingleton(new SkillInjectionOptions { MaxSkills = 2 });
            collection.AddSingleton<Nexus.Core.Pipeline.IAgentMiddleware, SkillInjectionMiddleware>();
        });
        var loop = services.GetRequiredService<IAgentLoop>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", SystemPrompt = "Base prompt." },
            UserInput = "Review this C# change",
        }));

        client.ReceivedMessages.Should().ContainSingle();
        client.ReceivedMessages[0].First().Role.Should().Be(ChatRole.System);
        client.ReceivedMessages[0].First().Text.Should().Contain("Base prompt.");
        client.ReceivedMessages[0].First().Text.Should().Contain("Use the repository's C# conventions.");
    }

    private static async Task DrainAsync(IAsyncEnumerable<AgentLoopEvent> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private static async Task<SessionInfo?> FirstSessionAsync(InMemorySessionStore store)
    {
        await foreach (var session in store.ListAsync())
            return session;

        return null;
    }

    private static ServiceProvider BuildServices(FakeChatClient client, IApprovalGate? approvalGate = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(client);
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        services.AddSingleton<IApprovalGate>(approvalGate ?? new AutoApproveGate());
        services.AddSingleton<IAgentPool, DefaultAgentPool>();
        services.AddSingleton<InMemorySessionStore>();
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<InMemorySessionStore>());
        services.AddSingleton<ISessionTranscript>(sp => sp.GetRequiredService<InMemorySessionStore>());
        services.AddSingleton<IAgentLoop, DefaultAgentLoop>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class PrefixRecallProvider : ICompactionRecallProvider
    {
        private readonly string _memory;

        public PrefixRecallProvider(string memory)
        {
            _memory = memory;
        }

        public int Priority => 0;

        public Task<IReadOnlyList<ChatMessage>> RecallAsync(CompactionRecallContext context, CancellationToken ct = default)
        {
            IReadOnlyList<ChatMessage> messages =
            [
                new ChatMessage(ChatRole.System, _memory),
                .. context.ActiveMessages,
            ];

            return Task.FromResult(messages);
        }
    }

    private sealed class ModifiedApprovalGate : IApprovalGate
    {
        private readonly string _output;

        public ModifiedApprovalGate(string output)
        {
            _output = output;
        }

        public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            var modified = JsonSerializer.SerializeToElement(new { Output = _output });
            return Task.FromResult(new ApprovalResult(true, "test-approver", ModifiedContext: modified));
        }
    }
}