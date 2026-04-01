using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Compaction;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.Memory;
using Nexus.Orchestration;
using Nexus.Orchestration.Defaults;
using Nexus.Sessions;
using Nexus.Testing.Mocks;
using Nexus.Tools.Standard;
using Nexus.Workflows.Dsl;
using Xunit;

namespace Nexus.Examples.Tests;

public sealed class RecipeExamplesTests
{
    [Fact]
    public async Task SingleAgentWithTools_RunsToolCallFlow()
    {
        var services = new ServiceCollection();
        services.AddNexus(nexus =>
        {
            nexus.UseChatClient(_ => new FakeChatClient()
                .WithFunctionCallResponse(new FunctionCallContent("call-1", "get_time"))
                .WithResponse("The time is 12:00"));
            nexus.AddOrchestration(o => o.UseDefaults());
        });

        await using var provider = services.BuildServiceProvider();
        var tool = MockTool.AlwaysReturns("get_time", "12:00");
        provider.GetRequiredService<IToolRegistry>().Register(tool);

        var pool = provider.GetRequiredService<IAgentPool>();
        var orchestrator = provider.GetRequiredService<IOrchestrator>();
        var agent = await pool.SpawnAsync(new AgentDefinition { Name = "Assistant", ToolNames = ["get_time"] });

        var result = await orchestrator.ExecuteSequenceAsync([
            AgentTask.Create("What time is it?") with { AssignedAgent = agent.Id }
        ]);

        result.Status.Should().Be(OrchestrationStatus.Completed);
        result.TaskResults.Values.Single().Text.Should().Be("The time is 12:00");
        tool.ReceivedInputs.Should().HaveCount(1);
    }

