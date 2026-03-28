# Nexus.Memory API Reference

## Namespace: Nexus.Core.Contracts

> These interfaces are forward-declared in `Nexus.Core`. Implementations live in `Nexus.Memory`.

### IConversationStore

```csharp
public interface IConversationStore
{
    Task<ConversationId> CreateAsync(string? threadId = null, CancellationToken ct = default);
    Task AppendAsync(ConversationId id, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ConversationId id, int? maxMessages = null, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetWindowAsync(ConversationId id, int maxTokens, ContextTrimStrategy strategy, CancellationToken ct = default);
    Task<ConversationId> ForkAsync(ConversationId parentId, Func<ChatMessage, bool>? filter = null, CancellationToken ct = default);
}
```

### IWorkingMemory

```csharp
public interface IWorkingMemory
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
```

### ConversationId

```csharp
public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New();
    public override string ToString();  // Returns first 8 hex chars
}
```

JSON serialization is handled by `ConversationIdJsonConverter`.
