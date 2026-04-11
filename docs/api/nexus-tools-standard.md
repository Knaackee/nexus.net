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

### ask_user Prompt Policy

When `ask_user` is included in an agent's `ToolNames`, the framework automatically appends a compact usage policy to the agent's system prompt. The policy instructs the agent to:

1. Ask before acting when user intent has ≥ 2 plausible interpretations (prefer `type=select` or `type=confirm`).
2. Always confirm before destructive, irreversible, or costly actions unless a permission gate already blocked them.
3. Limit unverified assumptions to 1 per turn; ask for clarification if more are needed.
4. Never silently override stated user preferences.
5. Keep questions short and decision-oriented; use `confirm`/`select` over `freeText` when choices are enumerable.

The policy is **not** injected when `ask_user` is absent from `ToolNames` — it does not pollute system prompts of agents that have no interaction channel. The text is minimal to preserve space in user-owned system prompts.

```csharp
// Policy injected automatically:
new AgentDefinition
{
    Name = "assistant",
    ToolNames = ["ask_user"],           // policy appended to SystemPrompt
    SystemPrompt = "You are helpful.",  // preserved as-is, policy appended below
}

// No injection:
new AgentDefinition
{
    Name = "assistant",
    ToolNames = [],                     // no ask_user → no policy
}
```

## When To Use It

- local coding agents need a practical default tool surface
- sub-agent delegation should be exposed as a tool
- hosts want bounded, configurable access to workspace and shell capabilities

## Related Docs

- [Tools And Sub-Agents](../llms/tools-and-subagents.md)
- [Single Agent With Tools](../recipes/single-agent-with-tools.md)