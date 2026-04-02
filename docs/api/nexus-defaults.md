# Nexus.Defaults API Reference

`Nexus.Defaults` is the opinionated composition layer for common Nexus applications.

Use it when you want a productive default stack instead of assembling every subsystem manually.

## What It Wires By Default

`AddDefaults(...)` composes these subsystems:

- configuration
- orchestration
- cost tracking
- commands
- MCP integration
- skills
- permissions with console prompt
- compaction
- in-memory memory
- in-memory sessions
- agent loop
- standard tools with console-backed interaction

## Key Types

### `NexusDefaultsOptions`

Controls the default agent definition, session title, and per-subsystem customization hooks.

### `NexusDefaultsBuilderExtensions.AddDefaults(...)`

The main composition entry point.

### `NexusDefaultHost`

Convenience host for running the default stack through `IAgentLoop`.

### `Nexus.CreateDefault(...)`

Global shortcut for constructing a `NexusDefaultHost` from an `IChatClient` or factory.

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.UseChatClient(sp => chatClient);
    builder.AddDefaults(options =>
    {
        options.DefaultAgentDefinition = new AgentDefinition
        {
            Name = "DefaultAgent",
            SystemPrompt = "You are a helpful assistant. Use tools when useful.",
        };
    });
});
```

Shortcut form:

```csharp
await using var host = Nexus.Nexus.CreateDefault(chatClient);
await foreach (var evt in host.RunAsync("Summarize the repo"))
{
}
```

## When To Use It

- prototypes should become productive quickly
- a CLI or local assistant wants the standard Nexus stack
- you need one place to customize multiple subsystems coherently

## When Not To Use It

- you need tight control over every registration
- your host is intentionally minimal or highly specialized

## Related Docs

- [Quick Start](../guides/quick-start.md)
- [Nexus CLI](../examples/nexus-cli.md)