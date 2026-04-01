# Tools And Sub-Agents

Tools are the agent-facing interface to side effects and external capabilities.

## Core Concepts

- `ITool`: executable capability
- `IToolRegistry`: tool discovery and resolution
- `ToolResult`: success or failure payload
- `ToolAnnotations`: read-only, approval, and idempotency metadata

## Standard Tools

`Nexus.Tools.Standard` provides these categories:

- filesystem
- search
- shell
- web fetch
- user interaction
- sub-agents

## Agent Tool

The `agent` tool delegates work to child agents.

Supported shapes:

- single child request
- batched requests through `tasks[]`
- bounded parallelism through `maxConcurrency`

## Use The Agent Tool When

- a coordinator needs quick specialist fan-out
- the delegated work is local to one step
- you want tool-driven delegation before a later workflow stage

## Use Graph Orchestration Instead When

- dependencies between tasks must be explicit
- merge, retry, and skip behavior must be modeled structurally
- checkpointing or graph-level concurrency policies matter