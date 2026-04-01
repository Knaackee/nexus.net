using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Nexus.Core.Agents;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;
using Nexus.Testing.Mocks;

namespace Nexus.Orchestration.Tests;

public class PartitionedToolExecutorTests
{
    [Fact]
    public async Task ReadOnlyTools_RunInParallel()
    {
        var executor = new PartitionedToolExecutor([], new ToolExecutorOptions
        {
            MaxReadOnlyConcurrency = 4,
        });

        var requests = Enumerable.Range(1, 4)
            .Select(index => new ToolExecutionRequest(
                $"call-{index}",
                CreateDelayedTool($"read_{index}", 150, isReadOnly: true),
                EmptyJson()))
            .ToList();

        var stopwatch = Stopwatch.StartNew();
        var results = await executor.ExecuteAsync(requests, CreateToolContext());
        stopwatch.Stop();

        results.Should().HaveCount(4);
        results.Should().OnlyContain(r => r.Result.IsSuccess);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(350));
    }

    [Fact]
    public async Task MixedTools_RunReadOnlyBlocksThenWritesSerially()
    {
        var executor = new PartitionedToolExecutor([], new ToolExecutorOptions
        {
            MaxReadOnlyConcurrency = 4,
        });
        var order = new ConcurrentQueue<string>();

        var requests = new List<ToolExecutionRequest>
        {
            new("call-1", CreateOrderedTool("read_a", 80, true, order), EmptyJson()),
            new("call-2", CreateOrderedTool("read_b", 80, true, order), EmptyJson()),
            new("call-3", CreateOrderedTool("write", 10, false, order), EmptyJson()),
            new("call-4", CreateOrderedTool("read_c", 10, true, order), EmptyJson()),
        };

        var results = await executor.ExecuteAsync(requests, CreateToolContext());

        results.Should().HaveCount(4);
        var sequence = order.ToArray();
        Array.IndexOf(sequence, "read_a:done").Should().BeLessThan(Array.IndexOf(sequence, "write:start"));
        Array.IndexOf(sequence, "read_b:done").Should().BeLessThan(Array.IndexOf(sequence, "write:start"));
        Array.IndexOf(sequence, "write:done").Should().BeLessThan(Array.IndexOf(sequence, "read_c:start"));
    }

    [Fact]
    public async Task ToolMiddleware_IsAppliedToExecutorCalls()
    {
        var middleware = new RecordingMiddleware();
        var executor = new PartitionedToolExecutor([middleware]);

        var results = await executor.ExecuteAsync(
            [new ToolExecutionRequest("call-1", MockTool.AlwaysReturns("read_file", "content"), EmptyJson())],
            CreateToolContext());

        results.Should().ContainSingle();
        middleware.Invocations.Should().ContainSingle().Which.Should().Be("read_file");
    }

    [Fact]
    public async Task ChatAgent_UsesToolExecutor()
    {
        var executor = new RecordingExecutor();
        var registry = new DefaultToolRegistry();
        registry.Register(MockTool.AlwaysReturns("lookup", "ignored"));

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("call-1", "lookup"))
            .WithResponse("Done");

        var agent = new ChatAgent("test", client, toolExecutor: executor);
        var context = CreateAgentContext(registry);

        await agent.ExecuteAsync(AgentTask.Create("Look it up"), context);

        executor.RecordedCalls.Should().ContainSingle().Which.ToolName.Should().Be("lookup");
    }

    private static AsyncTestTool CreateDelayedTool(string name, int delayMs, bool isReadOnly)
        => new AsyncTestTool(
            name,
            new ToolAnnotations { IsReadOnly = isReadOnly },
            async _ =>
            {
                await Task.Delay(delayMs);
                return ToolResult.Success(name);
            });

    private static AsyncTestTool CreateOrderedTool(string name, int delayMs, bool isReadOnly, ConcurrentQueue<string> order)
        => new AsyncTestTool(
            name,
            new ToolAnnotations { IsReadOnly = isReadOnly },
            async _ =>
            {
                order.Enqueue($"{name}:start");
                await Task.Delay(delayMs);
                order.Enqueue($"{name}:done");
                return ToolResult.Success(name);
            });

    private static JsonElement EmptyJson() => JsonDocument.Parse("{}").RootElement;

    private static IToolContext CreateToolContext()
    {
        var toolContext = NSubstitute.Substitute.For<IToolContext>();
        toolContext.AgentId.Returns(AgentId.New());
        toolContext.Correlation.Returns(new Nexus.Core.Contracts.CorrelationContext { TraceId = "trace", SpanId = "span" });
        toolContext.Tools.Returns(new DefaultToolRegistry());
        return toolContext;
    }

    private static IAgentContext CreateAgentContext(IToolRegistry tools)
    {
        var context = NSubstitute.Substitute.For<IAgentContext>();
        context.Tools.Returns(tools);
        context.Correlation.Returns(new Nexus.Core.Contracts.CorrelationContext { TraceId = "trace", SpanId = "span" });
        return context;
    }

    private sealed class RecordingMiddleware : IToolMiddleware
    {
        public List<string> Invocations { get; } = [];

        public async Task<ToolResult> InvokeAsync(
            ITool tool,
            JsonElement input,
            IToolContext ctx,
            ToolExecutionDelegate next,
            CancellationToken ct)
        {
            Invocations.Add(tool.Name);
            return await next(tool, input, ctx, ct);
        }
    }

    private sealed class RecordingExecutor : IToolExecutor
    {
        public List<(string CallId, string ToolName)> RecordedCalls { get; } = [];

        public Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ToolExecutionRequest> requests,
            IToolContext context,
            CancellationToken ct = default)
        {
            foreach (var request in requests)
                RecordedCalls.Add((request.CallId, request.Tool.Name));

            IReadOnlyList<ToolExecutionResult> results = requests
                .Select(r => new ToolExecutionResult(r.CallId, r.Tool.Name, ToolResult.Success("from executor")))
                .ToList();

            return Task.FromResult(results);
        }
    }

    private sealed class AsyncTestTool : ITool
    {
        private readonly Func<JsonElement, Task<ToolResult>> _handler;

        public AsyncTestTool(string name, ToolAnnotations annotations, Func<JsonElement, Task<ToolResult>> handler)
        {
            Name = name;
            Annotations = annotations;
            _handler = handler;
        }

        public string Name { get; }
        public string Description => Name;
        public ToolAnnotations? Annotations { get; }

        public Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
            => _handler(input);
    }
}