# Memory & Context

Nexus provides three memory surfaces: **conversation history** for persistent message logs, **working memory** for ephemeral key-value state, and **long-term memory** for recalling durable facts back into a loop after compaction.

## Conversation Store

`IConversationStore` manages chat message history with context window support:

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

### Basic Usage

```csharp
var store = sp.GetRequiredService<IConversationStore>();

// Create a conversation
var convId = await store.CreateAsync("thread-001");

// Append messages
await store.AppendAsync(convId, new ChatMessage(ChatRole.User, "Hello"));
await store.AppendAsync(convId, new ChatMessage(ChatRole.Assistant, "Hi there!"));

// Retrieve full history
var history = await store.GetHistoryAsync(convId);

// Get a context window that fits within token limits
var window = await store.GetWindowAsync(convId, maxTokens: 4000, ContextTrimStrategy.SlidingWindow);
```

### Forking Conversations

Fork a conversation to create a branch (e.g., for parallel agent exploration):

```csharp
// Fork with all messages
var branchId = await store.ForkAsync(convId);

// Fork with a filter (e.g., only system + user messages)
var filteredBranch = await store.ForkAsync(convId,
    msg => msg.Role == ChatRole.System || msg.Role == ChatRole.User);
```

### Context Window Strategies

| Strategy | Behavior |
|----------|----------|
| `SlidingWindow` | Keep the most recent messages that fit within the token budget |
| `SummarizeAndTruncate` | Summarize older messages, keep recent ones verbatim |
| `KeepFirstAndLast` | Keep the system prompt + first N and last M messages |
| `TokenBudget` | Allocate token budgets per role/message type |

Configure defaults in the agent definition:

```csharp
new AgentDefinition
{
    Name = "LongContext",
    ContextWindow = new ContextWindowOptions
    {
        MaxTokens = 128_000,
        TargetTokens = 100_000,
        TrimStrategy = ContextTrimStrategy.SlidingWindow,
        ReservedForOutput = 8_000,
        ReservedForTools = 4_000,
    },
};
```

## Working Memory

`IWorkingMemory` is a typed key-value store scoped to an agent's execution:

```csharp
public interface IWorkingMemory
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
```

### Usage

```csharp
var memory = sp.GetRequiredService<IWorkingMemory>();

// Store structured data
await memory.SetAsync("user_preferences", new { Theme = "dark", Language = "en" });
await memory.SetAsync("research_findings", findings);

// Retrieve
var prefs = await memory.GetAsync<UserPreferences>("user_preferences");

// Clean up
await memory.RemoveAsync("research_findings");
await memory.ClearAsync();
```

## Long-Term Memory

`ILongTermMemory` stores durable facts and can recall them later using a query:

```csharp
public interface ILongTermMemory
{
    Task StoreAsync(string content, IDictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryResult>> RecallAsync(string query, int maxResults = 5, CancellationToken ct = default);
}
```

### Usage

```csharp
var longTermMemory = sp.GetRequiredService<ILongTermMemory>();

await longTermMemory.StoreAsync(
    "Use OAuth device flow for Copilot authentication.",
    new Dictionary<string, string> { ["topic"] = "auth" });

var recalled = await longTermMemory.RecallAsync("Copilot auth", maxResults: 3);
```

### Post-Compaction Recall

`Nexus.Compaction` and `Nexus.Memory` now compose through `ICompactionRecallProvider`.
The built-in `LongTermMemoryRecallProvider` queries `ILongTermMemory` after compaction and prepends a synthetic memory message back into the active context.

```csharp
services.AddNexus(nexus =>
{
    nexus.AddCompaction(c => c.UseDefaults());
    nexus.AddMemory(m =>
    {
        m.UseInMemory();
        m.UseLongTermMemoryRecall(options =>
        {
            options.MaxResults = 3;
            options.MinimumRelevance = 0.2;
        });
    });
    nexus.AddAgentLoop(loop => loop.UseDefaults());
});
```

By default the provider uses the latest user message before compaction as its recall query. Consumers can override `QueryFactory`, `FormatMessage`, `MaxResults`, `MinimumRelevance`, `Priority`, and `MessageRole`.

## Configuration

Enable memory in the builder:

```csharp
services.AddNexus(nexus =>
{
    nexus.AddMemory(m =>
    {
        m.UseInMemory();  // Built-in in-memory implementation
    });
});
```

## Design Notes

- `IConversationStore` and `IWorkingMemory` are **forward-declared** in `Nexus.Core.Contracts`. The in-memory implementations live in the `Nexus.Memory` package.
- `ILongTermMemory` lives in `Nexus.Memory` and can be used independently or as a post-compaction recall source.
- Messages use `Microsoft.Extensions.AI.ChatMessage` — compatible with any `IChatClient`.
- `ConversationId` is a strongly-typed wrapper around `Guid` with JSON serialization support.
