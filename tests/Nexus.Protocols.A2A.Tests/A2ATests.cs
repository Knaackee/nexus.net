using System.Text.Json;
using FluentAssertions;
using Nexus.Protocols.A2A;
using Xunit;

namespace Nexus.Protocols.A2A.Tests;

public class A2ATypesTests
{
    [Fact]
    public void AgentCard_RequiredPropertiesCanBeSet()
    {
        var card = new AgentCard
        {
            Name = "TestAgent",
            Description = "A test agent",
            Endpoint = new Uri("https://example.com/a2a"),
            Version = "1.0.0",
            Skills =
            [
                new AgentSkill { Id = "skill-1", Name = "Skill One", Description = "First skill" }
            ]
        };

        card.Name.Should().Be("TestAgent");
        card.Endpoint.Should().Be(new Uri("https://example.com/a2a"));
        card.Version.Should().Be("1.0.0");
        card.Skills.Should().HaveCount(1);
        card.SupportedModalities.Should().Contain("text");
    }

    [Fact]
    public void A2ATaskRequest_CanBeCreatedWithMessages()
    {
        var request = new A2ATaskRequest
        {
            Id = "task-1",
            SessionId = "session-1",
            Messages =
            [
                new A2AMessage
                {
                    Role = "user",
                    Parts = [new A2ATextPart("Hello")]
                }
            ]
        };

        request.Id.Should().Be("task-1");
        request.SessionId.Should().Be("session-1");
        request.Messages.Should().HaveCount(1);
        request.Messages[0].Parts[0].Should().BeOfType<A2ATextPart>();
        ((A2ATextPart)request.Messages[0].Parts[0]).Text.Should().Be("Hello");
    }

    [Fact]
    public void A2ATask_DefaultCollectionsAreEmpty()
    {
        var task = new A2ATask
        {
            Id = "t1",
            SessionId = "s1",
            Status = A2ATaskStatus.Submitted
        };

        task.Messages.Should().BeEmpty();
        task.Artifacts.Should().BeEmpty();
        task.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void A2ATaskStatus_HasExpectedValues()
    {
        Enum.GetValues<A2ATaskStatus>().Should().Contain(A2ATaskStatus.Submitted);
        Enum.GetValues<A2ATaskStatus>().Should().Contain(A2ATaskStatus.Working);
        Enum.GetValues<A2ATaskStatus>().Should().Contain(A2ATaskStatus.Completed);
        Enum.GetValues<A2ATaskStatus>().Should().Contain(A2ATaskStatus.Failed);
        Enum.GetValues<A2ATaskStatus>().Should().Contain(A2ATaskStatus.Canceled);
        Enum.GetValues<A2ATaskStatus>().Should().Contain(A2ATaskStatus.InputRequired);
    }

    [Fact]
    public void A2AMessagePart_DerivedTypes_HaveCorrectData()
    {
        A2AMessagePart textPart = new A2ATextPart("content");
        A2AMessagePart filePart = new A2AFilePart("file.txt", "text/plain", [0x41, 0x42]);
        A2AMessagePart dataPart = new A2ADataPart("application/json", JsonSerializer.SerializeToElement(new { key = "value" }));

        textPart.Should().BeOfType<A2ATextPart>();
        filePart.Should().BeOfType<A2AFilePart>();
        dataPart.Should().BeOfType<A2ADataPart>();

        ((A2AFilePart)filePart).Name.Should().Be("file.txt");
        ((A2AFilePart)filePart).MimeType.Should().Be("text/plain");
        ((A2AFilePart)filePart).Data.Should().BeEquivalentTo(new byte[] { 0x41, 0x42 });
    }
}

public class HttpA2AClientTests
{
    [Fact]
    public async Task DiscoverAsync_DeserializesAgentCard()
    {
        var handler = new FakeHttpHandler(new HttpResponseMessage
        {
            Content = new StringContent("""
                {
                    "name": "Remote Agent",
                    "description": "A remote agent",
                    "endpoint": "https://remote.example.com/a2a",
                    "version": "2.0.0",
                    "skills": [{"id": "s1", "name": "Summarize"}]
                }
                """, System.Text.Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        using var client = new HttpA2AClient(httpClient);

        var card = await client.DiscoverAsync(new Uri("https://example.com/.well-known/agent.json"));

        card.Name.Should().Be("Remote Agent");
        card.Description.Should().Be("A remote agent");
        card.Version.Should().Be("2.0.0");
        card.Skills.Should().HaveCount(1);
        card.Skills[0].Name.Should().Be("Summarize");
    }

    [Fact]
    public async Task SendTaskAsync_SendsJsonRpcRequest()
    {
        var handler = new FakeHttpHandler(new HttpResponseMessage
        {
            Content = new StringContent("""
                {
                    "jsonrpc": "2.0",
                    "result": {
                        "id": "task-1",
                        "sessionId": "session-1",
                        "status": 4
                    },
                    "id": "req-1"
                }
                """, System.Text.Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        using var client = new HttpA2AClient(httpClient);

        var request = new A2ATaskRequest
        {
            Id = "task-1",
            SessionId = "session-1",
            Messages = [new A2AMessage { Role = "user", Parts = [new A2ATextPart("Do something")] }]
        };

        var task = await client.SendTaskAsync(new Uri("https://example.com/a2a"), request);

        task.Id.Should().Be("task-1");
        task.SessionId.Should().Be("session-1");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task CancelTaskAsync_PostsCancelRequest()
    {
        var handler = new FakeHttpHandler(new HttpResponseMessage());
        using var httpClient = new HttpClient(handler);
        using var client = new HttpA2AClient(httpClient);

        await client.CancelTaskAsync(new Uri("https://example.com/a2a"), "task-99");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri.Should().Be(new Uri("https://example.com/a2a"));
    }

    [Fact]
    public void A2AException_HasCorrectMessage()
    {
        var ex = new A2AException("something failed");
        ex.Message.Should().Be("something failed");
    }

    [Fact]
    public void A2AException_WithInnerException_PreservesIt()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new A2AException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpHandler(HttpResponseMessage response) => _response = response;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(_response);
    }
}
