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

## Namespace: Nexus.Memory

### ILongTermMemory

```csharp
public interface ILongTermMemory
{
    Task StoreAsync(string content, IDictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryResult>> RecallAsync(string query, int maxResults = 5, CancellationToken ct = default);
}
```

### MemoryResult

```csharp
public record MemoryResult(string Content, double Relevance, IDictionary<string, string> Metadata);
```

### LongTermMemoryRecallProvider

Implements `Nexus.Compaction.ICompactionRecallProvider` and recalls durable facts from `ILongTermMemory` after compaction.

```csharp
public sealed class LongTermMemoryRecallProvider : ICompactionRecallProvider
{
    public int Priority { get; }
    public Task<IReadOnlyList<ChatMessage>> RecallAsync(CompactionRecallContext context, CancellationToken ct = default);
}
```

### LongTermMemoryRecallOptions

```csharp
public sealed class LongTermMemoryRecallOptions
{
    public int Priority { get; set; } = 100;
    public int MaxResults { get; set; } = 3;
    public double MinimumRelevance { get; set; } = 0.05;
    public ChatRole MessageRole { get; set; } = ChatRole.System;
    public Func<CompactionRecallContext, string> QueryFactory { get; set; }
    public Func<IReadOnlyList<MemoryResult>, string> FormatMessage { get; set; }
}
```

### MemoryBuilder Extensions

```csharp
public static class MemoryServiceCollectionExtensions
{
    public static MemoryBuilder UseInMemory(this MemoryBuilder builder);
    public static MemoryBuilder UseLongTermMemoryRecall(this MemoryBuilder builder, Action<LongTermMemoryRecallOptions>? configure = null);
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
