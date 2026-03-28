# Protocols — MCP, A2A, AG-UI

## 1. Der 2026 Protocol Stack

| Protokoll | Zweck | Spec | .NET SDK |
|-----------|-------|------|----------|
| **MCP** | Agent ↔ externe Tools/Daten | 2025-11-25 | `ModelContextProtocol` v1.0 (Microsoft) |
| **A2A** | Agent ↔ Agent (Cross-Framework) | v0.3 (Google/Linux Foundation) | Nexus-eigene Implementierung |
| **AG-UI** | Agent ↔ Frontend (Real-Time) | Aktuell (CopilotKit) | Nexus-eigene Implementierung |

## 2. MCP — Nexus.Protocols.Mcp

### Nexus als MCP Host (Tools konsumieren)

```csharp
public interface IMcpHostManager
{
    Task<IMcpConnection> ConnectAsync(McpServerConfig config, CancellationToken ct);
    IReadOnlyList<IMcpConnection> Connections { get; }
    Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(CancellationToken ct);
    Task<IReadOnlyList<McpResourceDescriptor>> DiscoverResourcesAsync(CancellationToken ct);
}

public record McpServerConfig
{
    public required string Name { get; init; }
    public required McpTransport Transport { get; init; }
    public McpAuthConfig? Auth { get; init; }
    public ToolFilter? AllowedTools { get; init; }
}

public abstract record McpTransport;
public record StdioTransport(string Command, string[]? Arguments = null) : McpTransport;
public record HttpSseTransport(Uri Endpoint) : McpTransport;
```

### Nexus als MCP Server (Tools exponieren)

```csharp
// ASP.NET:
app.MapMcpEndpoint("/mcp", mcp =>
{
    mcp.ExposeAllTools();
    mcp.ExposeAllResources();
    mcp.ExposeAllPrompts();
});

// Selektiv:
app.MapMcpEndpoint("/mcp/research", mcp =>
{
    mcp.ExposeTool<WebSearchTool>();
    mcp.ExposeTool<SummarizerTool>();
});
```

### MCP Auth (2025-11-25 Spec)

- OAuth 2.1 mit Protected Resource Metadata (PRM) Discovery
- Resource Indicators (RFC 8707) gegen Token-Missbrauch
- Incremental Scope Consent
- URL-Mode Elicitation für sichere User-Interaktion

### MCP Sampling & Elicitation

```csharp
public interface IMcpSamplingHandler
{
    Task<SamplingResponse> HandleSamplingRequestAsync(
        SamplingRequest request,
        IReadOnlyList<McpToolDescriptor>? serverProvidedTools,
        CancellationToken ct);
}

public interface IMcpElicitationHandler
{
    Task<ElicitationResponse> HandleElicitationAsync(ElicitationRequest request, CancellationToken ct);
    Task<ElicitationResponse> HandleUrlElicitationAsync(UrlElicitationRequest request, CancellationToken ct);
}
```

### Attribute für Auto-Discovery

```csharp
[NexusTool("web_search", Description = "Sucht im Internet")]
public class WebSearchTool : ITool { }

[NexusResource("config://app-settings", Description = "App configuration")]
public class AppSettingsResource : IResource { }

[NexusPrompt("summarize", Description = "Summarizes text")]
public class SummarizePrompt : IPrompt { }
```

## 3. A2A — Nexus.Protocols.A2A

### Agent Cards

```csharp
public record NexusAgentCard
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Uri Endpoint { get; init; }
    public required string Version { get; init; }
    public IReadOnlyList<AgentSkill> Skills { get; init; } = [];
    public IReadOnlySet<string> SupportedModalities { get; init; } = new HashSet<string> { "text" };
    public A2AAuthRequirements? Auth { get; init; }
    public string? Signature { get; init; }  // A2A v0.3 Signed Cards
}
```

### A2A Client

