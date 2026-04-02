# Changelog

All notable changes to Nexus will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0] — 2026-04-02

### Added

- **Nexus.Core** — Core abstractions: `IAgent`, `ITool`, pipeline middleware, events, DI integration
- **Nexus.Orchestration** — Graph-based multi-agent orchestration with sequence, parallel, and hierarchical execution
- **Nexus.Orchestration.Checkpointing** — Snapshot serialization and in-memory checkpoint store
- **Nexus.AgentLoop** — Session-aware execution loop for agents with streaming events
- **Nexus.Sessions** — Session persistence and transcript storage
- **Nexus.Compaction** — Context window compaction strategies
- **Nexus.Configuration** — Hierarchical settings and configuration providers
- **Nexus.Memory** — Conversation history and working memory
- **Nexus.Messaging** — Inter-agent pub/sub messaging, shared state, dead letter queue
- **Nexus.Guardrails** — PII redaction, prompt injection detection, secrets scanning
- **Nexus.Permissions** — Tool approval rules and permission policies
- **Nexus.CostTracking** — Token counting and USD cost tracking per agent and session
- **Nexus.Telemetry** — OpenTelemetry traces and metrics middleware
- **Nexus.Auth.OAuth2** — API key auth, OAuth2 client credentials, token cache
- **Nexus.Commands** — Slash command framework for interactive sessions
- **Nexus.Skills** — Skill injection middleware
- **Nexus.Tools.Standard** — Built-in tools: file, shell, grep, web, sub-agent
- **Nexus.Protocols.Mcp** — Model Context Protocol tool adapter
- **Nexus.Protocols.A2A** — Agent-to-Agent protocol client with JSON-RPC transport
- **Nexus.Protocols.AgUi** — AG-UI event bridge for frontend streaming
- **Nexus.Workflows.Dsl** — JSON and YAML workflow definition language
- **Nexus.Hosting.AspNetCore** — ASP.NET Core endpoints for A2A, AG-UI SSE, health checks
- **Nexus.Testing** — Mock agents, fake LLM clients, event recording
- **Nexus.Defaults** — Batteries-included convenience wiring
- 8 runnable examples: Minimal, SingleAgentWithTools, ChatSessionWithMemory, HumanApprovedWorkflow, ParallelSubAgentsAndWorkflowFanOut, MultiAgent, ChatEditingWithDiffAndRevert, CLI
- 20 guides, 10 recipes, 10 API docs, 7 LLM-optimized docs
- Benchmark suite for workflow compilation and sub-agent execution
- CI pipeline with build, test, and release workflow
