# Installation

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- An LLM provider that implements `Microsoft.Extensions.AI.IChatClient`

## NuGet Packages

Install the packages you need. At minimum, you need **Nexus.Core**:

```bash
dotnet add package Nexus.Core
```

### Full Package List

| Package | Purpose |
|---------|---------|
| `Nexus.Core` | Core abstractions — agents, tools, pipeline, events, DI |
| `Nexus.Orchestration` | Graph, sequence, parallel, hierarchical execution |
| `Nexus.Orchestration.Checkpointing` | Snapshot serialization and in-memory store |
| `Nexus.Memory` | Conversation history and working memory |
| `Nexus.Messaging` | Inter-agent pub/sub, shared state, dead letter queue |
| `Nexus.Guardrails` | PII redaction, prompt injection, secrets detection |
| `Nexus.Telemetry` | OpenTelemetry traces and metrics |
| `Nexus.Auth.OAuth2` | API key auth, OAuth2 client credentials, token cache |
| `Nexus.Protocols.Mcp` | Model Context Protocol tool adapter |
| `Nexus.Protocols.A2A` | Agent-to-Agent protocol client (JSON-RPC) |
| `Nexus.Protocols.AgUi` | AG-UI event bridge for frontend streaming |
| `Nexus.Workflows.Dsl` | JSON/YAML workflow definitions |
| `Nexus.Hosting.AspNetCore` | ASP.NET Core endpoints (A2A, AG-UI SSE, health) |
| `Nexus.Testing` | Mock agents, fake clients, event recording |

### Common Combinations

**Minimal agent with tools:**

```bash
dotnet add package Nexus.Core
dotnet add package Nexus.Orchestration
dotnet add package Nexus.Memory
```

**Full orchestration with guardrails and telemetry:**

```bash
dotnet add package Nexus.Core
dotnet add package Nexus.Orchestration
dotnet add package Nexus.Orchestration.Checkpointing
dotnet add package Nexus.Memory
dotnet add package Nexus.Messaging
dotnet add package Nexus.Guardrails
dotnet add package Nexus.Telemetry
```

**Web API hosting with protocols:**

```bash
dotnet add package Nexus.Hosting.AspNetCore
dotnet add package Nexus.Protocols.Mcp
dotnet add package Nexus.Protocols.A2A
dotnet add package Nexus.Protocols.AgUi
```

## Project Setup

Create a new console application:

```bash
dotnet new console -n MyAgentApp
cd MyAgentApp
dotnet add package Nexus.Core
dotnet add package Nexus.Orchestration
dotnet add package Nexus.Memory
```

Your `.csproj` should target .NET 10:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Nexus.Core" Version="0.1.0" />
    <PackageReference Include="Nexus.Orchestration" Version="0.1.0" />
    <PackageReference Include="Nexus.Memory" Version="0.1.0" />
  </ItemGroup>
</Project>
```

## Next Steps

- [Quick Start](quickstart.md) — Build and run your first agent
- [Architecture Overview](../architecture/overview.md) — Understand the layered design
