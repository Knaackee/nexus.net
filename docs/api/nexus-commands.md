# Nexus.Commands API Reference

`Nexus.Commands` is the slash-command framework for interactive Nexus hosts.

Use it when a host such as the CLI, chat UI, or terminal shell needs command parsing and dispatch that is separate from normal user prompts.

## Key Types

### `ICommand`

The command contract.

```csharp
public interface ICommand
{
    string Name { get; }
    string Description { get; }
    string Usage => $"/{Name}";
    IReadOnlyList<string> Aliases => [];
    CommandType Type => CommandType.Action;
    CommandSource Source => CommandSource.Custom;

    Task<CommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken ct = default);
}
```

### `CommandInvocation`

Parsed command input including `RawInput`, `Name`, `Arguments`, and `ArgumentText`.

### `CommandResult`

Controls whether normal prompt processing should continue and can optionally return host output or a prompt to send.

### `ICommandCatalog`

Resolution surface used by the dispatcher.

### `SlashCommandDispatcher`

Parses `/`-prefixed input and routes it to the resolved command.

## Built-ins

`BuiltinCommands` provides shared implementations or factories for:

- `/help`
- `/quit`
- `/status`
- `/resume`
- `/cost`
- `/clear`
- `/model`
- `/compact`

## Registration

```csharp
services.AddNexus(builder =>
{
    builder.AddCommands(commands =>
    {
        commands.UseDefaults();
        commands.AddCommand(new DelegateCommand(
            "hello",
            "Print a greeting.",
            "/hello",
            (invocation, ct) => Task.FromResult(CommandResult.Continue(output: "hello"))));
    });
});
```

Directory loading is also supported:

```csharp
commands.AddDirectory(".nexus/commands", CommandSource.Project);
```

## When To Use It

- host-side control commands should not be sent to the model
- commands need shared naming and usage metadata across hosts
- markdown-defined commands should be loadable from disk

## Related Packages

- `Nexus.Skills` for reusable prompt/tool profiles
- `Nexus.Defaults` for opinionated composition

## Related Docs

- [Nexus CLI](../examples/nexus-cli.md)