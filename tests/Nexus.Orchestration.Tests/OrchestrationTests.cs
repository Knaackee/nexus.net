using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Orchestration.Checkpointing;
using Nexus.Orchestration.Defaults;
using Nexus.Orchestration.Middleware;
using System.Reflection;

namespace Nexus.Orchestration.Tests;

public class IdentifierTests
{
    [Fact]
    public void TaskGraphId_New_Creates_Unique()
    {
        var id1 = TaskGraphId.New();
        var id2 = TaskGraphId.New();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CheckpointId_New_Creates_Unique()
    {
        var id1 = CheckpointId.New();
        var id2 = CheckpointId.New();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void TaskGraphId_ToString_Returns_Short_Hex()
    {
        TaskGraphId.New().ToString().Should().HaveLength(8);
    }
}

public class OrchestrationResultTests
{
    [Fact]
    public void Can_Create_With_Required_Properties()
    {
        var result = new OrchestrationResult
        {
            Status = OrchestrationStatus.Completed,
            TaskResults = new Dictionary<TaskId, AgentResult>
            {
                [TaskId.New()] = AgentResult.Success("done")
            }
        };

        result.Status.Should().Be(OrchestrationStatus.Completed);
        result.TaskResults.Should().HaveCount(1);
    }
}

public class OrchestrationOptionsTests
{
    [Fact]
    public void Defaults_Are_Reasonable()
    {
        var options = new OrchestrationOptions();
        options.MaxConcurrentNodes.Should().Be(10);
        options.GlobalTimeout.Should().Be(TimeSpan.FromMinutes(30));
        options.CheckpointStrategy.Should().Be(CheckpointStrategy.AfterEachNode);
    }
}

public class OrchestrationEventsTests
{
    [Fact]
    public void NodeStartedEvent_Creates_With_Timestamp()
    {
        var graphId = TaskGraphId.New();
        var nodeId = TaskId.New();
        var agentId = AgentId.New();

        var evt = new NodeStartedEvent(graphId, nodeId, agentId);

        evt.GraphId.Should().Be(graphId);
        evt.NodeId.Should().Be(nodeId);
        evt.AgentId.Should().Be(agentId);
        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void NodeCompletedEvent_Contains_Result()
    {
        var result = AgentResult.Success("output");
        var evt = new NodeCompletedEvent(TaskGraphId.New(), TaskId.New(), result);
        evt.Result.Text.Should().Be("output");
    }

    [Fact]
    public void NodeFailedEvent_Contains_Error()
    {
        var error = new InvalidOperationException("boom");
        var evt = new NodeFailedEvent(TaskGraphId.New(), TaskId.New(), error);
        evt.Error.Message.Should().Be("boom");
    }
}

public class InMemoryCheckpointStoreTests
{
    private readonly InMemoryCheckpointStore _store = new();

    private static OrchestrationSnapshot CreateSnapshot(
        TaskGraphId? graphId = null,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = CheckpointId.New(),
        GraphId = graphId ?? TaskGraphId.New(),
        NodeStates = new Dictionary<TaskId, TaskNodeState>(),
        CompletedResults = new Dictionary<TaskId, AgentResult>(),
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SaveAsync_And_LoadAsync_Roundtrip()
    {
        var snapshot = CreateSnapshot();
        await _store.SaveAsync(snapshot);
        var loaded = await _store.LoadAsync(snapshot.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(snapshot.Id);
    }

    [Fact]
    public async Task LoadAsync_Returns_Null_For_Unknown()
    {
        var loaded = await _store.LoadAsync(CheckpointId.New());
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task LoadLatestAsync_Returns_Most_Recent()
    {
        var graphId = TaskGraphId.New();
        var snap1 = CreateSnapshot(graphId, DateTimeOffset.UtcNow.AddMinutes(-5));
        var snap2 = CreateSnapshot(graphId, DateTimeOffset.UtcNow);

        await _store.SaveAsync(snap1);
        await _store.SaveAsync(snap2);

        var latest = await _store.LoadLatestAsync(graphId);
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(snap2.Id);
    }

    [Fact]
    public async Task ListAsync_Returns_All_For_Graph()
    {
        var graphId = TaskGraphId.New();
        await _store.SaveAsync(CreateSnapshot(graphId));
        await _store.SaveAsync(CreateSnapshot(graphId));

        var all = await _store.ListAsync(graphId);
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_Removes_Checkpoint()
    {
        var snapshot = CreateSnapshot();
        await _store.SaveAsync(snapshot);
        await _store.DeleteAsync(snapshot.Id);

        var loaded = await _store.LoadAsync(snapshot.Id);
        loaded.Should().BeNull();
    }
}

public class JsonSnapshotSerializerTests
{
    private readonly JsonSnapshotSerializer _serializer = new();

    [Fact]
    public void Serialize_And_Deserialize_Roundtrip()
    {
        var snapshot = new OrchestrationSnapshot
        {
            Id = CheckpointId.New(),
            GraphId = TaskGraphId.New(),
            NodeStates = new Dictionary<TaskId, TaskNodeState>
            {
                [TaskId.New()] = TaskNodeState.Completed
            },
            CompletedResults = new Dictionary<TaskId, AgentResult>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var bytes = _serializer.Serialize(snapshot);
        var deserialized = _serializer.Deserialize(bytes);

        deserialized.Should().NotBeNull();
        deserialized.Id.Should().Be(snapshot.Id);
    }
}

public class DefaultOrchestratorIntegrationTests
{
    [Fact]
    public async Task ExecuteGraphStreamingAsync_ReusesAssignedAgent()
    {
        using var services = BuildServices(new UsageAwareOrchestrationChatClient());
        var pool = new DefaultAgentPool(services);
        using var orchestrator = new DefaultOrchestrator(pool, services);

        var agent = await pool.SpawnAsync(new AgentDefinition { Name = "researcher" });
        var graph = orchestrator.CreateGraph();
        var task = AgentTask.Create("Summarize this") with { AssignedAgent = agent.Id };
        graph.AddTask(task);

        var events = await CollectAsync(orchestrator.ExecuteGraphStreamingAsync(graph));

        events.OfType<NodeStartedEvent>().Single().AgentId.Should().Be(agent.Id);
        pool.ActiveAgents.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteGraphAsync_EnforcesMaxCostBudgetViaDefaultMiddleware()
    {
        using var services = BuildServices(new UsageAwareOrchestrationChatClient(inputTokens: 100, outputTokens: 20, estimatedCost: 0.25m));
        var pool = new DefaultAgentPool(services);
        using var orchestrator = new DefaultOrchestrator(pool, services);

        var agent = await pool.SpawnAsync(new AgentDefinition
        {
            Name = "budgeted",
            Budget = new AgentBudget { MaxCostUsd = 0.10m }
        });

        var graph = orchestrator.CreateGraph();
        var task = AgentTask.Create("Answer") with { AssignedAgent = agent.Id };
        var node = graph.AddTask(task);

        var result = await orchestrator.ExecuteGraphAsync(graph);

        result.TaskResults[node.TaskId].Status.Should().Be(AgentResultStatus.BudgetExceeded);
        result.TaskResults[node.TaskId].EstimatedCost.Should().Be(0.25m);
        result.TaskResults[node.TaskId].TokenUsage.Should().NotBeNull();
        result.TaskResults[node.TaskId].TokenUsage!.TotalTokens.Should().Be(120);
    }

    private static ServiceProvider BuildServices(UsageAwareOrchestrationChatClient client)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(client);
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        services.AddSingleton<IBudgetTracker, TestBudgetTracker>();
        services.AddSingleton<IAgentMiddleware, BudgetGuardMiddleware>();
        return services.BuildServiceProvider();
    }

    private static async Task<List<OrchestrationEvent>> CollectAsync(IAsyncEnumerable<OrchestrationEvent> stream)
    {
        var events = new List<OrchestrationEvent>();
        await foreach (var evt in stream)
            events.Add(evt);
        return events;
    }
}

internal sealed class TestBudgetTracker : IBudgetTracker
{
    private readonly Dictionary<AgentId, (int Input, int Output, decimal Cost, AgentBudget? Limit)> _entries = [];

    public Task TrackUsageAsync(AgentId agentId, int inputTokens, int outputTokens, decimal? cost, CancellationToken ct = default)
    {
        _entries.TryGetValue(agentId, out var current);
        _entries[agentId] = (
            current.Input + inputTokens,
            current.Output + outputTokens,
            current.Cost + (cost ?? 0m),
            current.Limit);
        return Task.CompletedTask;
    }

    public Task<BudgetStatus> GetStatusAsync(AgentId agentId, CancellationToken ct = default)
    {
        _entries.TryGetValue(agentId, out var current);
        var exhausted = current.Limit?.MaxCostUsd is decimal maxCost && current.Cost >= maxCost;
        return Task.FromResult(new BudgetStatus(
            current.Input,
            current.Output,
            current.Cost,
            current.Limit,
            exhausted));
    }

    public async Task<bool> HasBudgetAsync(AgentId agentId, CancellationToken ct = default)
        => !(await GetStatusAsync(agentId, ct).ConfigureAwait(false)).IsExhausted;

    public Task SetLimitAsync(AgentId agentId, AgentBudget? limit, CancellationToken ct = default)
    {
        _entries.TryGetValue(agentId, out var current);
        _entries[agentId] = (current.Input, current.Output, current.Cost, limit);
        return Task.CompletedTask;
    }
}

internal sealed class UsageAwareOrchestrationChatClient : IChatClient
{
    private readonly int _inputTokens;
    private readonly int _outputTokens;
    private readonly decimal _estimatedCost;

    public UsageAwareOrchestrationChatClient(int inputTokens = 10, int outputTokens = 5, decimal estimatedCost = 0.01m)
    {
        _inputTokens = inputTokens;
        _outputTokens = outputTokens;
        _estimatedCost = estimatedCost;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ignored")));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("result")],
        };

        var usageUpdate = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [],
        };
        SetTrackingMetadata(usageUpdate, _inputTokens, _outputTokens, _estimatedCost);
        yield return usageUpdate;
        await Task.Yield();
    }

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private static void SetTrackingMetadata(object target, int inputTokens, int outputTokens, decimal estimatedCost)
    {
        SetPropertyIfExists(target, "ModelId", "test-model");

        var usage = Activator.CreateInstance(typeof(ChatResponse).GetProperty("Usage", BindingFlags.Instance | BindingFlags.Public)!.PropertyType);
        if (usage is not null)
        {
            SetPropertyIfExists(usage, "InputTokenCount", inputTokens);
            SetPropertyIfExists(usage, "OutputTokenCount", outputTokens);
            SetPropertyIfExists(usage, "TotalTokenCount", inputTokens + outputTokens);
            SetAdditionalProperty(target, "Usage", usage);
        }

        SetAdditionalProperty(target, "NexusEstimatedCost", estimatedCost);
    }

    private static void SetAdditionalProperty(object target, string key, object value)
    {
        var additionalPropertiesProperty = target.GetType().GetProperty("AdditionalProperties", BindingFlags.Instance | BindingFlags.Public);
        if (additionalPropertiesProperty?.CanWrite != true)
            return;

        var dictionary = additionalPropertiesProperty.GetValue(target);
        if (dictionary is null)
        {
            dictionary = Activator.CreateInstance(additionalPropertiesProperty.PropertyType);
            additionalPropertiesProperty.SetValue(target, dictionary);
        }

        var indexer = dictionary?.GetType().GetProperty("Item");
        indexer?.SetValue(dictionary, value, [key]);
    }

    private static void SetPropertyIfExists(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite == true)
            property.SetValue(target, ConvertValue(value, property.PropertyType));
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(value))
            return value;

        return Convert.ChangeType(value, effectiveType, System.Globalization.CultureInfo.InvariantCulture);
    }
}
