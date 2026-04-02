using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Nexus.Testing.Mocks;

/// <summary>
/// A fake IChatClient that returns pre-configured responses.
/// Useful for unit testing agents without calling real LLM providers.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new();
    private readonly Queue<List<ChatResponseUpdate>> _streamingResponses = new();

    public List<IList<ChatMessage>> ReceivedMessages { get; } = [];

    public FakeChatClient(params string[] responses)
    {
        foreach (var r in responses)
        {
            _responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, r)));
            _streamingResponses.Enqueue([new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(r)] }]);
        }
    }

    public FakeChatClient WithResponse(string text)
    {
        _responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        _streamingResponses.Enqueue([new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(text)] }]);
        return this;
    }

    public FakeChatClient WithStreamingResponse(params string[] chunks)
    {
        var updates = chunks.Select(c => new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(c)] }).ToList();
        _streamingResponses.Enqueue(updates);
        var fullText = string.Join("", chunks);
        _responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, fullText)));
        return this;
    }

    public FakeChatClient WithStreamingUpdates(IEnumerable<ChatResponseUpdate> updates, ChatMessage? finalMessage = null)
    {
        var materialized = updates.ToList();
        _streamingResponses.Enqueue(materialized);
        _responses.Enqueue(new ChatResponse(finalMessage ?? new ChatMessage(ChatRole.Assistant, string.Empty)));
        return this;
    }

    public FakeChatClient WithReasoningResponse(string reasoning, string text)
    {
        return WithStreamingUpdates(
        [
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextReasoningContent(reasoning)]
            },
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(text)]
            }
        ],
        new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent(reasoning),
            new TextContent(text)
        ]));
    }

    public FakeChatClient WithFunctionCallResponse(params FunctionCallContent[] functionCalls)
    {
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = functionCalls.Cast<AIContent>().ToList()
        };
        _streamingResponses.Enqueue([update]);

        var message = new ChatMessage(ChatRole.Assistant, functionCalls.Cast<AIContent>().ToList());
        _responses.Enqueue(new ChatResponse(message));
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add(messages.ToList());
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, "[No more responses configured]"));
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add(messages.ToList());
        var updates = _streamingResponses.Count > 0
            ? _streamingResponses.Dequeue()
            : [new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("[No more responses configured]")] }];

        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
