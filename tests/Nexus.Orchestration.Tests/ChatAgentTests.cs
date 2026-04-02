using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Tools;
using Nexus.Testing.Mocks;
using System.Reflection;
using System.Text.Json;

namespace Nexus.Orchestration.Tests;

public class ChatAgentTests
{
    private static IAgentContext CreateContext(
        IToolRegistry? tools = null,
        IApprovalGate? approvalGate = null)
    {
        var context = Substitute.For<IAgentContext>();
        context.Tools.Returns(tools ?? new DefaultToolRegistry());
        context.ApprovalGate.Returns(approvalGate);
        context.Correlation.Returns(new CorrelationContext
        {
            TraceId = "test-trace",
            SpanId = "test-span"
        });
        return context;
    }

    private static async Task<List<AgentEvent>> CollectEvents(
        IAsyncEnumerable<AgentEvent> stream)
    {
        var events = new List<AgentEvent>();
        await foreach (var e in stream)
            events.Add(e);
        return events;
    }

    // ──────────────────────────────────────────────────
    // Basic text response (no tools)
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task SimpleTextResponse_ReturnsSuccessWithText()
    {
        var client = new FakeChatClient("Hello world");
        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        var result = await agent.ExecuteAsync(AgentTask.Create("Say hi"), context);

        result.Status.Should().Be(AgentResultStatus.Success);
        result.Text.Should().Be("Hello world");
    }

    [Fact]
    public async Task SimpleTextResponse_StreamsTextChunks()
    {
        var client = new FakeChatClient().WithStreamingResponse("Hello", " world");
        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Say hi"), context));

        events.OfType<TextChunkEvent>().Select(e => e.Text)
            .Should().ContainInOrder("Hello", " world");
    }

    [Fact]
    public async Task ReasoningResponse_StreamsReasoningSeparately_AndPreservesContents()
    {
        var client = new FakeChatClient().WithReasoningResponse("Need to reason.", "Final answer");
        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Solve it"), context));

        events.OfType<ReasoningChunkEvent>().Select(e => e.Text)
            .Should().ContainSingle().Which.Should().Be("Need to reason.");
        events.OfType<TextChunkEvent>().Select(e => e.Text)
            .Should().ContainSingle().Which.Should().Be("Final answer");

        var completed = events.OfType<AgentCompletedEvent>().Single().Result;
        completed.Text.Should().Be("Final answer");
        completed.Contents.Should().NotBeNull();
        completed.Contents!.Select(content => content.GetType()).Should().ContainInOrder(
            typeof(TextReasoningContent),
            typeof(TextContent));
    }

    [Fact]
    public async Task SimpleTextResponse_EmitsCorrectEventSequence()
    {
        var client = new FakeChatClient("Done");
        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Go"), context));

        events[0].Should().BeOfType<AgentStateChangedEvent>()
            .Which.NewState.Should().Be(AgentState.Running);
        events[1].Should().BeOfType<AgentIterationEvent>()
            .Which.Iteration.Should().Be(1);
        events.Should().ContainSingle(e => e is AgentCompletedEvent);
        events.Last().Should().BeOfType<AgentCompletedEvent>();
    }

    [Fact]
    public async Task SimpleTextResponse_SetsStateToCompleted()
    {
        var client = new FakeChatClient("Done");
        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        agent.State.Should().Be(AgentState.Created);

        await agent.ExecuteAsync(AgentTask.Create("Go"), context);

        agent.State.Should().Be(AgentState.Completed);
    }

