using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Compaction;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Memory;
using Nexus.Orchestration;
using Nexus.Sessions;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => new SequentialChatClient("First reply with memory.", "Second reply after resume."));
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddAgentLoop(loop => loop.UseDefaults());
    nexus.AddSessions(s => s.UseInMemory());
    nexus.AddCompaction(c => c.UseDefaults());
    nexus.AddMemory(m =>
    {
        m.UseInMemory();
        m.UseLongTermMemoryRecall();
    });
});

await using var serviceProvider = services.BuildServiceProvider();
var loop = serviceProvider.GetRequiredService<IAgentLoop>();
var sessions = serviceProvider.GetRequiredService<ISessionStore>();

await DrainAsync(loop.RunAsync(new AgentLoopOptions
{
    AgentDefinition = new AgentDefinition { Name = "SessionAssistant", SystemPrompt = "Keep track of prior turns." },
    UserInput = "Remember that the deployment window starts at 18:00 UTC.",
    SessionTitle = "Recipe memory demo",
}));

await DrainAsync(loop.RunAsync(new AgentLoopOptions
{
    AgentDefinition = new AgentDefinition { Name = "SessionAssistant", SystemPrompt = "Keep track of prior turns." },
    ResumeLastSession = true,
    UserInput = "What did I tell you about the deployment window?",
}));

await foreach (var session in sessions.ListAsync())
{
    Console.WriteLine($"Session: {session.Title} ({session.MessageCount} messages)");
}

static async Task DrainAsync(IAsyncEnumerable<AgentLoopEvent> stream)
{
    await foreach (var evt in stream)
    {
        if (evt is LoopCompletedEvent completed)
            Console.WriteLine($"Loop finished: {completed.FinalResult.Text}");
    }
}

sealed class SequentialChatClient : IChatClient
{
    private readonly Queue<string> _responses;

    public SequentialChatClient(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = _responses.Count > 0 ? _responses.Dequeue() : "No configured response.";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = response.Messages[^1].Contents,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}