using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Core.Tests;

public class AgentPipelineBuilderTests
{
    [Fact]
    public async Task BuildBuffered_No_Middleware_Calls_Terminal()
    {
        var builder = new AgentPipelineBuilder();
        var terminal = new AgentExecutionDelegate((task, ctx, ct) =>
            Task.FromResult(AgentResult.Success("terminal")));

        var pipeline = builder.BuildBuffered(terminal);
        var result = await pipeline(AgentTask.Create("test"), null!, CancellationToken.None);

        result.Text.Should().Be("terminal");
    }

    [Fact]
    public async Task BuildBuffered_Middleware_Wraps_Terminal()
    {
        var callOrder = new List<string>();
        var builder = new AgentPipelineBuilder();
        builder.Use(new TestAgentMiddleware("M1", callOrder));

        var terminal = new AgentExecutionDelegate((task, ctx, ct) =>
        {
            callOrder.Add("terminal");
            return Task.FromResult(AgentResult.Success("done"));
        });

        var pipeline = builder.BuildBuffered(terminal);
        await pipeline(AgentTask.Create("test"), null!, CancellationToken.None);

        callOrder.Should().ContainInOrder("M1-before", "terminal", "M1-after");
    }

    [Fact]
    public async Task BuildBuffered_Multiple_Middleware_Execute_In_Order()
    {
        var callOrder = new List<string>();
        var builder = new AgentPipelineBuilder();
        builder.Use(new TestAgentMiddleware("M1", callOrder));
        builder.Use(new TestAgentMiddleware("M2", callOrder));

        var terminal = new AgentExecutionDelegate((task, ctx, ct) =>
        {
            callOrder.Add("terminal");
            return Task.FromResult(AgentResult.Success("done"));
        });

        var pipeline = builder.BuildBuffered(terminal);
        await pipeline(AgentTask.Create("test"), null!, CancellationToken.None);

        callOrder.Should().ContainInOrder("M1-before", "M2-before", "terminal", "M2-after", "M1-after");
    }

    private sealed class TestAgentMiddleware : IAgentMiddleware
    {
        private readonly string _name;
        private readonly List<string>? _callOrder;

        public TestAgentMiddleware(string name, List<string>? callOrder = null)
        {
            _name = name;
            _callOrder = callOrder;
        }

        public async Task<AgentResult> InvokeAsync(
            AgentTask task, IAgentContext ctx,
            AgentExecutionDelegate next, CancellationToken ct)
        {
            _callOrder?.Add($"{_name}-before");
            var result = await next(task, ctx, ct);
            _callOrder?.Add($"{_name}-after");
            return result;
        }
    }
}

public class ApprovalGateTests
{
    [Fact]
    public async Task AutoApproveGate_Always_Approves()
    {
        var gate = new AutoApproveGate();
        var request = new ApprovalRequest("test", AgentId.New());

        var result = await gate.RequestApprovalAsync(request);

        result.IsApproved.Should().BeTrue();
        result.ApprovedBy.Should().Be("auto-approve");
    }
}

public class AgentEventTests
{
    [Fact]
    public void TextChunkEvent_Creates_With_Timestamp()
    {
        var id = AgentId.New();
        var evt = new TextChunkEvent(id, "hello");

        evt.AgentId.Should().Be(id);
        evt.Text.Should().Be("hello");
        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AgentCompletedEvent_Creates_Correctly()
    {
        var id = AgentId.New();
        var result = AgentResult.Success("done");
        var evt = new AgentCompletedEvent(id, result);

        evt.AgentId.Should().Be(id);
        evt.Result.Status.Should().Be(AgentResultStatus.Success);
    }
}

public class NexusBuilderTests
{
    [Fact]
    public void AddNexus_Registers_Core_Services()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddNexus(n => { });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetService<IToolRegistry>();
        var gate = sp.GetService<IApprovalGate>();

        registry.Should().NotBeNull();
        gate.Should().NotBeNull();
        gate.Should().BeOfType<AutoApproveGate>();
    }
}
