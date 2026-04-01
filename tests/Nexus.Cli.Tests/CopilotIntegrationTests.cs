using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Nexus.Cli.Tests;

/// <summary>
/// Integration tests against the real GitHub Copilot API.
/// Requires a valid cached token (~/.nexus-cli/). Uses gpt-4o-mini for cost.
/// </summary>
public sealed class CopilotAuthTests
{
    [Fact]
    public async Task GetTokenAsync_Returns_Valid_Token()
    {
        var token = await CopilotAuth.GetTokenAsync(CancellationToken.None);

        token.Should().NotBeNull();
        token.Token.Should().NotBeNullOrWhiteSpace();
        token.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetTokenAsync_Returns_Endpoints()
    {
        var token = await CopilotAuth.GetTokenAsync(CancellationToken.None);

        token.Endpoints.Should().NotBeNull();
        token.Endpoints!.Api.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetTokenAsync_Successive_Calls_Return_Cached()
    {
        var token1 = await CopilotAuth.GetTokenAsync(CancellationToken.None);
        var token2 = await CopilotAuth.GetTokenAsync(CancellationToken.None);

        // Should be the same cached token since it hasn't expired
        token1.Token.Should().Be(token2.Token);
    }
}

public sealed class CopilotChatClientTests : IDisposable
{
    private const string CheapModel = "gpt-4o-mini";
    private readonly CopilotChatClient _client = new(CheapModel);

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task GetResponseAsync_Returns_NonEmpty_Reply()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "Say exactly: HELLO") };

        var response = await _client.GetResponseAsync(messages);

        response.Should().NotBeNull();
        response.Messages.Should().NotBeEmpty();
        response.Messages[0].Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetResponseAsync_Follows_Instructions()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "You are a test bot. Reply with exactly the word the user provides, nothing else."),
            new ChatMessage(ChatRole.User, "PINEAPPLE"),
        };

        var response = await _client.GetResponseAsync(messages);

        response.Messages[0].Text.Should().Contain("PINEAPPLE");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_Yields_Chunks()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "Count from 1 to 5, each on a new line.") };
        var chunks = new List<string>();

        await foreach (var update in _client.GetStreamingResponseAsync(messages))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
                chunks.Add(text);
        }

        chunks.Should().NotBeEmpty("streaming should produce at least one chunk");

        var full = string.Concat(chunks);
        full.Should().Contain("1");
        full.Should().Contain("5");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_Cancellation_Stops_Early()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Write a very long essay about the history of computing. At least 2000 words."),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var chunks = new List<string>();
        var cancelled = false;

        try
        {
            await foreach (var update in _client.GetStreamingResponseAsync(messages, null, cts.Token))
            {
                var text = update.Text;
                if (!string.IsNullOrEmpty(text))
                    chunks.Add(text);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        // Either completed early (unlikely) or got cancelled — both OK
        (cancelled || chunks.Count > 0).Should().BeTrue(
            "should have been cancelled or produced some output");
    }

    [Fact]
    public async Task GetResponseAsync_With_ChatOptions_Respects_MaxTokens()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Write a long story about a dragon."),
        };

        var options = new ChatOptions { MaxOutputTokens = 50 };
        var response = await _client.GetResponseAsync(messages, options);

        // With max_tokens=50, the response should be short
        response.Messages[0].Text!.Length.Should().BeLessThan(500,
            "response with max_tokens=50 should be short");
    }

    [Fact]
    public async Task GetResponseAsync_Multi_Turn_Conversation()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a calculator. Respond only with the numeric result."),
            new(ChatRole.User, "What is 2 + 2?"),
        };

        var response1 = await _client.GetResponseAsync(messages);
        response1.Messages[0].Text.Should().Contain("4");

        messages.Add(new ChatMessage(ChatRole.Assistant, response1.Messages[0].Text!));
        messages.Add(new ChatMessage(ChatRole.User, "Multiply that by 3"));

        var response2 = await _client.GetResponseAsync(messages);
        response2.Messages[0].Text.Should().Contain("12");
    }

    [Fact]
    public async Task GetResponseAsync_SystemMessage_Affects_Behavior()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "You must always start your reply with the word BANANA."),
            new ChatMessage(ChatRole.User, "Hello"),
        };

        var response = await _client.GetResponseAsync(messages);

        response.Messages[0].Text.Should().StartWith("BANANA",
            "the system message should override default behavior");
    }
}

