# Recipe: Task System + Graph Brain

Use this when your application already has a real task backend and a real knowledge system, and Nexus should sit on top as the agent runtime.

## Good Fit

This recipe is a good fit if:

- tasks already live in another system
- Ladybug or another graph database acts as the "brain"
- Nexus should coordinate reasoning, tool use, approvals, and recall

## Core Pieces

Use these Nexus components:

- `ITool` adapters for task operations
- `ITool` adapters or recall providers for graph access
- `IAgentLoop` for the interactive session
- `WorkflowRoutingStrategy` if the flow is staged
- `ICompactionRecallProvider` if graph context should survive compaction

## Minimal Architecture

```text
User
  -> Nexus AgentLoop
      -> task_* tools -> external task system
      -> graph_* tools -> Ladybug
      -> compaction -> LadybugRecallProvider -> active context
```

## Recommended Rules

- keep task truth in the task backend
- keep semantic truth in Ladybug
- keep only active working context in Nexus
- use recall to rehydrate facts, not to replace transactional reads

## Smallest Useful Wiring

```csharp
services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => chatClient);
    nexus.AddAgentLoop(loop => loop.UseDefaults());
    nexus.AddCompaction(c => c.UseDefaults());
    nexus.AddMemory(m => m.UseInMemory());
    nexus.AddPermissions(p => p.UsePreset(PermissionPresets.Interactive));
});

services.AddSingleton<ITool, TaskGetTool>();
services.AddSingleton<ITool, TaskUpdateStatusTool>();
services.AddSingleton<ITool, LadybugQueryTool>();
services.AddSingleton<ICompactionRecallProvider, LadybugRecallProvider>();
```

## What To Avoid

Avoid duplicating whole task objects or whole graph subtrees into Nexus memory.

Instead:

- fetch exact task truth from the task tool
- fetch exact graph truth from Ladybug
- only inject the smallest recalled facts back into the active prompt

## Related Guides

- [External Brain & Task System](../guides/external-brain-task-system.md)
- [Memory & Context](../guides/memory.md)
- [Human-Approved Workflow](human-approved-workflow.md)