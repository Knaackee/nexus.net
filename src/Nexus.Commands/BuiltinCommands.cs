namespace Nexus.Commands;

public sealed class CommandHelpOptions
{
    public bool IncludeMessageHint { get; set; } = true;
    public string MessageHint { get; set; } = "<message>  Send a message to the active agent";
    public IList<string> FooterLines { get; } = [];

    internal CommandHelpOptions Clone()
    {
        var clone = new CommandHelpOptions
        {
            IncludeMessageHint = IncludeMessageHint,
            MessageHint = MessageHint,
        };

        foreach (var line in FooterLines)
            clone.FooterLines.Add(line);

        return clone;
    }
}

public static class BuiltinCommands
{
    public static IReadOnlyList<ICommand> CreateDefaults(Func<IReadOnlyList<ICommand>> listCommands, CommandHelpOptions? helpOptions = null)
    {
        ArgumentNullException.ThrowIfNull(listCommands);

        return
        [
            CreateHelp(listCommands, helpOptions),
            CreateQuit(),
        ];
    }

    public static ICommand CreateHelp(Func<IReadOnlyList<ICommand>> listCommands, CommandHelpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(listCommands);
        return new HelpCommand(listCommands, options?.Clone() ?? new CommandHelpOptions());
    }

    public static ICommand CreateQuit(string description = "Exit command processing.", string usage = "/quit")
        => new QuitCommand(description, usage);

    public static ICommand CreateStatus(Func<CommandInvocation, CommandResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateStatus((invocation, _) => Task.FromResult(handler(invocation)));
    }

    public static ICommand CreateStatus(Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler)
        => CreateBuiltinActionCommand("status", "Show current session or host status.", "/status", handler);

    public static ICommand CreateResume(Func<CommandInvocation, CommandResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateResume((invocation, _) => Task.FromResult(handler(invocation)));
    }

    public static ICommand CreateResume(Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler)
        => CreateBuiltinActionCommand("resume", "Resume the latest persisted session.", "/resume [key]", handler);

    public static ICommand CreateCost(Func<CommandInvocation, CommandResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateCost((invocation, _) => Task.FromResult(handler(invocation)));
    }

    public static ICommand CreateCost(Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler)
        => CreateBuiltinActionCommand("cost", "Show token and cost information.", "/cost", handler);

    public static ICommand CreateClear(Func<CommandInvocation, CommandResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateClear((invocation, _) => Task.FromResult(handler(invocation)));
    }

    public static ICommand CreateClear(Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler)
        => CreateBuiltinActionCommand("clear", "Clear the active session conversation.", "/clear", handler);

    public static ICommand CreateModel(Func<CommandInvocation, CommandResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateModel((invocation, _) => Task.FromResult(handler(invocation)));
    }

    public static ICommand CreateModel(Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler)
        => CreateBuiltinActionCommand("model", "Show or change the active model.", "/model [name]", handler);

    public static ICommand CreateCompact(Func<CommandInvocation, CommandResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return CreateCompact((invocation, _) => Task.FromResult(handler(invocation)));
    }

    public static ICommand CreateCompact(Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler)
        => CreateBuiltinActionCommand("compact", "Compact the active session history.", "/compact", handler);

    private static DelegateCommand CreateBuiltinActionCommand(
        string name,
        string description,
        string usage,
        Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler,
        IReadOnlyList<string>? aliases = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(usage);
        ArgumentNullException.ThrowIfNull(handler);

        return new DelegateCommand(name, description, usage, handler, aliases, CommandSource.Builtin);
    }

    private sealed class HelpCommand : ICommand
    {
        private readonly Func<IReadOnlyList<ICommand>> _listCommands;
        private readonly CommandHelpOptions _options;

        public HelpCommand(Func<IReadOnlyList<ICommand>> listCommands, CommandHelpOptions options)
        {
            _listCommands = listCommands;
            _options = options;
        }

        public string Name => "help";
        public string Description => "Show available commands.";
        public string Usage => "/help";
        public IReadOnlyList<string> Aliases => ["?"];
        public CommandSource Source => CommandSource.Builtin;

        public Task<CommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken ct = default)
        {
            var lines = new List<string> { "Available commands:" };
            foreach (var command in _listCommands())
                lines.Add($"  {command.Usage,-18} {command.Description}");

            if (_options.IncludeMessageHint && !string.IsNullOrWhiteSpace(_options.MessageHint))
                lines.Add($"  {_options.MessageHint}");

            if (_options.FooterLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(_options.FooterLines);
            }

            return Task.FromResult(CommandResult.Continue(output: string.Join(Environment.NewLine, lines)));
        }
    }

    private sealed class QuitCommand(string description, string usage) : ICommand
    {
        public string Name => "quit";
        public string Description => description;
        public string Usage => usage;
        public IReadOnlyList<string> Aliases => ["exit", "q"];
        public CommandSource Source => CommandSource.Builtin;

        public Task<CommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken ct = default)
            => Task.FromResult(CommandResult.Stop());
    }
}