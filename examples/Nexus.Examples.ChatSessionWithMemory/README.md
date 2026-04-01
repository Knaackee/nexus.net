# Chat Session With Memory

This example is the canonical recipe for a persistent multi-turn chat with resume support and memory-ready loop wiring.

## What This Example Solves

Use this shape when:

- the same conversation continues across turns
- transcript continuity matters
- you want the runtime to manage sessions instead of hand-rolling history handling

## Project Files

- `Program.cs`: complete runnable loop-based sample
- `Nexus.Examples.ChatSessionWithMemory.csproj`: project wiring
- tests: `tests/Nexus.Examples.Tests` using `ChatSessionWithMemory_PersistsAndResumesSession`

## Run It

```powershell
dotnet run --project examples/Nexus.Examples.ChatSessionWithMemory
```

## Validate It

```powershell
dotnet test tests/Nexus.Examples.Tests --filter ChatSessionWithMemory_PersistsAndResumesSession
```

## Step By Step

### Step 1: Register loop, orchestration, sessions, compaction, and memory

This example adds more runtime surface than the single-agent example because persistent chat is inherently multi-turn.

Why this step exists:

- `AddAgentLoop(...)` provides `IAgentLoop`
- `AddOrchestration(...)` supplies the loop's required agent-pool runtime
- `AddSessions(...)` stores transcript and metadata
- `AddCompaction(...)` prepares the runtime for prompt growth
- `AddMemory(...)` keeps the example aligned with long-term memory scenarios

### Step 2: Use a deterministic chat client

The sample uses `SequentialChatClient` with two prepared answers.

Why this step exists:

- the example remains runnable without provider credentials
- resume behavior is visible from deterministic output
- the sample focuses on session behavior, not provider variability

### Step 3: Run the first turn with a new session title

The first call to `IAgentLoop.RunAsync(...)` sets `SessionTitle` and user input.

Why this step exists:

- it creates the persisted chat session
- it demonstrates the normal entry point for a new conversation
- it keeps session creation inside the runtime instead of forcing manual store calls

### Step 4: Resume the last session

The second run sets `ResumeLastSession = true` and sends a follow-up user message.

Why this step exists:

- it proves the runtime can rebuild context from stored history
- it demonstrates the intended ergonomic resume API
- it avoids custom transcript loading code in application code

### Step 5: Inspect stored session state

The sample lists sessions through `ISessionStore` and prints title and message count.

Why this step exists:

- it gives visible proof that persistence occurred
- it shows the boundary between execution (`IAgentLoop`) and storage (`ISessionStore`)
- it is a natural hook for UIs that need to list prior chats

## Source Walkthrough

### Service registration

```csharp
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
```

Why it is written this way:

- the loop requires orchestration infrastructure underneath
- sessions and memory are distinct concerns, so both are registered explicitly
- compaction is included because long-running chat eventually needs context control

### First run and resume run

```csharp
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
```

Why it is written this way:

- it shows the intended public API for new and resumed chats
- the example keeps the same agent shape across both calls
- the two runs make transcript persistence observable

## How To Adapt This To Production

1. Swap `UseInMemory()` session storage for a durable store.
2. Replace `SequentialChatClient` with a provider-backed client.
3. Tune compaction options when the transcript starts growing significantly.
4. Add long-term recall only when compacted context truly needs rehydration.

## When To Move On

Move to [Human-Approved Workflow](../Nexus.Examples.HumanApprovedWorkflow/README.md) when the chat needs explicit stages and approval checkpoints.