    // ──────────────────────────────────────────────────
    // Tool execution (happy path)
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task ToolCall_ExecutesToolAndReturnsResult()
    {
        var tool = MockTool.AlwaysReturns("get_time", "12:00");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("call-1", "get_time"))
            .WithResponse("The time is 12:00");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry);

        var result = await agent.ExecuteAsync(AgentTask.Create("What time is it?"), context);

        result.Status.Should().Be(AgentResultStatus.Success);
        result.Text.Should().Be("The time is 12:00");
        tool.ReceivedInputs.Should().HaveCount(1);
    }

    [Fact]
    public async Task ToolCall_EmitsStartedAndCompletedEvents()
    {
        var tool = MockTool.AlwaysReturns("read_file", "content");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("call-1", "read_file"))
            .WithResponse("Here is the content");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry);

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Read it"), context));

        events.Should().ContainSingle(e => e is ToolCallStartedEvent)
            .Which.As<ToolCallStartedEvent>().ToolName.Should().Be("read_file");
        events.Should().ContainSingle(e => e is ToolCallCompletedEvent)
            .Which.As<ToolCallCompletedEvent>().Result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AskUserToolCall_EmitsStructuredUserInputRequestedEvent()
    {
        var tool = MockTool.AlwaysReturns("ask_user", "yes");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("call-ask", "ask_user", new Dictionary<string, object?>
            {
                ["question"] = "Continue with deployment?",
                ["type"] = "confirm",
                ["options"] = new List<string> { "yes", "no" },
                ["reason"] = "Need human confirmation"
            }))
            .WithResponse("User confirmed");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry);

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Ask the user"), context));

        var request = events.OfType<UserInputRequestedEvent>().Single();
        request.RequestId.Should().Be("call-ask");
        request.Request.InputType.Should().Be("confirm");
        request.Request.Question.Should().Be("Continue with deployment?");
        request.Request.Options.Should().ContainInOrder("yes", "no");
        request.Request.Reason.Should().Be("Need human confirmation");
    }

    [Fact]
    public async Task ToolNotFound_SendsErrorToLLM()
    {
        var registry = new DefaultToolRegistry(); // empty — no tools registered

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("call-1", "nonexistent"))
            .WithResponse("Sorry, I couldn't find that tool");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry);

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Do it"), context));

        var completed = events.OfType<ToolCallCompletedEvent>().Single();
        completed.Result.IsSuccess.Should().BeFalse();
        completed.Result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ToolFails_ErrorSentBackToLLM()
    {
        var tool = MockTool.AlwaysFails("write_file", "Permission denied");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("call-1", "write_file"))
            .WithResponse("Could not write the file");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry);

        var result = await agent.ExecuteAsync(AgentTask.Create("Write it"), context);

        result.Status.Should().Be(AgentResultStatus.Success);
        result.Text.Should().Contain("Could not write");
    }

    // ──────────────────────────────────────────────────
    // No double LLM call (T2.2 regression test)
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task NoDoubleLLMCall_OnlyOneCallPerIteration()
    {
        var client = new FakeChatClient("Simple answer");
        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        await agent.ExecuteAsync(AgentTask.Create("Question"), context);

        // FakeChatClient records each call. With the fix, there should be
        // exactly 1 call (streaming only), not 2 (streaming + full).
        client.ReceivedMessages.Should().HaveCount(1);
    }

    [Fact]
    public async Task NoDoubleLLMCall_ToolFlowAlsoSingleCallPerIteration()
    {
        var tool = MockTool.AlwaysReturns("ping", "pong");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "ping"))
            .WithResponse("Got pong");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry);

        await agent.ExecuteAsync(AgentTask.Create("Ping"), context);

        // 2 iterations (tool call + final answer) = 2 LLM calls total
        // Before fix: would be 4 (2 per iteration)
        client.ReceivedMessages.Should().HaveCount(2);
    }

    // ──────────────────────────────────────────────────
    // Tool Approval — Approved (T2.1)
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task ToolApproval_Approved_ToolExecutes()
    {
        var tool = MockTool.AlwaysReturns("delete_file", "deleted")
            .WithAnnotations(new ToolAnnotations { RequiresApproval = true });
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var gate = MockApprovalGate.AutoApprove();
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "delete_file"))
            .WithResponse("File deleted");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: gate);

        var result = await agent.ExecuteAsync(AgentTask.Create("Delete it"), context);

        result.Status.Should().Be(AgentResultStatus.Success);
        tool.ReceivedInputs.Should().HaveCount(1);
        gate.ReceivedRequests.Should().HaveCount(1);
        gate.ReceivedRequests[0].ToolName.Should().Be("delete_file");
    }

    [Fact]
    public async Task ToolApproval_Approved_EmitsApprovalEvents()
    {
        var tool = MockTool.AlwaysReturns("shell", "ok")
            .WithAnnotations(new ToolAnnotations { RequiresApproval = true });
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var gate = MockApprovalGate.AutoApprove();
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "shell"))
            .WithResponse("Done");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: gate);

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Run it"), context));

        // Should transition: Running → WaitingForApproval → Running
        var stateChanges = events.OfType<AgentStateChangedEvent>().ToList();
        stateChanges.Should().Contain(e =>
            e.OldState == AgentState.Running && e.NewState == AgentState.WaitingForApproval);
        stateChanges.Should().Contain(e =>
            e.OldState == AgentState.WaitingForApproval && e.NewState == AgentState.Running);

        events.Should().ContainSingle(e => e is ApprovalRequestedEvent);
    }

    // ──────────────────────────────────────────────────
    // Tool Approval — Denied (T2.1)
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task ToolApproval_Denied_ToolNotExecuted()
    {
        var tool = MockTool.AlwaysReturns("rm_rf", "destroyed")
            .WithAnnotations(new ToolAnnotations { RequiresApproval = true });
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var gate = MockApprovalGate.AutoDeny();
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "rm_rf"))
            .WithResponse("I was denied access");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: gate);

        var result = await agent.ExecuteAsync(AgentTask.Create("Delete everything"), context);

        tool.ReceivedInputs.Should().BeEmpty("tool should not execute when denied");
        gate.ReceivedRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ToolApproval_Denied_DeniedResultSentToLLM()
    {
        var tool = MockTool.AlwaysReturns("shell", "ok")
            .WithAnnotations(new ToolAnnotations { RequiresApproval = true });
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var gate = MockApprovalGate.AutoDeny();
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "shell"))
            .WithResponse("Access denied, trying another approach");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: gate);

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Run shell"), context));

        var completed = events.OfType<ToolCallCompletedEvent>().Single();
        completed.Result.IsSuccess.Should().BeFalse();
        completed.Result.Error.Should().Contain("DENIED");
    }

    // ──────────────────────────────────────────────────
    // Tool Approval — No gate registered (T2.1)
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task ToolApproval_NoGate_ToolExecutesRegardless()
    {
        var tool = MockTool.AlwaysReturns("dangerous_tool", "boom")
            .WithAnnotations(new ToolAnnotations { RequiresApproval = true });
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "dangerous_tool"))
            .WithResponse("Result: boom");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: null);

        var result = await agent.ExecuteAsync(AgentTask.Create("Do it"), context);

        result.Status.Should().Be(AgentResultStatus.Success);
        tool.ReceivedInputs.Should().HaveCount(1, "no gate = no check, tool runs");
    }

    // ──────────────────────────────────────────────────
    // Tool without RequiresApproval (T2.1)
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task ToolWithoutApproval_GateNotCalled()
    {
        var tool = MockTool.AlwaysReturns("read_file", "content")
            .WithAnnotations(new ToolAnnotations { IsReadOnly = true, RequiresApproval = false });
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var gate = MockApprovalGate.AutoDeny(); // would deny if called
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "read_file"))
            .WithResponse("Content is here");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: gate);

        var result = await agent.ExecuteAsync(AgentTask.Create("Read it"), context);

        result.Status.Should().Be(AgentResultStatus.Success);
        tool.ReceivedInputs.Should().HaveCount(1);
        gate.ReceivedRequests.Should().BeEmpty("gate should not be called for non-approval tools");
    }

    [Fact]
    public async Task ToolWithNullAnnotations_GateNotCalled()
    {
        // MockTool without WithAnnotations() → Annotations is null
        var tool = MockTool.AlwaysReturns("simple_tool", "result");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var gate = MockApprovalGate.AutoDeny();
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("c1", "simple_tool"))
            .WithResponse("Got result");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: gate);

        await agent.ExecuteAsync(AgentTask.Create("Do it"), context);

        gate.ReceivedRequests.Should().BeEmpty("null annotations = no approval needed");
    }

    // ──────────────────────────────────────────────────
    // MaxIterations exceeded
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task MaxIterations_Exceeded_ReturnsFailed()
    {
        // Agent always calls tools, never produces a text-only response
        var tool = MockTool.AlwaysReturns("loop_tool", "again");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient();
        // Queue 3 function call responses (for MaxIterations=3), no text response to stop
        for (int i = 0; i < 3; i++)
            client.WithFunctionCallResponse(new FunctionCallContent($"c{i}", "loop_tool"));

        var agent = new ChatAgent("test", client, new ChatAgentOptions { MaxIterations = 3 });
        var context = CreateContext(tools: registry);

        var result = await agent.ExecuteAsync(AgentTask.Create("Loop forever"), context);

        result.Status.Should().Be(AgentResultStatus.Failed);
        agent.State.Should().Be(AgentState.Failed);
    }

    [Fact]
    public async Task MaxIterations_Exceeded_EmitsFailedEvent()
    {
        var tool = MockTool.AlwaysReturns("loop", "more");
        var registry = new DefaultToolRegistry();
        registry.Register(tool);

        var client = new FakeChatClient();
        for (int i = 0; i < 2; i++)
            client.WithFunctionCallResponse(new FunctionCallContent($"c{i}", "loop"));

        var agent = new ChatAgent("test", client, new ChatAgentOptions { MaxIterations = 2 });
        var context = CreateContext(tools: registry);

        var events = await CollectEvents(
            agent.ExecuteStreamingAsync(AgentTask.Create("Loop"), context));

        events.Last().Should().BeOfType<AgentFailedEvent>()
            .Which.Error.Message.Should().Contain("Max iterations");
    }

    // ──────────────────────────────────────────────────
    // SystemPrompt configuration
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task SystemPrompt_IncludedInMessages()
    {
        var client = new FakeChatClient("Response");
        var agent = new ChatAgent("test", client, new ChatAgentOptions
        {
            SystemPrompt = "You are a helpful assistant."
        });
        var context = CreateContext();

        await agent.ExecuteAsync(AgentTask.Create("Hello"), context);

        var sentMessages = client.ReceivedMessages[0];
        sentMessages[0].Role.Should().Be(ChatRole.System);
        sentMessages[0].Text.Should().StartWith("You are a helpful assistant.");
        sentMessages[0].Text.Should().Contain("Execution environment:");
        sentMessages[1].Role.Should().Be(ChatRole.User);
        sentMessages[1].Text.Should().Be("Hello");
    }

    [Fact]
    public async Task NoSystemPrompt_StillAddsExecutionEnvironmentContext()
    {
        var client = new FakeChatClient("Response");
        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        await agent.ExecuteAsync(AgentTask.Create("Hello"), context);

        var sentMessages = client.ReceivedMessages[0];
        sentMessages.Should().HaveCount(2);
        sentMessages[0].Role.Should().Be(ChatRole.System);
        sentMessages[0].Text.Should().Contain("Execution environment:");
        sentMessages[1].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public async Task ExecutionEnvironmentContext_Can_Be_Disabled()
    {
        var client = new FakeChatClient("Response");
        var agent = new ChatAgent("test", client, new ChatAgentOptions
        {
            IncludeExecutionContext = false,
        });
        var context = CreateContext();

        await agent.ExecuteAsync(AgentTask.Create("Hello"), context);

        var sentMessages = client.ReceivedMessages[0];
        sentMessages.Should().HaveCount(1);
        sentMessages[0].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public async Task ExecutionEnvironmentContext_Uses_Current_Platform_Semantics()
    {
        var client = new FakeChatClient("Response");
        var agent = new ChatAgent("test", client, new ChatAgentOptions
        {
            SystemPrompt = "Base prompt",
        });
        var context = CreateContext();

        await agent.ExecuteAsync(AgentTask.Create("Hello"), context);

        var prompt = client.ReceivedMessages[0][0].Text;
        if (OperatingSystem.IsWindows())
        {
            prompt.Should().Contain("Host operating system: Windows");
            prompt.Should().Contain("cmd.exe (/d /c)");
            prompt.Should().Contain("Windows paths with backslashes and possible drive letters");
        }
        else
        {
            prompt.Should().Contain("/bin/bash (-lc)");
            prompt.Should().Contain("POSIX paths with forward slashes");
        }
    }

    // ──────────────────────────────────────────────────
    // Multiple tool calls in one response
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task MultipleToolCalls_AllExecuted()
    {
        var tool1 = MockTool.AlwaysReturns("tool_a", "result_a");
        var tool2 = MockTool.AlwaysReturns("tool_b", "result_b");
        var registry = new DefaultToolRegistry();
        registry.Register(tool1);
        registry.Register(tool2);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(
                new FunctionCallContent("c1", "tool_a"),
                new FunctionCallContent("c2", "tool_b"))
            .WithResponse("Both done");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry);

        var result = await agent.ExecuteAsync(AgentTask.Create("Do both"), context);

        result.Status.Should().Be(AgentResultStatus.Success);
        tool1.ReceivedInputs.Should().HaveCount(1);
        tool2.ReceivedInputs.Should().HaveCount(1);
    }

    [Fact]
    public async Task StreamingUsage_PropagatesIntoUsageEventAndCompletedResult()
    {
        var client = new UsageAwareChatClient();
        client.AddStreamingResponse(["Hello", " world"], inputTokens: 120, outputTokens: 45, estimatedCost: 0.37m);

        var agent = new ChatAgent("test", client);
        var context = CreateContext();

        var events = await CollectEvents(agent.ExecuteStreamingAsync(AgentTask.Create("Say hi"), context));

        var usageEvent = events.OfType<TokenUsageEvent>().Single();
        usageEvent.InputTokens.Should().Be(120);
        usageEvent.OutputTokens.Should().Be(45);
        usageEvent.EstimatedCost.Should().Be(0.37m);

        var completed = events.OfType<AgentCompletedEvent>().Single();
        completed.Result.Status.Should().Be(AgentResultStatus.Success);
        completed.Result.TokenUsage.Should().NotBeNull();
        completed.Result.TokenUsage!.TotalInputTokens.Should().Be(120);
        completed.Result.TokenUsage.TotalOutputTokens.Should().Be(45);
        completed.Result.TokenUsage.TotalTokens.Should().Be(165);
        completed.Result.EstimatedCost.Should().Be(0.37m);
    }


internal sealed class UsageAwareChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new();
    private readonly Queue<List<ChatResponseUpdate>> _updates = new();

    public void AddStreamingResponse(IEnumerable<string> chunks, int inputTokens, int outputTokens, decimal estimatedCost)
    {
        var updateList = chunks.Select(CreateTextUpdate).ToList();
        updateList.Add(CreateUsageUpdate(inputTokens, outputTokens, estimatedCost));
        _updates.Enqueue(updateList);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(chunks)));
        SetTrackingMetadata(response, inputTokens, outputTokens, estimatedCost);
        _responses.Enqueue(response);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Task.FromResult(_responses.Dequeue());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in _updates.Dequeue())
        {
            yield return update;
            await Task.Yield();
        }
    }

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private static ChatResponseUpdate CreateTextUpdate(string text)
        => new()
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)],
        };

    private static ChatResponseUpdate CreateUsageUpdate(int inputTokens, int outputTokens, decimal estimatedCost)
    {
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [],
        };

        SetTrackingMetadata(update, inputTokens, outputTokens, estimatedCost);
        return update;
    }

    private static void SetTrackingMetadata(object target, int inputTokens, int outputTokens, decimal estimatedCost)
    {
        SetPropertyIfExists(target, "ModelId", "test-model");

        var usage = Activator.CreateInstance(typeof(ChatResponse).GetProperty("Usage", BindingFlags.Instance | BindingFlags.Public)!.PropertyType);
        if (usage is not null)
        {
            SetPropertyIfExists(usage, "InputTokenCount", inputTokens);
            SetPropertyIfExists(usage, "OutputTokenCount", outputTokens);
            SetPropertyIfExists(usage, "TotalTokenCount", inputTokens + outputTokens);
            SetPropertyIfExists(usage, "PromptTokenCount", inputTokens);
            SetPropertyIfExists(usage, "CompletionTokenCount", outputTokens);

            var usageProperty = target.GetType().GetProperty("Usage", BindingFlags.Instance | BindingFlags.Public);
            if (usageProperty?.CanWrite == true)
                usageProperty.SetValue(target, usage);

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
    // ──────────────────────────────────────────────────
    // Mixed approval in batch: one approved, one denied
    // ──────────────────────────────────────────────────

    [Fact]
    public async Task MixedApproval_ApprovedToolRuns_DeniedToolSkipped()
    {
        var readTool = MockTool.AlwaysReturns("read_file", "content")
            .WithAnnotations(new ToolAnnotations { IsReadOnly = true });
        var writeTool = MockTool.AlwaysReturns("write_file", "written")
            .WithAnnotations(new ToolAnnotations { RequiresApproval = true });
        var registry = new DefaultToolRegistry();
        registry.Register(readTool);
        registry.Register(writeTool);

        var gate = MockApprovalGate.AutoDeny();
        var client = new FakeChatClient()
            .WithFunctionCallResponse(
                new FunctionCallContent("c1", "read_file"),
                new FunctionCallContent("c2", "write_file"))
            .WithResponse("Read succeeded, write was denied");

        var agent = new ChatAgent("test", client);
        var context = CreateContext(tools: registry, approvalGate: gate);

        await agent.ExecuteAsync(AgentTask.Create("Read and write"), context);

        readTool.ReceivedInputs.Should().HaveCount(1, "read_file has no RequiresApproval");
        writeTool.ReceivedInputs.Should().BeEmpty("write_file was denied");
        gate.ReceivedRequests.Should().HaveCount(1)
            .And.Subject.Single().ToolName.Should().Be("write_file");
    }
}
