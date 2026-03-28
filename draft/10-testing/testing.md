# Testing — Nexus.Testing

> Assembly: `Nexus.Testing`  
> Deps: `Nexus.Core`, `xUnit`

## 1. Mocks & Fakes

### FakeChatClient

```csharp
public class FakeChatClient : IChatClient
{
    private readonly Queue<string> _responses;
    public FakeChatClient(params string[] responses) => _responses = new(responses);
    public List<ChatMessage> ReceivedMessages { get; } = [];

    public Task<ChatResponse> GetResponseAsync(...) { ... }
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(...) { ... }
}

// Nutzung:
var client = new FakeChatClient("Hello!", "The answer is 42.");
var agent = new ChatAgent(client, new() { SystemPrompt = "..." });
var result = await agent.ExecuteAsync(task, ctx);
Assert.Equal("Hello!", result.Text);
```

### MockAgent

```csharp
public class MockAgent : IAgent
{
    public static MockAgent WithResponses(params (string input, string output)[] responses);
    public static MockAgent AlwaysReturns(string output);
    public static MockAgent AlwaysFails(string error);
    public List<AgentTask> ReceivedTasks { get; }
}
```

### MockTool

```csharp
public class MockTool : ITool
{
    public static MockTool WithHandler(string name, Func<JsonElement, ToolResult> handler);
    public static MockTool AlwaysReturns(string name, object result);
    public List<JsonElement> ReceivedInputs { get; }
}
```

### MockApprovalGate

```csharp
public class MockApprovalGate : IApprovalGate
{
    public static MockApprovalGate AutoApprove();
    public static MockApprovalGate AutoDeny(string reason);
    public static MockApprovalGate ApproveNth(int n);
    public List<ApprovalRequest> ReceivedRequests { get; }
}
```

## 2. EventRecorder

```csharp
public class EventRecorder
{
    public IReadOnlyList<AgentEvent> Events { get; }

    public static (IAgent agent, EventRecorder recorder) Wrap(IAgent agent);
}

// Nutzung:
var (wrappedAgent, recorder) = EventRecorder.Wrap(myAgent);
await wrappedAgent.ExecuteAsync(task, ctx);

Assert.Contains(recorder.Events, e => e is ToolCallStartedEvent tc && tc.ToolName == "web_search");
Assert.Single(recorder.Events.OfType<AgentCompletedEvent>());
```

### EventAssertions (Fluent)

```csharp
recorder.Events.Should()
    .ContainToolCall("web_search")
    .ContainTextChunk(containing: "result")
    .HaveCompletedSuccessfully()
    .HaveCostBelow(0.50m);
```

## 3. Agent Evaluation

```csharp
public interface IAgentEvaluator
{
    Task<EvaluationReport> EvaluateAsync(IAgent agent, IReadOnlyList<EvaluationCase> cases, CancellationToken ct);
}

public record EvaluationCase
{
    public required string Name { get; init; }
    public required string Input { get; init; }
    public required Func<AgentResult, bool> Assertion { get; init; }
    public string? ExpectedOutput { get; init; }
    public TimeSpan? MaxDuration { get; init; }
    public decimal? MaxCost { get; init; }
}

public record EvaluationReport
{
    public int TotalCases { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public decimal TotalCost { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public IReadOnlyList<EvaluationCaseResult> Results { get; init; } = [];
}
```

## 4. NexusTestHost

```csharp
public class NexusTestHost : IAsyncDisposable
{
    public static NexusTestHost Create(Action<NexusBuilder>? configure = null);
    public IAgentPool AgentPool { get; }
    public IOrchestrator Orchestrator { get; }
    public IToolRegistry Tools { get; }
    public FakeChatClient ChatClient { get; }
}

// Nutzung:
await using var host = NexusTestHost.Create(n =>
{
    n.UseChatClient(new FakeChatClient("Response"));
    n.AddOrchestration();
    n.AddTools(t => t.Add(MockTool.AlwaysReturns("search", "result")));
});

var agent = await host.AgentPool.SpawnAsync(ChatAgent.Define("Test", ...));
var result = await agent.ExecuteAsync(task, ctx);
```

## 5. Integration Tests mit Testcontainers

```csharp
[Collection("Integration")]
public class RedisMessagingTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public async Task InitializeAsync() => await _redis.StartAsync();
    public async Task DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task PubSub_DeliversMessages()
    {
        var bus = new RedisMessageBus(_redis.GetConnectionString());
        // ...
    }
}
```

## 6. Ordnerstruktur

```
Nexus.Testing/
├── Mocks/
│   ├── MockAgent.cs, FakeChatClient.cs
│   ├── MockTool.cs, MockApprovalGate.cs
├── Recording/
│   ├── EventRecorder.cs, EventAssertions.cs
├── Evaluation/
│   ├── IAgentEvaluator.cs, EvaluationCase.cs, EvaluationReport.cs
└── Helpers/
    └── NexusTestHost.cs
```
