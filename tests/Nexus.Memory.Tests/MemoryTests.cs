using FluentAssertions;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Compaction;
using Nexus.Memory;

namespace Nexus.Memory.Tests;

public class InMemoryConversationStoreTests
{
    private readonly InMemoryConversationStore _store = new();

    [Fact]
    public async Task CreateAsync_Returns_Unique_Id()
    {
        var id1 = await _store.CreateAsync();
        var id2 = await _store.CreateAsync();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public async Task AppendAsync_And_GetHistoryAsync_Roundtrip()
    {
        var id = await _store.CreateAsync();
        await _store.AppendAsync(id, new ChatMessage(ChatRole.User, "Hello"));
        await _store.AppendAsync(id, new ChatMessage(ChatRole.Assistant, "Hi"));

        var history = await _store.GetHistoryAsync(id);
        history.Should().HaveCount(2);
        history[0].Role.Should().Be(ChatRole.User);
        history[1].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public async Task GetHistoryAsync_With_MaxMessages()
    {
        var id = await _store.CreateAsync();
        for (int i = 0; i < 10; i++)
            await _store.AppendAsync(id, new ChatMessage(ChatRole.User, $"Message {i}"));

        var history = await _store.GetHistoryAsync(id, maxMessages: 3);
        history.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetHistoryAsync_Returns_Empty_For_Unknown()
    {
        var history = await _store.GetHistoryAsync(ConversationId.New());
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_Throws_For_Unknown_Conversation()
    {
        var act = () => _store.AppendAsync(ConversationId.New(), new ChatMessage(ChatRole.User, "x"));
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ForkAsync_Creates_Copy()
    {
        var id = await _store.CreateAsync();
        await _store.AppendAsync(id, new ChatMessage(ChatRole.User, "Hello"));
        await _store.AppendAsync(id, new ChatMessage(ChatRole.Assistant, "Hi"));

        var forkedId = await _store.ForkAsync(id);
        var forkedHistory = await _store.GetHistoryAsync(forkedId);

        forkedHistory.Should().HaveCount(2);
    }

    [Fact]
    public async Task ForkAsync_With_Filter()
    {
        var id = await _store.CreateAsync();
        await _store.AppendAsync(id, new ChatMessage(ChatRole.User, "Hello"));
        await _store.AppendAsync(id, new ChatMessage(ChatRole.Assistant, "Hi"));
        await _store.AppendAsync(id, new ChatMessage(ChatRole.User, "Bye"));

        var forkedId = await _store.ForkAsync(id, m => m.Role == ChatRole.User);
        var forkedHistory = await _store.GetHistoryAsync(forkedId);

        forkedHistory.Should().HaveCount(2);
        forkedHistory.Should().AllSatisfy(m => m.Role.Should().Be(ChatRole.User));
    }

    [Fact]
    public async Task ForkAsync_Throws_For_Unknown()
    {
        var act = () => _store.ForkAsync(ConversationId.New());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}

public class InMemoryWorkingMemoryTests
{
    private readonly InMemoryWorkingMemory _memory = new();

    [Fact]
    public async Task SetAsync_And_GetAsync_Roundtrip()
    {
        await _memory.SetAsync("key", "value");
        var result = await _memory.GetAsync<string>("key");
        result.Should().Be("value");
    }

    [Fact]
    public async Task GetAsync_Returns_Default_For_Missing()
    {
        var result = await _memory.GetAsync<string>("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_Overwrites_Existing()
    {
        await _memory.SetAsync("key", "first");
        await _memory.SetAsync("key", "second");
        var result = await _memory.GetAsync<string>("key");
        result.Should().Be("second");
    }

    [Fact]
    public async Task RemoveAsync_Removes_Entry()
    {
        await _memory.SetAsync("key", "value");
        await _memory.RemoveAsync("key");
        var result = await _memory.GetAsync<string>("key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ClearAsync_Removes_All()
    {
        await _memory.SetAsync("a", 1);
        await _memory.SetAsync("b", 2);
        await _memory.ClearAsync();

        (await _memory.GetAsync<int?>("a")).Should().BeNull();
        (await _memory.GetAsync<int?>("b")).Should().BeNull();
    }

    [Fact]
    public async Task Complex_Object_Serialization()
    {
        var data = new { Name = "Test", Value = 42, Items = new[] { "a", "b" } };
        await _memory.SetAsync("complex", data);
        var result = await _memory.GetAsync<System.Text.Json.JsonElement>("complex");
        result.GetProperty("Name").GetString().Should().Be("Test");
        result.GetProperty("Value").GetInt32().Should().Be(42);
    }
}

public class LongTermMemoryRecallProviderTests
{
    [Fact]
    public async Task RecallAsync_Prepends_Recalled_Memory_From_Latest_User_Query()
    {
        var memory = new InMemoryLongTermMemory();
        await memory.StoreAsync("Remember to use OAuth device flow for Copilot auth");
        await memory.StoreAsync("Unrelated deployment note");

        var provider = new LongTermMemoryRecallProvider(memory, new LongTermMemoryRecallOptions
        {
            MaxResults = 2,
            MinimumRelevance = 0.2,
        });

        var result = await provider.RecallAsync(new CompactionRecallContext
        {
            OriginalMessages =
            [
                new ChatMessage(ChatRole.User, "How should Copilot auth work?"),
                new ChatMessage(ChatRole.Assistant, "Old answer"),
            ],
            ActiveMessages =
            [
                new ChatMessage(ChatRole.Assistant, "[Conversation summary]\n- Auth was discussed."),
                new ChatMessage(ChatRole.User, "Continue with auth design"),
            ],
            Compaction = new CompactionResult(
            [
                new ChatMessage(ChatRole.Assistant, "[Conversation summary]\n- Auth was discussed."),
                new ChatMessage(ChatRole.User, "Continue with auth design"),
            ],
            180,
            45,
            "summary"),
            WindowOptions = new ContextWindowOptions { MaxTokens = 200, TargetTokens = 100 },
        });

        result.Should().HaveCount(3);
        result[0].Role.Should().Be(ChatRole.System);
        result[0].Text.Should().Contain("[Recalled memory]");
        result[0].Text.Should().Contain("OAuth device flow for Copilot auth");
    }

    [Fact]
    public async Task RecallAsync_Returns_Active_Messages_When_No_Memory_Matches()
    {
        var memory = new InMemoryLongTermMemory();
        await memory.StoreAsync("Completely different topic");

        var provider = new LongTermMemoryRecallProvider(memory, new LongTermMemoryRecallOptions());
        IReadOnlyList<ChatMessage> activeMessages =
        [
            new ChatMessage(ChatRole.Assistant, "Compacted summary"),
        ];

        var result = await provider.RecallAsync(new CompactionRecallContext
        {
            OriginalMessages = [new ChatMessage(ChatRole.User, "Need auth guidance")],
            ActiveMessages = activeMessages,
            Compaction = new CompactionResult(activeMessages, 120, 40, "summary"),
            WindowOptions = new ContextWindowOptions(),
        });

        result.Should().BeEquivalentTo(activeMessages);
    }
}
