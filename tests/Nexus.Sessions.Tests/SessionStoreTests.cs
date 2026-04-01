using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Nexus.Sessions.Tests;

public sealed class InMemorySessionStoreTests
{
    [Fact]
    public async Task Append_And_ReadAsync_Roundtrip()
    {
        var store = new InMemorySessionStore();
        var session = await store.CreateAsync(new SessionCreateOptions { Title = "test" });

        await store.AppendAsync(session.Id, new ChatMessage(ChatRole.User, "hello"));
        await store.AppendAsync(session.Id, new ChatMessage(ChatRole.Assistant, "world"));

        var messages = new List<ChatMessage>();
        await foreach (var message in store.ReadAsync(session.Id))
            messages.Add(message);

        messages.Select(m => m.Text).Should().ContainInOrder("hello", "world");

        var updated = await store.GetAsync(session.Id);
        updated.Should().NotBeNull();
        updated!.MessageCount.Should().Be(2);
    }

    [Fact]
    public async Task ReplaceAsync_Rewrites_Transcript_And_MessageCount()
    {
        var store = new InMemorySessionStore();
        var session = await store.CreateAsync(new SessionCreateOptions { Title = "test" });

        await store.AppendAsync(session.Id, new ChatMessage(ChatRole.User, "hello"));
        await store.ReplaceAsync(session.Id,
        [
            new ChatMessage(ChatRole.System, "summary"),
            new ChatMessage(ChatRole.User, "latest"),
        ]);

        var messages = new List<ChatMessage>();
        await foreach (var message in store.ReadAsync(session.Id))
            messages.Add(message);

        messages.Select(m => m.Text).Should().ContainInOrder("summary", "latest");
        messages.Should().HaveCount(2);

        var updated = await store.GetAsync(session.Id);
        updated.Should().NotBeNull();
        updated!.MessageCount.Should().Be(2);
    }
}

public sealed class FileSessionStoreTests : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), "nexus-sessions-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }

    [Fact]
    public async Task Create_List_Update_Delete_Lifecycle()
    {
        var store = new FileSessionStore(_baseDirectory);
        var session = await store.CreateAsync(new SessionCreateOptions
        {
            Title = "demo",
            Metadata = new Dictionary<string, string> { ["project"] = "nexus" },
        });

        await store.AppendAsync(session.Id, new ChatMessage(ChatRole.User, "hello"));
        await store.UpdateAsync(session with
        {
            CostSnapshot = new SessionCostSnapshot(10, 20, 30, 0.01m),
        });

        var sessions = new List<SessionInfo>();
        await foreach (var item in store.ListAsync(new SessionFilter { SearchText = "demo" }))
            sessions.Add(item);

        sessions.Should().ContainSingle();
        sessions[0].CostSnapshot!.TotalTokens.Should().Be(30);

        var transcript = new List<ChatMessage>();
        await foreach (var message in store.ReadLastAsync(session.Id, 1))
            transcript.Add(message);

        transcript.Should().ContainSingle();
        transcript[0].Text.Should().Be("hello");

        (await store.DeleteAsync(session.Id)).Should().BeTrue();
        (await store.GetAsync(session.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ReplaceAsync_Rewrites_File_Transcript_And_MessageCount()
    {
        var store = new FileSessionStore(_baseDirectory);
        var session = await store.CreateAsync(new SessionCreateOptions { Title = "demo" });

        await store.AppendAsync(session.Id, new ChatMessage(ChatRole.User, "hello"));
        await store.ReplaceAsync(session.Id,
        [
            new ChatMessage(ChatRole.Assistant, "summary"),
            new ChatMessage(ChatRole.User, "latest"),
        ]);

        var transcript = new List<ChatMessage>();
        await foreach (var message in store.ReadAsync(session.Id))
            transcript.Add(message);

        transcript.Select(message => message.Text).Should().ContainInOrder("summary", "latest");

        var updated = await store.GetAsync(session.Id);
        updated.Should().NotBeNull();
        updated!.MessageCount.Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_For_Missing_Session()
    {
        var store = new FileSessionStore(_baseDirectory);

        var deleted = await store.DeleteAsync(SessionId.New());

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_Can_Filter_By_Metadata()
    {
        var store = new FileSessionStore(_baseDirectory);
        await store.CreateAsync(new SessionCreateOptions
        {
            Title = "demo",
            Metadata = new Dictionary<string, string> { ["project"] = "nexus" },
        });

        var sessions = new List<SessionInfo>();
        await foreach (var session in store.ListAsync(new SessionFilter { SearchText = "nexus" }))
            sessions.Add(session);

        sessions.Should().ContainSingle();
        sessions[0].Metadata["project"].Should().Be("nexus");
    }

    [Fact]
    public async Task ReplaceAsync_Throws_For_Missing_Session()
    {
        var store = new FileSessionStore(_baseDirectory);

        var act = () => store.ReplaceAsync(SessionId.New(), [new ChatMessage(ChatRole.User, "hello")]);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}