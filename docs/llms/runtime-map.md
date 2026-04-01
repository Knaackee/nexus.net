# Runtime Map

Nexus is split into a small set of stable runtime responsibilities.

## Core Packages

- `Nexus.Core`: base contracts for agents, tools, approvals, events, and configuration
- `Nexus.Orchestration`: task graphs, sequencing, dependency edges, and execution policies
- `Nexus.AgentLoop`: multi-turn execution, routing, approvals, and loop events
- `Nexus.Workflows.Dsl`: workflow definition loading, validation, compilation, and execution bridge
- `Nexus.Tools.Standard`: filesystem, search, shell, web, interaction, and sub-agent tools
- `Nexus.Sessions`: persistent chat sessions and transcripts
- `Nexus.Memory`: working memory and long-term recall hooks
- `Nexus.Compaction`: context-window compaction strategies and recall integration

## Supporting Packages

- `Nexus.Permissions`: approval and policy surface for risky tool execution
- `Nexus.Guardrails`: input and output filtering, sanitization, and detection
- `Nexus.CostTracking`: token and estimated-cost accounting
- `Nexus.Messaging`: inter-agent message bus and shared state patterns
- `Nexus.Orchestration.Checkpointing`: orchestration snapshots and resume support
- `Nexus.Protocols.*`: protocol adapters for MCP, A2A, and AG-UI
- `Nexus.Hosting.AspNetCore`: HTTP hosting surface for runtime exposure
- `Nexus.Testing`: fake clients, mock agents, mock approval gates, and test helpers

## Main Execution Shapes

- one-shot tool-using assistant: `Nexus.Orchestration`
- multi-turn session chat: `Nexus.AgentLoop` plus `Nexus.Sessions`
- staged workflow: `Nexus.AgentLoop` with `WorkflowRoutingStrategy`
- DAG workflow execution: `Nexus.Workflows.Dsl` plus `Nexus.Orchestration`
- delegated specialists: `Nexus.Tools.Standard` `agent` tool