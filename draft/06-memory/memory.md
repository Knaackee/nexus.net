# Memory & Context — Nexus.Memory

> Assembly: `Nexus.Memory`  
> Deps: `Nexus.Core`

## 1. Drei Memory-Ebenen

| Ebene | Interface | Lebensdauer | Default |
|-------|-----------|-------------|---------|
| Conversation | `IConversationStore` | Session | InMemory |
| Working | `IWorkingMemory` | Task-Ausführung | InMemory |
| Long-Term | `ILongTermMemory` | Persistent | Braucht Vector Store |

## 2. IConversationStore

```csharp
public interface IConversationStore
{
    Task<ConversationId> CreateAsync(string? threadId = null, CancellationToken ct = default);
    Task AppendAsync(ConversationId id, ChatMessage message, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ConversationId id, int? maxMessages = null, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetWindowAsync(ConversationId id, int maxTokens, ContextTrimStrategy strategy, CancellationToken ct = default);
    Task<ConversationId> ForkAsync(ConversationId parentId, Func<ChatMessage, bool>? filter = null, CancellationToken ct = default);
}
```

**Fork** ist entscheidend für Multi-Agent: Ein Sub-Agent bekommt eine gefilterte Kopie der Conversation, nicht den gesamten Verlauf.

## 3. IContextWindowManager

```csharp
public interface IContextWindowManager
{
    int EstimateTokens(IEnumerable<ChatMessage> messages, string? modelId = null);
    IReadOnlyList<ChatMessage> Trim(IReadOnlyList<ChatMessage> messages, int maxTokens, ContextTrimStrategy strategy);
    Task<IReadOnlyList<ChatMessage>> CompressAsync(IReadOnlyList<ChatMessage> messages, int targetTokens, IChatClient summarizer, CancellationToken ct);
}

public enum ContextTrimStrategy
{
    SlidingWindow,             // Älteste weg, System-Prompt bleibt
    SummarizeAndTruncate,      // Alte zusammenfassen, dann trimmen
    KeepFirstAndLast,          // System + letzte N
    TokenBudget                // Budget pro Message-Typ
}
```

### ContextWindowOptions

```csharp
public record ContextWindowOptions
{
    public int MaxTokens { get; init; } = 128_000;
    public int TargetTokens { get; init; } = 100_000;
    public ContextTrimStrategy TrimStrategy { get; init; } = ContextTrimStrategy.SlidingWindow;
    public int ReservedForOutput { get; init; } = 8_000;
    public int ReservedForTools { get; init; } = 4_000;
}
```

## 4. IWorkingMemory

```csharp
public interface IWorkingMemory
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}
```

## 5. ILongTermMemory

```csharp
public interface ILongTermMemory
{
    Task StoreAsync(string content, IDictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryResult>> RecallAsync(string query, int maxResults = 5, CancellationToken ct = default);
}

public record MemoryResult(string Content, double Relevance, IDictionary<string, string> Metadata);
```

## 6. Backends

| Backend | Paket | Use Case |
|---------|-------|----------|
| InMemory | `Nexus.Memory` | Dev/Test |
| Redis | `Nexus.Memory.Redis` | Production Working Memory + Conversation |
| Qdrant | `Nexus.Memory.Qdrant` | Production Long-Term (Vector Search) |
| Postgres | `Nexus.Memory.Postgres` | Production Conversation + Working |

## 7. Registrierung

```csharp
n.AddMemory(m =>
{
    m.UseRedis("localhost:6379");         // Conversation + Working
    m.UseQdrant("http://localhost:6333"); // Long-Term
});
```
