# Recipe: Chat Session With Memory

Use this when your application is not just a one-shot task runner, but an ongoing session that needs continuity.

## Not A Good Fit

Do not start here if the task is single-shot and stateless. Sessions, transcripts, and compaction add operational surface that you should only carry when continuity is real.

## Source-Backed Asset

- runnable example: [../../examples/Nexus.Examples.ChatSessionWithMemory/README.md](../../examples/Nexus.Examples.ChatSessionWithMemory/README.md)

## Good Fit

This recipe is a good fit if:

- the same user or agent continues across turns
- the transcript can grow large
- you need resume support
- you may need compaction and post-compaction recall

## Core Pieces

Use these Nexus components:

- `AddAgentLoop(loop => loop.UseDefaults())`
- `AddSessions(...)`
- `AddCompaction(compaction => compaction.UseDefaults())`
- `AddMemory(...)` if you want recall or working memory

Optional:

- `ICompactionRecallProvider` for rehydrating durable knowledge after compaction

## Minimal Wiring

```csharp
services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => chatClient);
    nexus.AddAgentLoop(loop => loop.UseDefaults());
    nexus.AddSessions(s => s.UseInMemory());
    nexus.AddCompaction(c => c.UseDefaults());
    nexus.AddMemory(m =>
    {
        m.UseInMemory();
        m.UseLongTermMemoryRecall();
    });
});

var sp = services.BuildServiceProvider();
var loop = sp.GetRequiredService<IAgentLoop>();

await foreach (var evt in loop.RunAsync(new AgentLoopOptions
{
    AgentDefinition = new AgentDefinition
    {
        Name = "SessionAssistant",
        SystemPrompt = "Keep track of the conversation and use tools when useful.",
    },
    UserInput = "Help me continue the implementation.",
    SessionTitle = "Implementation chat",
}))
{
    Console.WriteLine(evt);
}
```

## Mental Model

The active prompt is not the same thing as durable memory.

- session transcript: full conversational history
- active context: the subset currently inside the prompt window
- long-term memory: facts recalled back in after compaction

That separation is what keeps this setup understandable.

## When To Add Recall

Add a recall provider when:

- compaction removes details the agent still needs later
- durable facts live in an external store
- the same domain context keeps reappearing across turns

If all relevant state already lives in the transcript, compaction alone may be enough.

## Common Next Step

If the session needs explicit stages such as research, plan, execute, and review, move next to [Human-Approved Workflow](human-approved-workflow.md).

## Related Guides

- [Memory & Context](../guides/memory.md)
- [Checkpointing](../guides/checkpointing.md)
- [External Brain & Task System](../guides/external-brain-task-system.md)

## Read Next

- staged flow on top of a loop: [Human-Approved Workflow](human-approved-workflow.md)
- package details: [Nexus.AgentLoop](../api/nexus-agent-loop.md)