# Nexus.Protocols.Mcp API Reference

`Nexus.Protocols.Mcp` integrates Model Context Protocol servers into the Nexus tool system.

Use it when external tools or resources already exist behind MCP and should become normal Nexus tools instead of being wrapped manually one by one.

## Key Types

### `McpBuilderExtensions`

Main builder surface:

- `UseDefaults()`
- `Configure(...)`
- `AddServer(string name, McpTransport transport)`
- `AddServer(McpServerConfig config)`

### `McpOptions`

Collected builder options containing the configured server list.

### `McpServerConfig`

```csharp
public record McpServerConfig
{
    public required string Name { get; init; }
    public required McpTransport Transport { get; init; }
    public ToolFilter? AllowedTools { get; init; }
    public TimeSpan ConnectionTimeout { get; init; }
}
```

### `McpTransport`

Transport abstraction with built-in shapes:

- `StdioTransport`
- `HttpSseTransport`

### `IMcpHostManager`

Host-side manager for connecting to configured MCP servers and exposing discovered capabilities.

### Discovery Descriptors

- `McpToolDescriptor`
- `McpResourceDescriptor`
- `McpPromptDescriptor`

## Registration

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

## When To Use It

- tools already exist behind MCP servers
- the same external tool surface should be shared across agents or hosts
- tool discovery should remain declarative and provider-agnostic

## Related Docs

- [Protocols Guide](../guides/protocols.md)
- [Nexus.Protocols Overview](nexus-protocols.md)