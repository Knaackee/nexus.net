# Changelog

All notable changes to Nexus will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [0.3.6] — 2026-04-12

### Changed

- Hardened injected `AskUserPolicy` system guidance so agents must prefer `ask_user` for decision points and avoid plain-text decision menus when tool is available
- Added explicit guidance for mandatory ask_user situations (ambiguity, missing required parameters, multiple execution paths, risky/irreversible actions)
- Clarified canonical typed-question usage in policy text (`confirm`, `select`, `multiSelect`, `freeText`, `secret`) and fallback behavior when ask_user is unavailable

### Tests

- Updated policy assertion markers in `Nexus.AgentLoop.Tests` to validate hardened policy injection behavior
- Verified ask_user-related suites: `Nexus.AgentLoop.Tests`, `Nexus.Orchestration.Tests`, and `Nexus.Tools.Standard.Tests`

## [0.3.5] — 2026-04-11

### Fixed

- `ask_user` argument parsing now accepts legacy `inputType` when `type` is missing and normalizes typed modes case-insensitively (`freeText`, `confirm`, `select`, `multiSelect`, `secret`)
- `ask_user` no longer silently downgrades unknown typed modes to `freeText`; invalid `type`/`inputType` now fail with actionable validation errors
- `select` and `multiSelect` now require non-empty `options`; malformed payloads fail explicitly instead of emitting ambiguous prompts
- Orchestration `UserInputRequest` emission now resolves normalized typed mode consistently and avoids emitting malformed typed requests
- Added diagnostics counters for ask_user parsing source, unknown/mismatch values, missing options validation, and resolved input type distribution

## [0.3.1] — 2026-04-11

### Added

- `AskUserPolicy`: compact prompt policy automatically appended to agent system prompts when `ask_user` is present in `ToolNames` — guides the LLM to ask before acting on ambiguous intent, confirm before destructive/costly actions, limit unverified assumptions, and prefer `confirm`/`select` question types over free text
- Policy is conditional: injected only when `ask_user` is in the active tool list, leaving all other agent system prompts untouched
- 9 new tests covering policy injection, ambiguous-intent and risky-action scenarios, and full e2e message verification
- Updated docs: `nexus-tools-standard`, `nexus-orchestration` API references, and `testing` guide

## [0.3.0] — 2026-04-02

### Added

- Structured Priority-1 streaming support for reasoning, tool-use, approval requests, and ask-user interaction requests across agent events, AG-UI, and session transcripts
- New AG-UI event shapes for reasoning chunks, approval requests, and structured user input requests
- New testing helpers in `FakeChatClient` for reasoning-aware streaming responses

### Changed

- `ChatAgent` now preserves structured assistant contents instead of rebuilding final turns from plain text alone
- `FileSessionStore` now persists structured chat contents in addition to text for backwards-compatible transcript roundtrips
- Updated example and docs to show structured streaming behavior for tool use and reasoning

## [0.2.1] — 2026-04-02

### Fixed

- Replaced `StreamReader.EndOfStream` checks inside async streaming loops so CI and release builds no longer fail on `CA2024`
- Restored release pipeline compatibility for `Nexus.Cli` and the live Ollama integration test client

## [0.2.0] — 2026-04-02

### Added

- Canonical package index at `docs/api/README.md` covering the full `src` package surface
- Dedicated package docs for AgentLoop, Auth.OAuth2, Commands, Compaction, Configuration, Defaults, Hosting.AspNetCore, Orchestration.Checkpointing, Sessions, Skills, Telemetry, Tools.Standard, and split protocol pages for MCP, A2A, and AG-UI

### Changed

- Strengthened `docs/llms/README.md` for low-token first-hop routing by problem shape and package family
- Improved root and docs index discoverability so README, docs index, API index, guides, recipes, and examples route consistently
- Refined key guides and recipes with clearer scope boundaries, quick selectors, and stronger next-hop navigation for both humans and LLMs

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
