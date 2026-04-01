namespace Nexus.Commands;

public sealed class DelegateCommand : ICommand
{
    private readonly Func<CommandInvocation, CancellationToken, Task<CommandResult>> _handler;

    public DelegateCommand(
        string name,
        string description,
        string usage,
        Func<CommandInvocation, CommandResult> handler,
        IReadOnlyList<string>? aliases = null,
        CommandSource source = CommandSource.Custom)
        : this(name, description, usage, (invocation, _) => Task.FromResult(handler(invocation)), aliases, source)
    {
    }

    public DelegateCommand(
        string name,
        string description,
        string usage,
        Func<CommandInvocation, CancellationToken, Task<CommandResult>> handler,
        IReadOnlyList<string>? aliases = null,
        CommandSource source = CommandSource.Custom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(usage);
        ArgumentNullException.ThrowIfNull(handler);

        Name = name;
        Description = description;
        Usage = usage;
        _handler = handler;
        Aliases = aliases ?? [];
        Source = source;
    }

    public string Name { get; }
    public string Description { get; }
    public string Usage { get; }
    public IReadOnlyList<string> Aliases { get; }
    public CommandSource Source { get; }

    public Task<CommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken ct = default)
        => _handler(invocation, ct);
}