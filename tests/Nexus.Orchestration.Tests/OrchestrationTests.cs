using FluentAssertions;
using Nexus.Core.Agents;
using Nexus.Orchestration;
using Nexus.Orchestration.Checkpointing;

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
