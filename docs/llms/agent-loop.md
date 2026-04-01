# Agent Loop

`Nexus.AgentLoop` is the runtime for multi-turn execution.

## Primary Entry Points

- `IAgentLoop.RunAsync(AgentLoopOptions, CancellationToken)`
- `AgentLoopOptions`
- `IRoutingStrategy`
- `WorkflowRoutingStrategy`

## What The Loop Does

- resolves or creates an agent
- optionally resolves or resumes a session
- builds message history
- applies compaction when needed
- executes one step or routed steps
- emits structured loop events

## When To Use It

- the same conversation continues across turns
- a step may pause for approval
- workflow routing decides the next step
- you need session persistence and resume behavior

## Important Events

- `LoopStartedEvent`
- `TextChunkLoopEvent`
- `ToolCallStartedLoopEvent`
- `ToolCallCompletedLoopEvent`
- `ApprovalRequestedLoopEvent`
- `CompactionTriggeredLoopEvent`
- `LoopCompletedEvent`

## Common Pairings

- sessions: `Nexus.Sessions`
- compaction: `Nexus.Compaction`
- approval: `Nexus.Core.Contracts.IApprovalGate`
- workflow routing: `Nexus.AgentLoop.WorkflowRoutingStrategy`