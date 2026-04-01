# Nexus Documentation

Complete documentation for the Nexus Multi-Agent Orchestration Engine.

## Contents

### Getting Started

- [Installation](getting-started/installation.md) — NuGet packages and project setup
- [Quick Start Entry](getting-started/quickstart.md) — Jump from setup into the runnable guide
- [Nexus CLI](getting-started/cli.md) — Interactive multi-chat coding agent

### Architecture

- [Overview](architecture/overview.md) — Layered architecture and design principles
- [Core Engine](architecture/core-engine.md) — Agents, tools, pipeline, events, DI

### Guides

- [Quick Start](guides/quick-start.md) — The fastest runnable setup from DI to first task
- [Orchestration](guides/orchestration.md) — Graph, sequence, parallel, and hierarchical execution
- [Sub-Agents](guides/sub-agents.md) — Single and batched child-agent delegation
- [Performance And Benchmarking](guides/performance-and-benchmarking.md) — What to measure, how to reason about hot paths, and where benchmark evidence lives
- [Production Hardening](guides/production-hardening.md) — Runtime controls, failure policies, and deployment guidance
- [CI And Quality Gates](guides/ci-and-quality-gates.md) — Practical verification gates for tests, coverage, docs, and benchmarks
- [Workflow Patterns And Anti-Patterns](guides/workflow-patterns-and-anti-patterns.md) — Structural heuristics for reliable staged execution
- [External Brain & Task System](guides/external-brain-task-system.md) — Using Nexus with a task backend and a graph-database brain such as Ladybug
- [Memory & Context](guides/memory.md) — Conversation history, working memory, context windows
- [Guardrails](guides/guardrails.md) — PII redaction, prompt injection detection, content safety
- [Permissions](guides/permissions.md) — Rule-based tool approval and interactive prompts
- [Cost Tracking](guides/cost-tracking.md) — Token accounting and estimated USD cost aggregation
- [Messaging](guides/messaging.md) — Inter-agent pub/sub, shared state, dead letter queue
- [Checkpointing](guides/checkpointing.md) — Snapshot serialization and resume-from-failure
- [Workflows DSL](guides/workflows-dsl.md) — JSON/YAML pipeline definitions
- [Protocols](guides/protocols.md) — MCP, A2A, and AG-UI integration
- [Telemetry](guides/telemetry.md) — OpenTelemetry traces, metrics, and audit logging
- [Auth & Security](guides/auth.md) — API keys, OAuth2, secret providers
- [Testing](guides/testing.md) — Mock agents, fake clients, evaluation harnesses
- [Middleware](guides/middleware.md) — Agent and tool pipeline extensibility

### Recipes

- [Recipe Index](recipes/README.md) — Thin scenario selector that routes to the right guide or runnable example
- [Examples Index](../examples/README.md) — Runnable step-by-step scenario examples with source and test links
- [Existing Provider UI](recipes/existing-provider-ui.md) — Keep your own provider/model picker UI and hand the runtime choices to Nexus
- [Single Agent With Tools](recipes/single-agent-with-tools.md) — Smallest useful setup for one tool-using assistant
- [Chat Session With Memory](recipes/chat-session-with-memory.md) — Session-aware loop with compaction and recall
- [Human-Approved Workflow](recipes/human-approved-workflow.md) — Research, plan, execute, review with approval gates
- [Parallel Sub-Agents And Workflow Fan-Out](recipes/parallel-subagents-and-workflow-fanout.md) — Fast specialist fan-out followed by deterministic merge stages
- [Task System + Graph Brain](recipes/task-system-graph-brain.md) — Nexus over an external task backend and Ladybug-style graph memory
- [Checkpointed Recovery Workflow](recipes/checkpointed-recovery-workflow.md) — Resume a graph instead of rerunning expensive completed stages
- [Tool-Only Worker Agent](recipes/tool-only-worker-agent.md) — Constrained worker focused on a minimal tool surface
- [Cost-Aware Batch Processing](recipes/cost-aware-batch-processing.md) — Bounded execution under explicit token and cost budgets

### LLM Docs

- [LLM Docs Index](llms/README.md) — Concise runtime map for retrieval and prompting
- [Runtime Map](llms/runtime-map.md) — Package-to-responsibility map
- [Agent Loop](llms/agent-loop.md) — Multi-turn execution surface
- [Workflows DSL](llms/workflows-dsl.md) — Serializable workflow model and execution bridge
- [Tools And Sub-Agents](llms/tools-and-subagents.md) — Tool system and delegated child-agent model
- [Testing And Benchmarks](llms/testing-and-benchmarks.md) — Evidence surfaces and test helpers
- [Glossary](llms/glossary.md) — Stable runtime terminology

### API Reference

- [Nexus.Core](api/nexus-core.md) — Core abstractions and contracts
- [Nexus.Orchestration](api/nexus-orchestration.md) — Orchestrator, agent pool, task graphs
- [Nexus.Memory](api/nexus-memory.md) — Conversation store and working memory
- [Nexus.Guardrails](api/nexus-guardrails.md) — Guardrail pipeline and built-in guards
- [Nexus.Permissions](api/nexus-permissions.md) — Rule-based tool approval and prompts
- [Nexus.CostTracking](api/nexus-cost-tracking.md) — Cost tracker, pricing provider, chat client wrapper
- [Nexus.Messaging](api/nexus-messaging.md) — Message bus and shared state
- [Nexus.Workflows.Dsl](api/nexus-workflows-dsl.md) — Workflow loader, validator, serializer
- [Nexus.Protocols](api/nexus-protocols.md) — MCP, A2A, AG-UI adapters
- [Nexus.Testing](api/nexus-testing.md) — Test utilities and mocks

### Examples

- [Examples Index](../examples/README.md) — Canonical home for runnable scenario examples
- [Minimal Agent](examples/minimal.md) — Single agent with tools and guardrails
- [Multi-Agent Graph](examples/multi-agent.md) — Graph orchestration with checkpointing
- [Nexus CLI](examples/nexus-cli.md) — GitHub Copilot multi-chat agent

### Benchmarks

- [Benchmarks README](../benchmarks/README.md) — Run and interpret the runtime benchmark suite