```csharp
public interface IA2AClient
{
    Task<NexusAgentCard> DiscoverAsync(Uri agentCardUri, CancellationToken ct);
    Task<A2ATask> SendTaskAsync(Uri endpoint, A2ATaskRequest request, CancellationToken ct);
    IAsyncEnumerable<A2ATaskUpdate> StreamTaskAsync(Uri endpoint, A2ATaskRequest request, CancellationToken ct);
    Task SubscribeToPushAsync(Uri endpoint, string taskId, Uri callbackUri, CancellationToken ct);
}
```

### A2A Server

```csharp
// ASP.NET:
app.MapA2AEndpoint("/a2a", options =>
{
    options.Agent = myAgent;
    options.Card = new NexusAgentCard { ... };
    options.Transport = A2ATransport.JsonRpcOverHttp; // oder GrpcTransport
});
```

## 4. AG-UI — Nexus.Protocols.AgUi

### ~16 Event Types

```csharp
public abstract record AgUiEvent(string EventType, DateTimeOffset Timestamp);

public record AgUiRunStartedEvent() : AgUiEvent("RUN_STARTED", ...);
public record AgUiRunFinishedEvent(string? Error = null) : AgUiEvent("RUN_FINISHED", ...);
public record AgUiTextChunkEvent(string Text) : AgUiEvent("TEXT_CHUNK", ...);
public record AgUiToolCallStartEvent(string ToolCallId, string ToolName, JsonElement Arguments) : AgUiEvent("TOOL_CALL_START", ...);
public record AgUiToolCallEndEvent(string ToolCallId, JsonElement Result) : AgUiEvent("TOOL_CALL_END", ...);
public record AgUiStateDeltaEvent(JsonElement Delta) : AgUiEvent("STATE_DELTA", ...);
public record AgUiStateSnapshotEvent(JsonElement State) : AgUiEvent("STATE_SNAPSHOT", ...);
public record AgUiStepStartedEvent(string StepId, string StepName) : AgUiEvent("STEP_STARTED", ...);
public record AgUiCustomEvent(string Name, JsonElement Data) : AgUiEvent("CUSTOM", ...);
```

### AG-UI Bridge

```csharp
public class AgUiEventBridge
{
    public IAsyncEnumerable<AgUiEvent> BridgeAsync(
        IAsyncEnumerable<OrchestrationEvent> events, CancellationToken ct);
}
```

### ASP.NET Integration

```csharp
app.MapAgUiEndpoint("/agent/stream", options =>
{
    options.Transport = AgUiTransport.ServerSentEvents;  // oder WebSocket
    options.Orchestrator = orchestrator;
    options.SharedStateSchema = typeof(MyAppState);
    options.FrontendTools = [
        new FrontendToolDefinition("navigate_to", "Navigates browser"),
        new FrontendToolDefinition("show_dialog", "Shows confirmation dialog")
    ];
});
```

## 5. Ordnerstruktur

```
Nexus.Protocols.Mcp/
├── IMcpHostManager.cs, McpServerConfig.cs, McpTransport.cs
├── McpToolAdapter.cs, McpResourceAdapter.cs, McpPromptAdapter.cs
├── McpSamplingHandler.cs, McpElicitationHandler.cs
├── Attributes/ (NexusTool, NexusResource, NexusPrompt)
└── Extensions/McpBuilderExtensions.cs

Nexus.Protocols.A2A/
├── IA2AClient.cs, IA2AServer.cs, NexusAgentCard.cs
├── A2ATask.cs, A2AMessagePart.cs
├── Transport/ (HttpJsonRpc, Grpc)
└── Extensions/A2ABuilderExtensions.cs

Nexus.Protocols.AgUi/
├── IAgUiEventEmitter.cs, AgUiEvent.cs (Hierarchie)
├── AgUiEventBridge.cs, AgUiSharedState.cs
├── Transport/ (Sse, WebSocket)
└── Extensions/AgUiBuilderExtensions.cs
```
