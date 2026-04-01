# External Brain & Task System

Use this pattern when your application already has:

- a task system that is the source of truth for work items, queues, or execution state
- a graph database that acts as the "brain" or semantic memory layer
- Nexus only for agent execution, routing, tool orchestration, approvals, and session handling

This is a good fit for a setup such as:

- Task backend: your own task engine, scheduler, or workflow runtime
- Brain: Ladybug or another graph database for entities, relationships, history, and semantic context
- Nexus: agent loop, tools, approvals, compaction, and routing

## When Nexus Fits

Nexus fits well if you want agents to:

- reason over tasks without owning the task lifecycle
- query and update graph knowledge without replacing the graph database
- run multi-turn tool loops with approvals, compaction, and resume support
- move through explicit workflow steps such as research, plan, execute, and review

Nexus is not the right place to become the system of record for either your task state or your graph semantics. Keep those responsibilities in the external systems and let Nexus consume them.

## Recommended Role Split

Use a strict separation of concerns:

| Concern | Recommended owner |
|---------|-------------------|
| Task lifecycle, assignment, status transitions | External task system |
| Durable entities, relationships, semantic recall | Ladybug graph database |
| Agent execution, tool loops, approvals, session flow | Nexus |
| Short-lived active prompt context | Nexus AgentLoop |
| Long-lived facts rehydrated after compaction | Ladybug via Nexus recall provider |

This avoids duplicate truth and keeps each system doing the job it is already good at.

## Integration Model

The cleanest design has three integration layers.

### 1. Task Tool Layer

Expose your task system through one or more Nexus tools.

Typical tools:

- `task_list`
- `task_get`
- `task_update_status`
- `task_assign`
- `task_append_note`
- `task_create_dependency`

These tools should remain thin adapters over the real task backend.

```csharp
public sealed class TaskGetTool : ITool
{
    private readonly ITaskBackend _backend;

    public TaskGetTool(ITaskBackend backend)
    {
        _backend = backend;
    }

    public string Name => "task_get";
    public string Description => "Loads a task from the external task system.";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        var taskId = input.GetProperty("taskId").GetString()
            ?? throw new InvalidOperationException("taskId is required.");

        var task = await _backend.GetAsync(taskId, ct);
        return ToolResult.Success($"Task {task.Id}: {task.Title} ({task.Status})");
    }
}
```

### 2. Brain Access Layer

Expose Ladybug through either tools, recall providers, or both.

Use tools when the agent needs deliberate graph operations:

- `graph_query`
- `graph_neighbors`
- `graph_upsert_fact`
- `graph_link_task`

Use a recall provider when the agent needs relevant graph context reintroduced automatically after compaction.

### 3. Nexus Orchestration Layer

Use Nexus for the agent runtime itself:

- `IAgentLoop` for session-aware execution
- `IRoutingStrategy` when the conversation follows explicit steps
- `IApprovalGate` for step-level or tool-level human approval
- `ICompactionService` and `ICompactionRecallProvider` for large-context recovery

## Reference Architecture

```text
User
  -> Nexus AgentLoop
      -> task_* tools -> external task system
      -> graph_* tools -> Ladybug
      -> compaction -> recall provider -> Ladybug
      -> routing strategy -> research / plan / execute / review
```

The important point is that Nexus coordinates, but the external systems remain authoritative.

## Ladybug as Post-Compaction Recall

If Ladybug acts as the durable brain, the best Nexus integration is usually a custom `ICompactionRecallProvider`.

That gives you this flow:

1. Nexus compacts the active conversation.
2. The recall provider queries Ladybug using the latest user intent, task state, or graph anchors.
3. Relevant facts are inserted back into the active prompt as a synthetic system or assistant message.
4. The loop continues with a smaller but still informed context window.

Conceptually:

```csharp
public sealed class LadybugRecallProvider : ICompactionRecallProvider
{
    private readonly ILadybugClient _ladybug;

    public LadybugRecallProvider(ILadybugClient ladybug)
    {
        _ladybug = ladybug;
    }

    public int Priority => 50;

    public async Task<IReadOnlyList<ChatMessage>> RecallAsync(CompactionRecallContext context, CancellationToken ct = default)
    {
        var query = context.OriginalMessages
            .Where(message => message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text))
            .Select(message => message.Text!)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(query))
            return context.ActiveMessages;

        var facts = await _ladybug.RecallFactsAsync(query, maxResults: 5, ct);
        if (facts.Count == 0)
            return context.ActiveMessages;

        var recalled = new ChatMessage(
            ChatRole.System,
            "[Recalled graph context]\n" + string.Join("\n", facts.Select(fact => $"- {fact}")));

        return [recalled, .. context.ActiveMessages];
    }
}
```

If Ladybug already exposes a query API with ranking or graph-distance scoring, use that instead of trying to recreate ranking inside Nexus.

## Suggested Workflow Shape

For mixed task-and-brain systems, a routed loop is often more useful than a raw single-agent loop.

Typical steps:

1. `Research`
   Pull current task state and graph context.
2. `Plan`
   Decide what should change in the task system and what facts matter.
3. `Execute`
   Call task tools and graph tools.
4. `Review`
   Summarize changes, ask for approval if needed, and emit the next recommended action.

This maps naturally to `WorkflowRoutingStrategy` if the sequence is mostly fixed.

## Minimal Service Registration

```csharp
services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => chatClient);

    nexus.AddMemory(memory =>
    {
        memory.UseInMemory();
    });

    nexus.AddCompaction(compaction => compaction.UseDefaults());
    nexus.AddAgentLoop(loop => loop.UseDefaults());
    nexus.AddOrchestration(orchestration => orchestration.UseDefaults());
    nexus.AddPermissions(p => p.UsePreset(PermissionPresets.Interactive));
});

services.AddSingleton<ITaskBackend, ExternalTaskBackend>();
services.AddSingleton<ILadybugClient, LadybugClient>();
services.AddSingleton<ITool, TaskGetTool>();
services.AddSingleton<ITool, TaskUpdateStatusTool>();
services.AddSingleton<ITool, LadybugQueryTool>();
services.AddSingleton<ICompactionRecallProvider, LadybugRecallProvider>();
```

If your Ladybug integration is purely recall-oriented, you may not need graph tools at all. If the agent must also mutate the graph, keep recall and mutation separate.

## What Not To Do

Avoid these mistakes:

### Do not mirror the whole task system into Nexus memory

Keep task truth in the task backend. Nexus memory should only hold ephemeral working context or compacted recall text.

### Do not treat graph recall as a replacement for deterministic task reads

If the agent needs the current task status, ask the task backend directly through a tool. Graph recall is context enrichment, not transactional truth.

### Do not push graph traversal semantics into the agent loop

Traversal depth, ranking, edge filtering, and consistency rules belong in Ladybug or in a thin integration client.

## Decision Checklist

This pattern is a good fit if the answer to most of these is yes:

- You already have a reliable task backend.
- You already have a graph brain such as Ladybug.
- You want Nexus to orchestrate agent behavior rather than replace your domain systems.
- You need multi-turn tool use, approvals, resume, or compaction.
- You want graph context to survive compaction without bloating every prompt.

If that is your shape, Nexus is a strong orchestration layer on top of your existing systems.

## Next Step Options

The next concrete implementation is usually one of these:

1. Build thin `task_*` tools over the external task backend.
2. Add a `LadybugRecallProvider` for post-compaction graph recall.
3. Introduce a `WorkflowRoutingStrategy` with `Research -> Plan -> Execute -> Review`.
4. Add permission rules so mutating task or graph operations require explicit approval.