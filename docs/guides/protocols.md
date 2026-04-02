# Protocols

Nexus integrates with three external agent protocols: MCP, A2A, and AG-UI.

## Use This Guide When

Use this guide to choose the right protocol boundary.

If you already know the package you need, go straight to the package page:

- [Nexus.Protocols.Mcp](../api/nexus-protocols-mcp.md)
- [Nexus.Protocols.A2A](../api/nexus-protocols-a2a.md)
- [Nexus.Protocols.AgUi](../api/nexus-protocols-agui.md)
- [Nexus.Hosting.AspNetCore](../api/nexus-hosting-aspnetcore.md)

## Quick Choice

| Need | Start with |
|---|---|
| Bring external MCP tools into Nexus | `Nexus.Protocols.Mcp` |
| Call or expose remote agents over HTTP | `Nexus.Protocols.A2A` |
| Stream runtime progress into a frontend | `Nexus.Protocols.AgUi` |
| Host protocol endpoints in ASP.NET Core | `Nexus.Hosting.AspNetCore` |

## Model Context Protocol (MCP)

`Nexus.Protocols.Mcp` adapts MCP tool servers into Nexus tools and AI functions so they can be registered in the normal runtime tool registry.

### What is MCP?

The [Model Context Protocol](https://modelcontextprotocol.io/) is an open standard for connecting AI models to external tools and data sources. MCP servers expose tools over stdio or HTTP transports.

### Configuration

```csharp
services.AddNexus(nexus =>
{
    nexus.AddMcp(mcp =>
    {
        mcp.UseDefaults();
        mcp.AddServer("filesystem", new StdioTransport(
            "npx",
            ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"]));
    });
});
```

`Nexus.Defaults` now calls `AddMcp(...).UseDefaults()` automatically, so hosts such as `Nexus.Cli` only need to provide server configuration.

### CLI Configuration

`Nexus.Cli` loads MCP servers automatically from:

- `.nexus/mcp.json` in the current project
- `~/.nexus/mcp.json` in the user profile

Project-local config overrides user config when both define the same server name.

```json
{
    "servers": {
        "filesystem": {
            "command": "npx",
            "args": ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"],
            "workingDirectory": ".",
            "allowedTools": {
                "include": ["read_file", "write_file"]
            }
        },
        "docs": {
            "endpoint": "https://example.com/mcp"
        }
    }
}
```

### Agent-Level MCP

Specify MCP servers in the agent definition when you want agent-owned MCP metadata in addition to builder-level registration:

```csharp
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "FileAgent",
    SystemPrompt = "You can read and write files.",
    McpServers =
    [
        new McpServerConfig
        {
            Name = "filesystem",
            Transport = new StdioTransport(
                "npx",
                ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"]),
        },
        new McpServerConfig
        {
            Name = "remote-api",
            Transport = new HttpSseTransport(new Uri("https://api.example.com/mcp")),
            AllowedTools = new ToolFilter { Include = ["search", "read"] },
        },
    ],
});
```

### Tool Filtering

Control which tools an agent can access from an MCP server:

```csharp
new McpServerConfig
{
    Name = "github",
    Transport = new StdioTransport(
        "npx",
        ["-y", "@modelcontextprotocol/server-github"]),
    AllowedTools = new ToolFilter
    {
        Include = ["search_repositories", "get_file_contents"],
        Exclude = ["delete_repository"],
    },
}
```

## Agent-to-Agent Protocol (A2A)

`Nexus.Protocols.A2A` enables communication between agents across different systems via JSON-RPC over HTTP.

Use it when the other side is another agent service, not just a tool server.

### Configuration

```csharp
services.AddNexus(nexus =>
{
    nexus.AddA2A(a2a =>
    {
        // Configure A2A endpoints, auth, etc.
    });
});
```

### Sending Tasks

```csharp
var a2aClient = sp.GetRequiredService<IA2AClient>();

var result = await a2aClient.SendTaskAsync(
    new Uri("https://remote-agent.example.com/a2a"),
    new A2ATaskRequest
{
        Id = Guid.NewGuid().ToString("N"),
        SessionId = Guid.NewGuid().ToString("N"),
        Messages =
        [
            new A2AMessage
            {
                Role = "user",
                Parts = [new A2ATextPart("Analyze this document")],
            }
        ],
    });
```

## AG-UI (Agent-User Interface)

`Nexus.Protocols.AgUi` bridges agent events to frontend applications via Server-Sent Events (SSE).

### Configuration

```csharp
services.AddNexus(nexus =>
{
    nexus.AddAgUi(agui =>
    {
        // Configure SSE options, event filtering
    });
});
```

### ASP.NET Core Hosting

The `Nexus.Hosting.AspNetCore` package maps AG-UI endpoints:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => myClient);
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddAgUi();
    nexus.AddA2A();
});

var app = builder.Build();

// Map protocol endpoints
app.MapAgUiEndpoint("/agent/stream"); // SSE streaming endpoint
app.MapA2AEndpoint("/a2a");           // A2A JSON-RPC endpoint
app.MapHealthChecks("/health");       // Health check

app.Run();
```

If you want the standard route set in one call, use `app.MapNexusEndpoints()`.

### Frontend Integration

Connect from a web frontend:

```javascript
const evtSource = new EventSource('/agent/stream?task=Analyze+data');

evtSource.addEventListener('text-chunk', (e) => {
    appendToChat(JSON.parse(e.data).text);
});

evtSource.addEventListener('tool-call', (e) => {
    showToolIndicator(JSON.parse(e.data).toolName);
});

evtSource.addEventListener('agent-completed', (e) => {
    evtSource.close();
});
```

## Package Dependencies

```
Nexus.Hosting.AspNetCore
  ├── Nexus.Protocols.AgUi → Nexus.Core
  ├── Nexus.Protocols.A2A  → Nexus.Core
  └── Nexus.Protocols.Mcp  → Nexus.Core
```

All three protocol packages depend only on `Nexus.Core`. The hosting package wires them into ASP.NET Core.

## Read Next

- local tool integration: [Tools And Sub-Agents](../llms/tools-and-subagents.md)
- HTTP hosting details: [Nexus.Hosting.AspNetCore](../api/nexus-hosting-aspnetcore.md)
- protocol package detail: [Package Index](../api/README.md)
