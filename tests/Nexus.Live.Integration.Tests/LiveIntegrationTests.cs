using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.CostTracking;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Orchestration.Defaults;
using Nexus.Orchestration.Middleware;
using Nexus.Sessions;
using Xunit;

namespace Nexus.Live.Integration.Tests;

public sealed class LiveOllamaChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_Returns_Text()
    {
        var env = await LiveTestLogging.CreateEnvironmentOrSkipAsync(nameof(GetResponseAsync_Returns_Text));
        if (env is null)
            return;

        await using (env)
        {
            var response = await env.ChatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with exactly HELLO")]);

            response.Messages.Should().NotBeEmpty();
            response.Messages[0].Text.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_Yields_Chunks()
    {
        var env = await LiveTestLogging.CreateEnvironmentOrSkipAsync(nameof(GetStreamingResponseAsync_Yields_Chunks));
        if (env is null)
            return;

        await using (env)
        {
            var chunks = new List<string>();
            await foreach (var update in env.ChatClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Count from 1 to 3")]))
            {
                if (!string.IsNullOrWhiteSpace(update.Text))
                    chunks.Add(update.Text);
            }

            chunks.Should().NotBeEmpty();
        }
    }
}

public sealed class LiveRuntimeIntegrationTests
{
    [Fact]
    public async Task ChatAgent_ExecuteAsync_Returns_Success_With_Usage()
    {
        var env = await LiveTestLogging.CreateEnvironmentOrSkipAsync(nameof(ChatAgent_ExecuteAsync_Returns_Success_With_Usage));
        if (env is null)
            return;

        await using (env)
        {
            var agent = new ChatAgent("live", env.ChatClient);
            var context = BuildContext(env.ChatClient);
            var result = await agent.ExecuteAsync(AgentTask.Create("Answer in one short sentence: what is 2+2?"), context);

            result.Status.Should().Be(AgentResultStatus.Success);
            result.Text.Should().NotBeNullOrWhiteSpace();
            result.TokenUsage.Should().NotBeNull();
            result.TokenUsage!.TotalTokens.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task DefaultOrchestrator_ExecuteGraphAsync_Completes_Live_Run()
    {
        var env = await LiveTestLogging.CreateEnvironmentOrSkipAsync(nameof(DefaultOrchestrator_ExecuteGraphAsync_Completes_Live_Run));
        if (env is null)
            return;

        await using (env)
        {
            var services = BuildServiceProvider(env.ChatClient);
            var pool = new DefaultAgentPool(services);
            using var orchestrator = new DefaultOrchestrator(pool, services);

            var graph = orchestrator.CreateGraph();
            var task = AgentTask.Create("Reply with the word READY only") with
            {
                AgentDefinition = new AgentDefinition { Name = "live" },
            };

            var node = graph.AddTask(task);
            var result = await orchestrator.ExecuteGraphAsync(graph);

            result.TaskResults[node.TaskId].Status.Should().Be(AgentResultStatus.Success);
            result.TaskResults[node.TaskId].Text.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task CostTrackingChatClient_Records_Live_Usage()
    {
        var env = await LiveTestLogging.CreateEnvironmentOrSkipAsync(nameof(CostTrackingChatClient_Records_Live_Usage));
        if (env is null)
            return;

        await using (env)
        {
            var pricingOptions = new CostTrackingOptions().AddModel(env.Model, input: 1m, output: 2m);
            var pricing = new DefaultModelPricingProvider(pricingOptions);
            var tracker = new DefaultCostTracker(pricing);
            using var client = new CostTrackingChatClient(env.ChatClient, tracker, pricing, env.Model);

            var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Say exactly TEST")]);
            var snapshot = await tracker.GetSnapshotAsync();

            response.Messages.Should().NotBeEmpty();
            snapshot.TotalTokens.Should().BeGreaterThan(0);
            snapshot.TotalCost.Should().BeGreaterThan(0m);
        }
    }

    [Fact]
    public async Task DefaultAgentLoop_Persists_And_Resumes_Live_Session()
    {
        var env = await LiveTestLogging.CreateEnvironmentOrSkipAsync(nameof(DefaultAgentLoop_Persists_And_Resumes_Live_Session));
        if (env is null)
            return;

        await using (env)
        {
            var sessionDirectory = Path.Combine(Path.GetTempPath(), "nexus-live-sessions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionDirectory);

            try
            {
                var services = BuildServiceProvider(env.ChatClient, sessionDirectory);
                var loop = services.GetRequiredService<IAgentLoop>();
                var sessionStore = services.GetRequiredService<ISessionStore>();
                var sessionTranscript = services.GetRequiredService<ISessionTranscript>();

                await DrainAsync(loop.RunAsync(new AgentLoopOptions
                {
                    AgentDefinition = new AgentDefinition { Name = "live" },
                    UserInput = "Say hello briefly",
                    SessionTitle = "live-run",
                }));

                await DrainAsync(loop.RunAsync(new AgentLoopOptions
                {
                    AgentDefinition = new AgentDefinition { Name = "live" },
                    ResumeLastSession = true,
                    UserInput = "Now say goodbye briefly",
                }));

                var session = await FirstSessionAsync(sessionStore);
                session.Should().NotBeNull();
                session!.MessageCount.Should().BeGreaterOrEqualTo(4);

                var transcript = new List<ChatMessage>();
                await foreach (var message in sessionTranscript.ReadAsync(session.Id))
                    transcript.Add(message);

                transcript.Should().HaveCountGreaterOrEqualTo(4);
                env.ChatClient.ReceivedMessages.Last().Select(m => m.Text).Should().Contain("Say hello briefly");
            }
            finally
            {
                if (Directory.Exists(sessionDirectory))
                    Directory.Delete(sessionDirectory, recursive: true);
            }
        }
    }

    private static TestAgentContext BuildContext(IChatClient client)
    {
        var services = BuildServiceProvider(client);
        var agent = new ChatAgent("ctx", client);
        return new TestAgentContext(agent, services);
    }

    private static ServiceProvider BuildServiceProvider(IChatClient client, string? sessionDirectory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(client);
        services.AddSingleton<IChatClient>(client);
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        services.AddSingleton<IApprovalGate, AutoApproveGate>();
        services.AddSingleton<IAgentMiddleware, BudgetGuardMiddleware>();
        services.AddSingleton<IBudgetTracker, Nexus.CostTracking.DefaultBudgetTracker>();
        services.AddSingleton<IAgentPool, DefaultAgentPool>();

        if (sessionDirectory is null)
        {
            services.AddSingleton<InMemorySessionStore>();
            services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<InMemorySessionStore>());
            services.AddSingleton<ISessionTranscript>(sp => sp.GetRequiredService<InMemorySessionStore>());
        }
        else
        {
            services.AddSingleton<FileSessionStore>(_ => new FileSessionStore(sessionDirectory));
            services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<FileSessionStore>());
            services.AddSingleton<ISessionTranscript>(sp => sp.GetRequiredService<FileSessionStore>());
        }

        services.AddSingleton<IAgentLoop, DefaultAgentLoop>();
        return services.BuildServiceProvider();
    }

    private static async Task DrainAsync(IAsyncEnumerable<AgentLoopEvent> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private static async Task<SessionInfo?> FirstSessionAsync(ISessionStore store)
    {
        await foreach (var session in store.ListAsync())
            return session;

        return null;
    }

    private sealed class TestAgentContext : IAgentContext
    {
        private readonly IServiceProvider _services;

        public TestAgentContext(IAgent agent, IServiceProvider services)
        {
            Agent = agent;
            _services = services;
        }

        public IAgent Agent { get; }
        public IChatClient GetChatClient(string? name = null) => _services.GetRequiredService<IChatClient>();
        public IToolRegistry Tools => _services.GetRequiredService<IToolRegistry>();
        public IConversationStore? Conversations => null;
        public IWorkingMemory? WorkingMemory => null;
        public IMessageBus? MessageBus => null;
        public IApprovalGate? ApprovalGate => _services.GetRequiredService<IApprovalGate>();
        public IBudgetTracker? Budget => _services.GetRequiredService<IBudgetTracker>();
        public ISecretProvider? Secrets => null;
        public CorrelationContext Correlation { get; } = CorrelationContext.New();

        public Task<IAgent> SpawnChildAsync(AgentDefinition definition, CancellationToken ct = default)
            => _services.GetRequiredService<IAgentPool>().SpawnAsync(definition, ct);
    }
}

internal static class LiveTestLogging
{
    public static async Task<OllamaLiveTestEnvironment?> CreateEnvironmentOrSkipAsync(string testName)
    {
        var env = await OllamaLiveTestEnvironment.CreateAsync().ConfigureAwait(false);
        if (env is null)
        {
            Console.WriteLine($"[live-test] {testName}: skipped because no reachable Ollama endpoint or no installed model was found.");
            return null;
        }

        Console.WriteLine($"[live-test] {testName}: endpoint={env.Endpoint}, model={env.Model}");
        return env;
    }
}