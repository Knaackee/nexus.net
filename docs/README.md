# Nexus Documentation

Complete documentation for the Nexus Multi-Agent Orchestration Engine.

## Contents

### Getting Started

- [Installation](getting-started/installation.md) — NuGet packages and project setup
- [Quick Start](getting-started/quickstart.md) — Your first agent in 5 minutes
- [Nexus CLI](getting-started/cli.md) — Interactive multi-chat coding agent

### Architecture

- [Overview](architecture/overview.md) — Layered architecture and design principles
- [Core Engine](architecture/core-engine.md) — Agents, tools, pipeline, events, DI

### Guides

- [Orchestration](guides/orchestration.md) — Graph, sequence, parallel, and hierarchical execution
- [Memory & Context](guides/memory.md) — Conversation history, working memory, context windows
- [Guardrails](guides/guardrails.md) — PII redaction, prompt injection detection, content safety
- [Messaging](guides/messaging.md) — Inter-agent pub/sub, shared state, dead letter queue
- [Checkpointing](guides/checkpointing.md) — Snapshot serialization and resume-from-failure
- [Workflows DSL](guides/workflows-dsl.md) — JSON/YAML pipeline definitions
- [Protocols](guides/protocols.md) — MCP, A2A, and AG-UI integration
- [Telemetry](guides/telemetry.md) — OpenTelemetry traces, metrics, and audit logging
- [Auth & Security](guides/auth.md) — API keys, OAuth2, secret providers
- [Testing](guides/testing.md) — Mock agents, fake clients, evaluation harnesses
- [Middleware](guides/middleware.md) — Agent and tool pipeline extensibility

### API Reference

- [Nexus.Core](api/nexus-core.md) — Core abstractions and contracts
- [Nexus.Orchestration](api/nexus-orchestration.md) — Orchestrator, agent pool, task graphs
- [Nexus.Memory](api/nexus-memory.md) — Conversation store and working memory
- [Nexus.Guardrails](api/nexus-guardrails.md) — Guardrail pipeline and built-in guards
- [Nexus.Messaging](api/nexus-messaging.md) — Message bus and shared state
- [Nexus.Workflows.Dsl](api/nexus-workflows-dsl.md) — Workflow loader, validator, serializer
- [Nexus.Protocols](api/nexus-protocols.md) — MCP, A2A, AG-UI adapters
- [Nexus.Testing](api/nexus-testing.md) — Test utilities and mocks

### Examples

- [Minimal Agent](examples/minimal.md) — Single agent with tools and guardrails
- [Multi-Agent Graph](examples/multi-agent.md) — Graph orchestration with checkpointing
- [Nexus CLI](examples/nexus-cli.md) — GitHub Copilot multi-chat agent
