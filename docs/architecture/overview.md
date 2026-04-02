# Architecture Overview

Nexus is built as a layered, composable framework. Each layer depends only on layers below it, and every component is registered through dependency injection.

## Layer Diagram

```
┌──────────────────────────────────────────────────────────┐
│                 5. Hosting & Protocols                    │
│    Nexus.Hosting.AspNetCore  ·  MCP  ·  A2A  ·  AG-UI   │
├──────────────────────────────────────────────────────────┤
│                  4. Workflows DSL                        │
│       JSON/YAML pipeline definitions and validation      │
├──────────────────────────────────────────────────────────┤
│              3. Orchestration & Checkpointing            │
│    Graph · Sequence · Parallel · Hierarchical · Resume   │
├──────────────────────────────────────────────────────────┤
│                  2. Cross-Cutting Services                │
│   Guardrails · Memory · Messaging · Telemetry · Auth     │
├──────────────────────────────────────────────────────────┤
│                      1. Core                             │
│    Agents · Tools · Pipeline · Events · Contracts · DI   │
└──────────────────────────────────────────────────────────┘
```

### Layer 1: Core (`Nexus.Core`)

The foundation. Defines all abstractions:

- **Agents** — `IAgent`, `AgentDefinition`, `AgentResult`, `AgentState`, `AgentId`, `AgentTask`
- **Tools** — `ITool`, `IToolRegistry`, `LambdaTool`, `ToolResult`, `ToolAnnotations`
- **Pipeline** — `IAgentMiddleware`, `IToolMiddleware` for composable request/response interception
- **Events** — `AgentEvent` hierarchy for streaming (`TextChunkEvent`, `ReasoningChunkEvent`, `ToolCallStartedEvent`, `ApprovalRequestedEvent`, `UserInputRequestedEvent`, `AgentCompletedEvent`, etc.)
- **Contracts** — Forward-declared interfaces (`IConversationStore`, `IWorkingMemory`, `IMessageBus`) that other packages implement
- **Configuration** — `NexusBuilder` with fluent sub-builders, `AddNexus` extension method

### Layer 2: Cross-Cutting Services

Independent services that agents and orchestration consume:

- **Guardrails** (`Nexus.Guardrails`) — Input/output validation, PII redaction, prompt injection detection
- **Memory** (`Nexus.Memory`) — Conversation history, working memory, context window trimming
- **Messaging** (`Nexus.Messaging`) — Inter-agent pub/sub, request/response, broadcast, dead letter queue
- **Telemetry** (`Nexus.Telemetry`) — OpenTelemetry `ActivitySource` and `Meter` with pre-defined counters and histograms
- **Auth** (`Nexus.Auth.OAuth2`) — `IAuthStrategy` with API key, OAuth2, and token caching

### Layer 3: Orchestration

Coordinates multi-agent execution:

- **Orchestrator** (`IOrchestrator`) — Four execution modes: graph, sequence, parallel, hierarchical
- **Agent Pool** (`IAgentPool`) — Spawn, pause, resume, kill agents with lifecycle events
- **Task Graphs** (`ITaskGraph`) — DAGs with conditional edges and context propagation
- **Checkpointing** (`ICheckpointStore`) — Snapshot and resume long-running orchestrations

### Layer 4: Workflows DSL

Declarative pipeline definitions:

- **`WorkflowDefinition`** — Record-based model serializable to JSON/YAML
- **`IWorkflowLoader`** / **`IWorkflowSerializer`** — Load from files, streams, or strings
- **`IWorkflowValidator`** — Cycle detection, referential integrity, budget validation
- **`IConditionEvaluator`** — Edge condition expressions
- **`IAgentTypeRegistry`** — Pluggable agent type factories

### Layer 5: Hosting & Protocols

External connectivity:

- **MCP** (`Nexus.Protocols.Mcp`) — Tool discovery and execution via Model Context Protocol
- **A2A** (`Nexus.Protocols.A2A`) — Agent-to-Agent JSON-RPC client
- **AG-UI** (`Nexus.Protocols.AgUi`) — Server-Sent Events bridge for frontend streaming
- **ASP.NET Core** (`Nexus.Hosting.AspNetCore`) — Endpoint routing, health checks, SSE middleware

## Dependency Graph

```
Nexus.Hosting.AspNetCore
  ├── Nexus.Protocols.AgUi → Nexus.Core
  ├── Nexus.Protocols.A2A  → Nexus.Core
  └── Nexus.Protocols.Mcp  → Nexus.Core

Nexus.Workflows.Dsl → Nexus.Core

Nexus.Orchestration.Checkpointing → Nexus.Orchestration → Nexus.Core
Nexus.Memory      → Nexus.Core
Nexus.Messaging   → Nexus.Core
Nexus.Guardrails  → Nexus.Core
Nexus.Telemetry   → Nexus.Core
Nexus.Auth.OAuth2 → Nexus.Core
Nexus.Testing     → Nexus.Core
```

## Design Principles

1. **Abstractions in Core** — All interfaces live in `Nexus.Core`. Implementations are in separate packages. You only deploy what you use.

2. **Builder Pattern** — `NexusBuilder` with typed sub-builders (`OrchestrationBuilder`, `MemoryBuilder`, etc.) for discoverable, composable configuration.

3. **Streaming-First** — Every execution method has both `Task<T>` and `IAsyncEnumerable<T>` variants. Events flow through the pipeline in real-time.

4. **Pipeline Middleware** — Agent and tool execution are wrapped in composable middleware chains, similar to ASP.NET Core middleware.

5. **Protocol-Agnostic** — Core agents don't know about MCP, A2A, or AG-UI. Protocol adapters translate between Nexus events and external wire formats.

6. **Microsoft.Extensions.AI** — Built on the standard `IChatClient` abstraction. Any LLM provider that implements it works with Nexus.
