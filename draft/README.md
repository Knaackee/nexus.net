# Nexus Documentation

> **Multi-Agent Orchestration Engine for .NET**  
> Version 0.4 — März 2026  
> Paradigma: Pluggable Engine — Code-first, DSL-optional, zero opinions.

## Dokument-Index

| # | Dokument | Beschreibung |
|---|---------|-------------|
| 00 | [Architektur & Gesamtspec](00-overview/architecture.md) | Schichten, Assembly-Map, Gesamtspec, Nicht-Ziele |
| 01 | [Core Engine](01-core/core-engine.md) | IAgent, ITool, Events, Pipeline, Auth, Contracts |
| 02 | [Orchestration](02-orchestration/orchestration.md) | AgentPool, TaskGraph, Scheduler, ErrorPolicies, Routers |
| 03 | [Streaming](03-streaming/streaming.md) | Dual API, Event-Hierarchie, Fan-In, Middleware |
| 04 | [Protocols](04-protocols/protocols-overview.md) | MCP, A2A, AG-UI |
| 05 | [Guardrails](05-guardrails/guardrails.md) | Security Pipeline, Prompt Injection, PII |
| 06 | [Memory & Context](06-memory/memory.md) | Conversation, Context Window, Long-Term |
| 07 | [Checkpointing](07-checkpointing/checkpointing.md) | State Recovery, Snapshot, Resume |
| 08 | [Messaging](08-messaging/messaging.md) | MessageBus, SharedState, Dead Letter |
| 09 | [Auth & Security](09-auth/auth-security.md) | OAuth 2.1, Device Flow, Secrets |
| 10 | [Testing](10-testing/testing.md) | Mocks, Evaluation, Integration Tests |
| 11 | [Observability](11-observability/observability.md) | Tracing, Metrics, Logging, Audit |
| 12 | [Resilience](12-resilience/resilience.md) | Retry, Circuit Breaker, Fallback, Compensation |
| 13 | [Extensibility](13-extensibility/extensibility.md) | Middleware, Extension Methods, Custom Providers |
| 14 | [Rate Limiting](14-rate-limiting/rate-limiting.md) | Token Bucket, Sliding Window, Per-Provider |
| 15 | [Workflows DSL](15-workflows-dsl/workflows-dsl.md) | Serialisierbare Workflow-Definitionen, JSON/YAML, Visual Builder Support |
| 16 | [Getting Started](16-getting-started/getting-started.md) | Installation, Quickstart, Beispiele |

## Paketstruktur (27 Pakete)

### Foundation
| Paket | Beschreibung |
|-------|-------------|
| `Nexus.Core` | Interfaces, DTOs, Events, Pipeline (Dep: M.E.AI.Abstractions) |

### Capabilities
| Paket | Beschreibung |
|-------|-------------|
| `Nexus.Orchestration` | AgentPool, TaskGraph, Orchestrator, ChatAgent, Router |
| `Nexus.Orchestration.Checkpointing` | ICheckpointStore, Snapshot, Resume |
| `Nexus.Messaging` | MessageBus, SharedState, Dead Letter Queue |
| `Nexus.Guardrails` | Security Pipeline, Built-in Guards |
| `Nexus.Guardrails.ML` | ONNX-basierte Injection/Toxicity Detection |
| `Nexus.Memory` | ConversationStore, ContextWindowManager |
| `Nexus.Auth.OAuth2` | OAuth 2.1 Flows |
| `Nexus.Telemetry` | OpenTelemetry Integration |
| `Nexus.Testing` | Mocks, Fakes, Evaluator |
| `Nexus.Workflows.Dsl` | Serialisierbares Workflow-Format, JSON/YAML Loader |

### Protocols
| Paket | Beschreibung |
|-------|-------------|
| `Nexus.Protocols.Mcp` | MCP Host/Client/Server |
| `Nexus.Protocols.A2A` | A2A Client/Server, Agent Cards |
| `Nexus.Protocols.AgUi` | AG-UI Event Emitter |

### Hosting
| Paket | Beschreibung |
|-------|-------------|
| `Nexus.Hosting.AspNetCore` | Endpoints, Health Checks |

### Infrastructure Adapter
| Paket | Beschreibung |
|-------|-------------|
| `Nexus.Messaging.Redis` | Redis MessageBus + SharedState |
| `Nexus.Memory.Redis` | Redis Memory |
| `Nexus.Memory.Qdrant` | Vector Store |
| `Nexus.Memory.Postgres` | Postgres Memory |
| `Nexus.Checkpointing.Redis` | Redis Checkpoints |
| `Nexus.Checkpointing.Postgres` | Postgres Checkpoints |
| `Nexus.Secrets.AzureKeyVault` | Azure Key Vault |
| `Nexus.Secrets.AwsSecretsManager` | AWS Secrets Manager |
