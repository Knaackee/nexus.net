# Nexus.Tools.Standard API Reference

`Nexus.Tools.Standard` is the standard tool bundle for filesystem, search, shell, web, user interaction, and delegated child-agent execution.

Use it when you want an agent to operate on a local workspace or host environment without building a tool surface from scratch.

## Tool Categories

- `FileSystem`
- `Search`
- `Shell`
- `Web`
- `Interaction`
- `Agents`

## Main Types

### `StandardToolBuilder`

Selects categories and configures shared tool options.

Important methods:

- `Configure(...)`
- `FileSystem()`
- `Search()`
- `Shell()`
- `Web()`
- `Interaction()`
- `Agents()`
- `Only(...)`
- `UseConsoleInteraction()`

### `StandardToolOptions`

Shared limits and execution settings.

Important fields:

- `BaseDirectory`
- `WorkingDirectory`
- `MaxReadLines`
- `MaxSearchResults`
- `MaxFetchCharacters`
- `HttpTimeout`
- `ShellTimeout`

### Built-in Tools

- `FileReadTool`
- `FileWriteTool`
- `FileEditTool`
- `GlobTool`
- `GrepTool`
- `ShellTool`
- `WebFetchTool`
- `AskUserTool`
- `AgentTool`

### Result Records

- `ShellCommandResult`
- `GrepMatch`
- `WebFetchResult`
- `AgentToolResult`
- `AgentBatchToolResult`
- `AgentToolInvocationResult`
- `EditResult`
- `FileWriteResult`

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddStandardTools(tools =>
    {
        tools.Only(StandardToolCategory.FileSystem, StandardToolCategory.Search, StandardToolCategory.Agents);
        tools.UseConsoleInteraction();
    });
});
```

## Important Behavior

This package also ensures that the `IToolRegistry` is built from the `ITool` implementations registered in DI. That makes DI-registered tools discoverable without a separate manual registry step.

## When To Use It

- local coding agents need a practical default tool surface
- sub-agent delegation should be exposed as a tool
- hosts want bounded, configurable access to workspace and shell capabilities

## Related Docs

- [Tools And Sub-Agents](../llms/tools-and-subagents.md)
- [Single Agent With Tools](../recipes/single-agent-with-tools.md)