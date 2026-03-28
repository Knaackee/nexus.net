# Nexus.Protocols API Reference

## Nexus.Protocols.Mcp

Model Context Protocol integration. Adapts MCP tool servers into Nexus `ITool` instances.

### McpServerConfig

Configured per-agent via `AgentDefinition.McpServers`:

```csharp
public record McpServerConfig
{
    public required string Name { get; init; }
    public string? Command { get; init; }              // Stdio transport
    public IReadOnlyList<string>? Arguments { get; init; }
    public Uri? Endpoint { get; init; }                // HTTP transport
    public ToolFilter? AllowedTools { get; init; }
}
```

### ToolFilter

```csharp
public record ToolFilter
{
    public IReadOnlyList<string>? Include { get; init; }
    public IReadOnlyList<string>? Exclude { get; init; }
}
```

### Configuration

```csharp
services.AddNexus(nexus => nexus.AddMcp());
```

## Nexus.Protocols.A2A

Agent-to-Agent protocol client using JSON-RPC over HTTP.

### Configuration

```csharp
services.AddNexus(nexus => nexus.AddA2A());
```

## Nexus.Protocols.AgUi

AG-UI event bridge — translates Nexus agent events to Server-Sent Events for frontend streaming.

### Configuration

```csharp
services.AddNexus(nexus => nexus.AddAgUi());
```

## Nexus.Hosting.AspNetCore

ASP.NET Core hosting for protocol endpoints:

```csharp
var app = builder.Build();
app.MapNexusAgUi("/agent/stream");  // AG-UI SSE endpoint
app.MapNexusA2A("/agent/a2a");      // A2A JSON-RPC endpoint
app.MapNexusHealth("/health");       // Health check
```
