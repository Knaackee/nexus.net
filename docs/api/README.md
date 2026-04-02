# Nexus Package Index

This is the canonical package map for the `src` tree.

Use this page when you know the package name, or when you need to answer these questions quickly:

- which package owns a runtime concern
- which page is the documentation home for a package
- which guide or example to read next

## Core Runtime

| Package | Purpose | Read next |
|---|---|---|
| [Nexus.Core](nexus-core.md) | Core contracts for agents, tools, auth, events, and configuration builders. | [Core Engine](../architecture/core-engine.md) |
| [Nexus.Orchestration](nexus-orchestration.md) | Graph, sequence, parallel, and hierarchical execution. | [Orchestration Guide](../guides/orchestration.md) |
| [Nexus.AgentLoop](nexus-agent-loop.md) | Multi-turn loop with tool calls, approvals, sessions, and compaction. | [Agent Loop LLM Doc](../llms/agent-loop.md) |
| [Nexus.Workflows.Dsl](nexus-workflows-dsl.md) | Serializable workflow definitions and compilation into orchestrated execution. | [Workflows DSL Guide](../guides/workflows-dsl.md) |

## State, Memory, And Context

| Package | Purpose | Read next |
|---|---|---|
| [Nexus.Sessions](nexus-sessions.md) | Session metadata and transcript persistence for loop-based conversations. | [Memory Guide](../guides/memory.md) |
| [Nexus.Memory](nexus-memory.md) | Working memory and long-term recall integration points. | [Memory Guide](../guides/memory.md) |
| [Nexus.Compaction](nexus-compaction.md) | Context-window measurement, compaction, and post-compaction recall. | [Chat Session With Memory](../recipes/chat-session-with-memory.md) |
| [Nexus.Configuration](nexus-configuration.md) | Layered settings loading across default, project, user, managed, and runtime scopes. | [Nexus CLI](../examples/nexus-cli.md) |

## Control, Safety, And Operations

| Package | Purpose | Read next |
|---|---|---|
| [Nexus.Permissions](nexus-permissions.md) | Rule-based approvals and interactive permission prompts. | [Permissions Guide](../guides/permissions.md) |
| [Nexus.Guardrails](nexus-guardrails.md) | Input and output filtering, sanitization, and safety checks. | [Guardrails Guide](../guides/guardrails.md) |
| [Nexus.CostTracking](nexus-cost-tracking.md) | Token accounting and estimated-cost aggregation. | [Cost Tracking Guide](../guides/cost-tracking.md) |
| [Nexus.Telemetry](nexus-telemetry.md) | Activity and metric instrumentation for agents, tools, tokens, and checkpoints. | [Telemetry Guide](../guides/telemetry.md) |
| [Nexus.Auth.OAuth2](nexus-auth-oauth2.md) | OAuth2 client credentials and API-key auth strategies. | [Auth Guide](../guides/auth.md) |

## Tools, Commands, And Skills

| Package | Purpose | Read next |
|---|---|---|
| [Nexus.Tools.Standard](nexus-tools-standard.md) | Standard filesystem, search, shell, web, interaction, and sub-agent tools. | [Tools And Sub-Agents](../llms/tools-and-subagents.md) |
| [Nexus.Commands](nexus-commands.md) | Slash-command framework for interactive hosts such as the CLI. | [Nexus CLI](../examples/nexus-cli.md) |
| [Nexus.Skills](nexus-skills.md) | Skill catalog, directory loading, relevance matching, and prompt/tool injection. | [Nexus CLI](../examples/nexus-cli.md) |
| [Nexus.Defaults](nexus-defaults.md) | Opinionated composition layer that wires the common Nexus stack together. | [Quick Start](../guides/quick-start.md) |

## Hosting And Protocols

| Package | Purpose | Read next |
|---|---|---|
| [Nexus.Hosting.AspNetCore](nexus-hosting-aspnetcore.md) | ASP.NET Core endpoints and health checks for Nexus runtimes. | [Protocols Guide](../guides/protocols.md) |
| [Nexus.Protocols.Mcp](nexus-protocols-mcp.md) | MCP tool-server integration. | [Protocols Guide](../guides/protocols.md) |
| [Nexus.Protocols.A2A](nexus-protocols-a2a.md) | Agent-to-agent JSON-RPC bridge. | [Protocols Guide](../guides/protocols.md) |
| [Nexus.Protocols.AgUi](nexus-protocols-agui.md) | AG-UI event bridge for frontend streaming. | [Protocols Guide](../guides/protocols.md) |

## Workflow Durability And Testing

| Package | Purpose | Read next |
|---|---|---|
| [Nexus.Orchestration.Checkpointing](nexus-orchestration-checkpointing.md) | Snapshot persistence and resume support for orchestration graphs. | [Checkpointing Guide](../guides/checkpointing.md) |
| [Nexus.Testing](nexus-testing.md) | Test doubles, mock agents, and workflow testing helpers. | [Testing Guide](../guides/testing.md) |

## How To Read This Map

- Start with the package page when you already know the subsystem name.
- Start with a guide when you are solving a problem shape.
- Start with a recipe when you want the smallest working setup.
- Start with the LLM docs when you need fast package-to-capability routing with low token cost.