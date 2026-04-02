# Nexus.Hosting.AspNetCore API Reference

`Nexus.Hosting.AspNetCore` integrates Nexus with ASP.NET Core endpoints and health checks.

Use it when your runtime should be exposed over HTTP instead of only through an in-process host or CLI.

## Key Endpoints

### `MapAgUiEndpoint(...)`

Maps the AG-UI streaming endpoint and bridges orchestration events to the frontend over server-sent events.

### `MapA2AEndpoint(...)`

Maps the A2A JSON-RPC endpoint for agent-to-agent communication.

### `AddNexusHealthChecks(...)`

Adds a Nexus agent-pool health check.

### `MapNexusEndpoints(...)`

Convenience mapper for the standard endpoint set.

## Typical Use

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks().AddNexusHealthChecks();

var app = builder.Build();
app.MapNexusEndpoints();
```

## Endpoint Defaults

- AG-UI stream: `/agent/stream`
- A2A endpoint: `/a2a`
- health check: `/health`

## Related Packages

- `Nexus.Protocols.AgUi`
- `Nexus.Protocols.A2A`
- `Nexus.Orchestration`

## Related Docs

- [Protocols Guide](../guides/protocols.md)
- [Nexus.Protocols](nexus-protocols.md)