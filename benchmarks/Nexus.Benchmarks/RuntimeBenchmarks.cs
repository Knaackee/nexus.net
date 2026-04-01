using System.Runtime.CompilerServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Orchestration.Defaults;
using Nexus.Tools.Standard;
using Nexus.Workflows.Dsl;

namespace Nexus.Benchmarks;

[MemoryDiagnoser]
public class WorkflowRuntimeBenchmarks
{
    private ServiceProvider _provider = null!;
    private IWorkflowGraphCompiler _compiler = null!;
    private IWorkflowExecutor _executor = null!;
    private WorkflowDefinition _workflow = null!;
    private Dictionary<string, object> _variables = null!;

    [GlobalSetup]
    public void Setup()
    {
        _provider = new ServiceCollection()
            .AddSingleton<IChatClient>(_ => new ConstantChatClient("approved"))
            .AddSingleton<IToolRegistry, DefaultToolRegistry>()
            .AddSingleton<IAgentPool, DefaultAgentPool>()
            .AddSingleton<IOrchestrator, DefaultOrchestrator>()
            .AddWorkflowDsl()
            .BuildServiceProvider();

        _compiler = _provider.GetRequiredService<IWorkflowGraphCompiler>();
        _executor = _provider.GetRequiredService<IWorkflowExecutor>();
        _variables = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["topic"] = "incident routing"
        };

        _workflow = new WorkflowDefinition
        {
            Id = "benchmark-flow",
            Name = "Benchmark Flow",
            Nodes =
            [
                new NodeDefinition { Id = "intake", Name = "Intake", Description = "Classify ${topic}" },
                new NodeDefinition { Id = "research", Name = "Research", Description = "Research ${topic}" },
                new NodeDefinition { Id = "review", Name = "Review", Description = "Review ${topic}" },
                new NodeDefinition { Id = "publish", Name = "Publish", Description = "Publish if approved" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "intake", To = "research" },
                new EdgeDefinition { From = "intake", To = "review" },
                new EdgeDefinition { From = "review", To = "publish", Condition = "result.text.contains('approved')" }
            ],
            Options = new WorkflowOptions { MaxConcurrentNodes = 2, GlobalTimeoutSeconds = 30 }
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
    }

    [Benchmark]
    public CompiledWorkflow CompileWorkflow()
        => _compiler.Compile(_workflow, _variables);

    [Benchmark]
    public Task<OrchestrationResult> ExecuteWorkflow()
        => _executor.ExecuteAsync(_workflow, _variables);
}

[MemoryDiagnoser]
public class SubAgentBenchmarks
{
    private ServiceProvider _provider = null!;
    private AgentTool _tool = null!;
    private JsonElement _payload;

    [GlobalSetup]
    public void Setup()
    {
        _provider = new ServiceCollection().BuildServiceProvider();
        _tool = new AgentTool(new BenchmarkAgentPool(), _provider);
        _payload = JsonDocument.Parse("""
            {
              "maxConcurrency": 3,
              "tasks": [
                { "agent": "Researcher", "task": "Gather facts" },
                { "agent": "Reviewer", "task": "Check correctness" },
                { "agent": "Planner", "task": "Propose next slice" }
              ]
            }
            """).RootElement.Clone();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
    }

    [Benchmark]
    public Task<ToolResult> RunParallelSubAgents()
        => _tool.ExecuteAsync(_payload, new BenchmarkToolContext(), CancellationToken.None);

    private sealed class BenchmarkToolContext : IToolContext
    {
        public AgentId AgentId { get; } = AgentId.New();
        public IToolRegistry Tools { get; } = new DefaultToolRegistry();
        public ISecretProvider? Secrets => null;
        public IBudgetTracker? Budget => null;
        public CorrelationContext Correlation { get; } = CorrelationContext.New();
    }

    private sealed class BenchmarkAgentPool : IAgentPool
    {
        public IReadOnlyList<IAgent> ActiveAgents => [];
        public IObservable<AgentLifecycleEvent> Lifecycle => throw new NotSupportedException();

        public Task CheckpointAndStopAllAsync(ICheckpointStore store, CancellationToken ct = default) => Task.CompletedTask;

        public Task DrainAsync(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;

        public Task KillAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task PauseAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task ResumeAsync(AgentId id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IAgent> SpawnAsync(AgentDefinition definition, CancellationToken ct = default)
            => Task.FromResult<IAgent>(new BenchmarkAgent(definition.Name));
    }

    private sealed class BenchmarkAgent : IAgent
    {
        public BenchmarkAgent(string name)
        {
            Name = name;
        }

        public AgentId Id { get; } = AgentId.New();
        public string Name { get; }
        public AgentState State => AgentState.Idle;

        public Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
            => Task.FromResult(AgentResult.Success($"{Name}:{task.Description}"));

        public async IAsyncEnumerable<Nexus.Core.Events.AgentEvent> ExecuteStreamingAsync(
            AgentTask task,
            IAgentContext context,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new Nexus.Core.Events.AgentCompletedEvent(Id, await ExecuteAsync(task, context, ct).ConfigureAwait(false));
        }
    }
}

internal sealed class ConstantChatClient : IChatClient
{
    private readonly string _responseText;

    public ConstantChatClient(string responseText)
    {
        _responseText = responseText;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText)));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(_responseText)],
        };

        await Task.Yield();
    }

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}