    [Fact]
    public async Task ChatSessionWithMemory_PersistsAndResumesSession()
    {
        var services = new ServiceCollection();
        services.AddNexus(nexus =>
        {
            nexus.UseChatClient(_ => new FakeChatClient("first answer", "second answer"));
            nexus.AddOrchestration(o => o.UseDefaults());
            nexus.AddAgentLoop(loop => loop.UseDefaults());
            nexus.AddSessions(s => s.UseInMemory());
            nexus.AddCompaction(c => c.UseDefaults());
            nexus.AddMemory(m =>
            {
                m.UseInMemory();
                m.UseLongTermMemoryRecall();
            });
        });

        await using var provider = services.BuildServiceProvider();
        var loop = provider.GetRequiredService<IAgentLoop>();
        var sessionStore = provider.GetRequiredService<ISessionStore>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            UserInput = "First question",
            SessionTitle = "recipe-memory",
        }));

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant" },
            ResumeLastSession = true,
            UserInput = "Second question",
        }));

        var sessions = new List<SessionInfo>();
        await foreach (var session in sessionStore.ListAsync())
            sessions.Add(session);

        sessions.Should().ContainSingle();
        sessions[0].MessageCount.Should().Be(4);
    }

    [Fact]
    public async Task HumanApprovedWorkflow_UsesApprovalGateBeforeNextStep()
    {
        var gate = new ApprovedWithModifiedOutputGate("plan output approved by reviewer");
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(_ => new FakeChatClient("research output", "plan output", "execution output"));
        services.AddSingleton<IApprovalGate>(gate);
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        services.AddSingleton<IAgentPool, DefaultAgentPool>();
        services.AddSingleton<InMemorySessionStore>();
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<InMemorySessionStore>());
        services.AddSingleton<ISessionTranscript>(sp => sp.GetRequiredService<InMemorySessionStore>());
        services.AddSingleton<IAgentLoop, DefaultAgentLoop>();

        await using var provider = services.BuildServiceProvider();
        var loop = provider.GetRequiredService<IAgentLoop>();
        var workflow = new WorkflowDefinition
        {
            Id = "wf-approval",
            Name = "Approval Workflow",
            Nodes =
            [
                new NodeDefinition { Id = "research", Name = "Research", Description = "Research: {input}" },
                new NodeDefinition { Id = "plan", Name = "Plan", Description = "Plan from: {previous}", RequiresApproval = true },
                new NodeDefinition { Id = "execute", Name = "Execute", Description = "Execute: {previous}" },
            ],
            Edges =
            [
                new EdgeDefinition { From = "research", To = "plan" },
                new EdgeDefinition { From = "plan", To = "execute" },
            ],
        };

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            RoutingStrategy = new WorkflowRoutingStrategy(workflow),
            UserInput = "Prepare the release plan",
        }))
        {
            events.Add(evt);
        }

        gate.RequestCount.Should().Be(1);
        var completed = events.OfType<LoopCompletedEvent>().Single();
        completed.Reason.Should().Be(LoopStopReason.AgentCompleted);
    }

    [Fact]
    public async Task ParallelSubAgentsAndWorkflowFanOut_DelegatesThenExecutesWorkflow()
    {
        var services = new ServiceCollection();
        services.AddNexus(nexus =>
        {
            nexus.UseChatClient(_ => new FakeChatClient("approved merge", "approved publish"));
            nexus.AddOrchestration(o => o.UseDefaults());
            nexus.AddStandardTools(tools => tools.Only(StandardToolCategory.Agents));
        });
        services.AddWorkflowDsl();

        await using var provider = services.BuildServiceProvider();
        var agentTool = new AgentTool(new RecordingAgentPool(), provider);

        var toolResult = await agentTool.ExecuteAsync(JsonDocument.Parse("""
            {
              "maxConcurrency": 2,
              "tasks": [
                { "agent": "Researcher", "task": "Collect facts" },
                { "agent": "Reviewer", "task": "Review draft" }
              ]
            }
            """).RootElement.Clone(), new RecipeToolContext(provider.GetRequiredService<IToolRegistry>()), CancellationToken.None);

        toolResult.IsSuccess.Should().BeTrue();
        ((AgentBatchToolResult)toolResult.Value!).CompletedCount.Should().Be(2);

        var executor = provider.GetRequiredService<IWorkflowExecutor>();
        var workflowResult = await executor.ExecuteAsync(new WorkflowDefinition
        {
            Id = "fanout-merge",
            Name = "Fanout Merge",
            Nodes =
            [
                new NodeDefinition { Id = "merge", Name = "Merge", Description = "Merge findings" },
                new NodeDefinition { Id = "publish", Name = "Publish", Description = "Publish findings" },
            ],
            Edges = [new EdgeDefinition { From = "merge", To = "publish", Condition = "result.text.contains('approved')" }],
        });

        workflowResult.Status.Should().Be(OrchestrationStatus.Completed);
        workflowResult.TaskResults.Should().HaveCount(2);
    }

    private static async Task DrainAsync(IAsyncEnumerable<AgentLoopEvent> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private sealed class ApprovedWithModifiedOutputGate : IApprovalGate
    {
        private readonly string _output;
        public int RequestCount { get; private set; }

        public ApprovedWithModifiedOutputGate(string output)
        {
            _output = output;
        }

        public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            RequestCount++;
            var modified = JsonSerializer.SerializeToElement(new { Output = _output });
            return Task.FromResult(new ApprovalResult(true, "test-reviewer", ModifiedContext: modified));
        }
    }

    private sealed class RecipeToolContext : IToolContext
    {
        public RecipeToolContext(IToolRegistry tools)
        {
            Tools = tools;
        }

        public IToolRegistry Tools { get; }
        public ISecretProvider? Secrets => null;
        public IBudgetTracker? Budget => null;
        public CorrelationContext Correlation { get; } = new() { TraceId = "recipes", SpanId = "tests" };
        public AgentId AgentId => AgentId.New();
    }

    private sealed class RecordingAgentPool : IAgentPool
    {
        public IReadOnlyList<IAgent> ActiveAgents => _agents.Values.ToList();
        public IObservable<AgentLifecycleEvent> Lifecycle { get; } = EmptyObservable<AgentLifecycleEvent>.Instance;

        private readonly Dictionary<AgentId, IAgent> _agents = [];

        public Task<IAgent> SpawnAsync(AgentDefinition definition, CancellationToken ct = default)
        {
            var agent = MockAgent.AlwaysReturns($"{definition.Name}:{definition.SystemPrompt}", definition.Name ?? "worker");
            _agents[agent.Id] = agent;
            return Task.FromResult<IAgent>(agent);
        }

        public Task PauseAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task ResumeAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task KillAsync(AgentId id, CancellationToken ct = default)
        {
            _agents.Remove(id);
            return Task.CompletedTask;
        }

        public Task DrainAsync(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;

        public Task CheckpointAndStopAllAsync(ICheckpointStore store, CancellationToken ct = default)
        {
            _agents.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyObservable<T> : IObservable<T>
    {
        public static EmptyObservable<T> Instance { get; } = new();

        public IDisposable Subscribe(IObserver<T> observer) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}