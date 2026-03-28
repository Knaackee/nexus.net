# Protocols

Nexus integrates with three external agent protocols: MCP, A2A, and AG-UI.

## Model Context Protocol (MCP)

`Nexus.Protocols.Mcp` adapts MCP tool servers into Nexus `ITool` instances.

### What is MCP?

The [Model Context Protocol](https://modelcontextprotocol.io/) is an open standard for connecting AI models to external tools and data sources. MCP servers expose tools over stdio or HTTP transports.

### Configuration

```csharp
services.AddNexus(nexus =>
{
    nexus.AddMcp(mcp =>
    {
        // MCP servers are configured per-agent via AgentDefinition
    });
});
```

### Agent-Level MCP

Specify MCP servers in the agent definition:

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
            Command = "npx",
            Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"],
        },
        new McpServerConfig
        {
            Name = "remote-api",
            Endpoint = new Uri("https://api.example.com/mcp"),
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
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-github"],
    AllowedTools = new ToolFilter
    {
        Include = ["search_repositories", "get_file_contents"],
        Exclude = ["delete_repository"],
    },
}
```

## Agent-to-Agent Protocol (A2A)

`Nexus.Protocols.A2A` enables communication between agents across different systems via JSON-RPC over HTTP.

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

var result = await a2aClient.SendTaskAsync(new A2ATask
{
    Endpoint = new Uri("https://remote-agent.example.com/a2a"),
    Description = "Analyze this document",
    Input = documentContent,
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
app.MapNexusAgUi("/agent/stream");   // SSE streaming endpoint
app.MapNexusA2A("/agent/a2a");       // A2A JSON-RPC endpoint
app.MapNexusHealth("/health");        // Health check

app.Run();
```

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
