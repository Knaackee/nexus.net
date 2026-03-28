using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;

namespace Nexus.Memory;

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<ConversationId, List<ChatMessage>> _conversations = new();

    public Task<ConversationId> CreateAsync(string? threadId = null, CancellationToken ct = default)
    {
        var id = ConversationId.New();
        _conversations[id] = [];
        return Task.FromResult(id);
    }

    public Task AppendAsync(ConversationId id, ChatMessage message, CancellationToken ct = default)
    {
        if (!_conversations.TryGetValue(id, out var messages))
            throw new KeyNotFoundException($"Conversation {id} not found");

        lock (messages)
        {
            messages.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        ConversationId id, int? maxMessages = null, CancellationToken ct = default)
    {
        if (!_conversations.TryGetValue(id, out var messages))
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);

        lock (messages)
        {
            var result = maxMessages.HasValue
                ? messages.TakeLast(maxMessages.Value).ToList()
                : messages.ToList();

            return Task.FromResult<IReadOnlyList<ChatMessage>>(result);
        }
    }

    public Task<IReadOnlyList<ChatMessage>> GetWindowAsync(
        ConversationId id, int maxTokens, ContextTrimStrategy strategy, CancellationToken ct = default)
    {
        // Simplified: just return recent messages within token estimate
        return GetHistoryAsync(id, maxTokens / 4, ct);
    }

    public Task<ConversationId> ForkAsync(
        ConversationId parentId, Func<ChatMessage, bool>? filter = null, CancellationToken ct = default)
    {
        if (!_conversations.TryGetValue(parentId, out var parentMessages))
            throw new KeyNotFoundException($"Conversation {parentId} not found");

        var newId = ConversationId.New();
        lock (parentMessages)
        {
            var filtered = filter is not null
                ? parentMessages.Where(filter).ToList()
                : parentMessages.ToList();
            _conversations[newId] = filtered;
        }

        return Task.FromResult(newId);
    }
}
