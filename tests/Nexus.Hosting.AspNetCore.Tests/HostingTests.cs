using FluentAssertions;
using NSubstitute;
using Nexus.Hosting.AspNetCore.HealthChecks;
using Nexus.Orchestration;
using Nexus.Core.Agents;
using Nexus.Core.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Nexus.Protocols.AgUi;
using Xunit;

namespace Nexus.Hosting.AspNetCore.Tests;

public class AgentPoolHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthyWithActiveCount()
    {
        var pool = Substitute.For<IAgentPool>();
        pool.ActiveAgents.Returns(new List<IAgent> { Substitute.For<IAgent>(), Substitute.For<IAgent>() });

        var healthCheck = new AgentPoolHealthCheck(pool);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("2");
        result.Data.Should().ContainKey("activeAgentCount");
        result.Data["activeAgentCount"].Should().Be(2);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthyWhenNoAgents()
    {
        var pool = Substitute.For<IAgentPool>();
        pool.ActiveAgents.Returns(new List<IAgent>());

        var healthCheck = new AgentPoolHealthCheck(pool);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["activeAgentCount"].Should().Be(0);
    }
}

public class A2AEndpointTests
{
    [Fact]
    public async Task HandleAsync_Returns415ForNonJsonContentType()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.ContentType = "text/plain";

        Func<System.Text.Json.JsonElement, CancellationToken, Task<System.Text.Json.JsonElement>> handler =
            (_, _) => Task.FromResult(default(System.Text.Json.JsonElement));

        await Nexus.Hosting.AspNetCore.Endpoints.A2AEndpoint.HandleAsync(ctx, handler);

        ctx.Response.StatusCode.Should().Be(415);
    }

    [Fact]
    public async Task HandleAsync_Returns400ForInvalidJson()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream("not valid json"u8.ToArray());

        Func<System.Text.Json.JsonElement, CancellationToken, Task<System.Text.Json.JsonElement>> handler =
            (_, _) => Task.FromResult(default(System.Text.Json.JsonElement));

        await Nexus.Hosting.AspNetCore.Endpoints.A2AEndpoint.HandleAsync(ctx, handler);

        ctx.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task HandleAsync_CallsHandlerWithParsedJson()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream("""{"method":"test"}"""u8.ToArray());
        ctx.Response.Body = new MemoryStream();

        System.Text.Json.JsonElement? receivedElement = null;
        Func<System.Text.Json.JsonElement, CancellationToken, Task<System.Text.Json.JsonElement>> handler =
            (element, _) =>
            {
                receivedElement = element;
                return Task.FromResult(System.Text.Json.JsonSerializer.SerializeToElement(new { ok = true }));
            };

        await Nexus.Hosting.AspNetCore.Endpoints.A2AEndpoint.HandleAsync(ctx, handler);

        receivedElement.Should().NotBeNull();
        receivedElement!.Value.GetProperty("method").GetString().Should().Be("test");
    }
}

public class AgUiEventBridgeTests
{
    [Fact]
    public async Task BridgeAsync_Maps_Reasoning_And_UserInput_Request_Events()
    {
        var graphId = TaskGraphId.New();
        var nodeId = TaskId.New();
        var agentId = AgentId.New();

        async IAsyncEnumerable<OrchestrationEvent> Stream()
        {
            yield return new AgentEventInGraph(graphId, nodeId, new ReasoningChunkEvent(agentId, "Need to inspect the plan."));
            yield return new AgentEventInGraph(graphId, nodeId, new UserInputRequestedEvent(
                agentId,
                "ask-1",
                new UserInputRequest("confirm", "Proceed?", [], null, false, null, "Need confirmation")));
        }

        var events = new List<AgUiEvent>();
        await foreach (var evt in AgUiEventBridge.BridgeAsync(Stream()))
            events.Add(evt);

        events.OfType<AgUiReasoningChunkEvent>().Should().ContainSingle()
            .Which.Text.Should().Be("Need to inspect the plan.");
        events.OfType<AgUiUserInputRequestEvent>().Should().ContainSingle()
            .Which.RequestId.Should().Be("ask-1");
        events.OfType<AgUiUserInputRequestEvent>().Single().Request.Question.Should().Be("Proceed?");
    }
}