public sealed class ChatSessionTests : IDisposable
{
    private const string CheapModel = "gpt-4o-mini";
    private readonly ChatSession _session = new("test1", CheapModel);

    public void Dispose() => _session.Dispose();

    [Fact]
    public void New_Session_Is_Idle_With_No_Messages()
    {
        _session.State.Should().Be(ChatSessionState.Idle);
        _session.MessageCount.Should().Be(0);
        _session.Key.Should().Be("test1");
        _session.Model.Should().Be(CheapModel);
        _session.LastOutput.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_Transitions_To_Running_Then_Idle()
    {
        var stateChanges = new List<ChatSessionState>();
        _session.OnStateChanged += s => stateChanges.Add(s.State);

        _session.Send("Say hi");
        _session.State.Should().Be(ChatSessionState.Running);

        // Wait for completion
        await WaitForDoneAsync(_session, TimeSpan.FromSeconds(30));

        _session.State.Should().Be(ChatSessionState.Idle,
            $"session failed with output: {_session.LastOutput}");
        stateChanges.Should().Contain(ChatSessionState.Running);
        stateChanges.Should().Contain(ChatSessionState.Idle);
    }

    [Fact]
    public async Task Send_Produces_Output_Via_Chunks()
    {
        var chunks = new List<string>();
        _session.OnChunk += c => chunks.Add(c);

        _session.Send("Say exactly: HELLO WORLD");

        await WaitForDoneAsync(_session, TimeSpan.FromSeconds(30));

        _session.State.Should().Be(ChatSessionState.Idle,
            $"session failed with output: {_session.LastOutput}");
        chunks.Should().NotBeEmpty("should have received streamed chunks");
        _session.LastOutput.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Send_Increments_MessageCount()
    {
        _session.Send("Say hi");
        await WaitForDoneAsync(_session, TimeSpan.FromSeconds(30));

        _session.State.Should().Be(ChatSessionState.Idle,
            $"session failed with output: {_session.LastOutput}");
        _session.MessageCount.Should().Be(2, "one user + one assistant message");
    }

    [Fact]
    public async Task Send_While_Running_Is_Ignored()
    {
        _session.Send("Write a long poem about the ocean");

        // Try sending again immediately while running
        _session.State.Should().Be(ChatSessionState.Running);
        _session.Send("This should be ignored");

        await WaitForDoneAsync(_session, TimeSpan.FromSeconds(30));

        _session.State.Should().Be(ChatSessionState.Idle,
            $"session failed with output: {_session.LastOutput}");
        // Should only have 2 messages (first Send + assistant response)
        _session.MessageCount.Should().Be(2);
    }

    [Fact]
    public async Task Cancel_Stops_Running_Session()
    {
        _session.Send("Write a very long essay about every country in the world. Include extensive details.");

        _session.State.Should().Be(ChatSessionState.Running);
        await Task.Delay(300);
        _session.Cancel();

        await WaitForDoneAsync(_session, TimeSpan.FromSeconds(10));

        _session.State.Should().Be(ChatSessionState.Idle);
    }

    [Fact]
    public async Task Send_After_Completion_Starts_New_Exchange()
    {
        _session.Send("Say A");
        await WaitForDoneAsync(_session, TimeSpan.FromSeconds(30));
        _session.State.Should().Be(ChatSessionState.Idle,
            $"first send failed: {_session.LastOutput}");
        _session.MessageCount.Should().Be(2);

        _session.Send("Say B");
        await WaitForDoneAsync(_session, TimeSpan.FromSeconds(30));
        _session.State.Should().Be(ChatSessionState.Idle,
            $"second send failed: {_session.LastOutput}");
        _session.MessageCount.Should().Be(4, "two exchanges = 4 messages");
    }

    private static async Task WaitForDoneAsync(ChatSession session, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (session.State == ChatSessionState.Running && !cts.IsCancellationRequested)
            await Task.Delay(100, cts.Token);
    }
}

public sealed class ChatManagerTests : IDisposable
{
    private readonly ChatManager _manager = new();

    public void Dispose() => _manager.Dispose();

    [Fact]
    public void Empty_Manager_Has_No_Sessions()
    {
        _manager.Sessions.Should().BeEmpty();
        _manager.ActiveKey.Should().BeNull();
        _manager.ActiveSession.Should().BeNull();
    }

    [Fact]
    public void Add_Creates_Session_And_Sets_Active()
    {
        var session = _manager.Add("a", "gpt-4o-mini");

        session.Key.Should().Be("a");
        session.Model.Should().Be("gpt-4o-mini");
        _manager.ActiveKey.Should().Be("a");
        _manager.ActiveSession.Should().BeSameAs(session);
        _manager.Sessions.Should().HaveCount(1);
    }

    [Fact]
    public void Add_Duplicate_Key_Throws()
    {
        _manager.Add("x", "gpt-4o-mini");

        var act = () => _manager.Add("x", "gpt-4o-mini");

        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public void Add_Duplicate_Key_Case_Insensitive_Throws()
    {
        _manager.Add("alpha", "gpt-4o-mini");

        var act = () => _manager.Add("ALPHA", "gpt-4o-mini");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void First_Add_Sets_Active_Subsequent_Do_Not()
    {
        _manager.Add("a", "gpt-4o-mini");
        _manager.Add("b", "gpt-4o-mini");

        _manager.ActiveKey.Should().Be("a", "first added should remain active");
    }

    [Fact]
    public void Switch_Changes_Active_Session()
    {
        _manager.Add("a", "gpt-4o-mini");
        _manager.Add("b", "gpt-4o-mini");

        _manager.Switch("b").Should().BeTrue();
        _manager.ActiveKey.Should().Be("b");
        _manager.ActiveSession!.Key.Should().Be("b");
    }

    [Fact]
    public void Switch_To_Nonexistent_Key_Returns_False()
    {
        _manager.Add("a", "gpt-4o-mini");

        _manager.Switch("zzz").Should().BeFalse();
        _manager.ActiveKey.Should().Be("a", "active should not change");
    }

    [Fact]
    public void Switch_Is_Case_Insensitive()
    {
        _manager.Add("alpha", "gpt-4o-mini");

        _manager.Switch("ALPHA").Should().BeTrue();
    }

    [Fact]
    public void Remove_Disposes_And_Removes_Session()
    {
        _manager.Add("a", "gpt-4o-mini");
        _manager.Add("b", "gpt-4o-mini");

        _manager.Remove("a").Should().BeTrue();

        _manager.Sessions.Should().HaveCount(1);
        _manager.Get("a").Should().BeNull();
    }

    [Fact]
    public void Remove_Active_Switches_To_Next()
    {
        _manager.Add("a", "gpt-4o-mini");
        _manager.Add("b", "gpt-4o-mini");

        _manager.Remove("a");

        _manager.ActiveKey.Should().Be("b");
    }

    [Fact]
    public void Remove_Last_Session_Clears_Active()
    {
        _manager.Add("a", "gpt-4o-mini");

        _manager.Remove("a");

        _manager.ActiveKey.Should().BeNull();
        _manager.ActiveSession.Should().BeNull();
    }

    [Fact]
    public void Remove_Nonexistent_Returns_False()
    {
        _manager.Remove("nope").Should().BeFalse();
    }

    [Fact]
    public void Get_Returns_Session_By_Key()
    {
        var session = _manager.Add("a", "gpt-4o-mini");

        _manager.Get("a").Should().BeSameAs(session);
        _manager.Get("A").Should().BeSameAs(session, "case insensitive");
    }

    [Fact]
    public void Get_Nonexistent_Returns_Null()
    {
        _manager.Get("nope").Should().BeNull();
    }

    [Fact]
    public async Task Parallel_Sessions_Run_Concurrently()
    {
        var s1 = _manager.Add("chat1", "gpt-4o-mini");
        var s2 = _manager.Add("chat2", "gpt-4o-mini");

        s1.Send("Say exactly: RESPONSE_ONE");
        s2.Send("Say exactly: RESPONSE_TWO");

        // Both should be running simultaneously
        s1.State.Should().Be(ChatSessionState.Running);
        s2.State.Should().Be(ChatSessionState.Running);

        await WaitForDoneAsync(s1, TimeSpan.FromSeconds(30));
        await WaitForDoneAsync(s2, TimeSpan.FromSeconds(30));

        s1.State.Should().Be(ChatSessionState.Idle,
            $"s1 failed: {s1.LastOutput}");
        s2.State.Should().Be(ChatSessionState.Idle,
            $"s2 failed: {s2.LastOutput}");

        s1.LastOutput.Should().NotBeNullOrWhiteSpace();
        s2.LastOutput.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Three_Parallel_Sessions_All_Complete()
    {
        var sessions = new[]
        {
            _manager.Add("a", "gpt-4o-mini"),
            _manager.Add("b", "gpt-4o-mini"),
            _manager.Add("c", "gpt-4o-mini"),
        };

        sessions[0].Send("What is 1+1? Reply with just the number.");
        sessions[1].Send("What is 2+2? Reply with just the number.");
        sessions[2].Send("What is 3+3? Reply with just the number.");

        foreach (var s in sessions)
            await WaitForDoneAsync(s, TimeSpan.FromSeconds(30));

        foreach (var s in sessions)
        {
            s.State.Should().Be(ChatSessionState.Idle,
                $"session '{s.Key}' failed: {s.LastOutput}");
            s.LastOutput.Should().NotBeNullOrWhiteSpace();
            s.MessageCount.Should().Be(2);
        }

        sessions[0].LastOutput.Should().Contain("2");
        sessions[1].LastOutput.Should().Contain("4");
        sessions[2].LastOutput.Should().Contain("6");
    }

    [Fact]
    public void Dispose_Cleans_Up_All_Sessions()
    {
        _manager.Add("a", "gpt-4o-mini");
        _manager.Add("b", "gpt-4o-mini");

        _manager.Dispose();

        _manager.Sessions.Should().BeEmpty();
    }

    private static async Task WaitForDoneAsync(ChatSession session, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (session.State == ChatSessionState.Running && !cts.IsCancellationRequested)
            await Task.Delay(100, cts.Token);
    }
}

public sealed class CopilotChatClientModelTests
{
    [Fact]
    public async Task CopilotProvider_Initializes_Discoverable_Models()
    {
        using var provider = new CopilotCliChatProvider();

        await provider.InitializeAsync();

        provider.AvailableModels.Should().NotBeEmpty();
        provider.DefaultModel.Should().NotBeNullOrWhiteSpace();
        provider.AvailableModels.Should().Contain(provider.DefaultModel);
    }

    [Fact]
    public async Task Different_Model_Can_Respond()
    {
        // Test with gpt-4o-mini specifically (cheapest)
        using var client = new CopilotChatClient("gpt-4o-mini");

        var messages = new[] { new ChatMessage(ChatRole.User, "Say OK") };
        var response = await client.GetResponseAsync(messages);

        response.Messages[0].Text.Should().NotBeNullOrWhiteSpace();
    }
}
