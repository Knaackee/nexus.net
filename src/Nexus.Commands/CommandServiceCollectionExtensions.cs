using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Configuration;

namespace Nexus.Commands;

public enum CommandType
{
    Action,
    Prompt,
}

public enum CommandSource
{
    Builtin,
    User,
    Project,
    Plugin,
    Custom,
}

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

public sealed record CommandInvocation
{
    public required string RawInput { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string ArgumentText { get; init; } = string.Empty;
}

public readonly record struct CommandResult(
    bool ContinueProcessing,
    string? PromptToSend = null,
    string? Output = null)
{
    public static CommandResult Continue(string? promptToSend = null, string? output = null)
        => new(true, promptToSend, output);

    public static CommandResult Stop(string? output = null)
        => new(false, null, output);
}

public sealed record CommandDispatchResult
{
    public bool WasCommand { get; init; }
    public bool WasHandled { get; init; }
    public bool ContinueProcessing { get; init; } = true;
    public string? UnknownCommandName { get; init; }
    public string? PromptToSend { get; init; }
    public string? Output { get; init; }

    public static CommandDispatchResult NotACommand() => new() { WasCommand = false, ContinueProcessing = true };

    public static CommandDispatchResult Unknown(string name) => new()
    {
        WasCommand = true,
        WasHandled = false,
        ContinueProcessing = true,
        UnknownCommandName = name,
    };

    public static CommandDispatchResult Handled(CommandResult result) => new()
    {
        WasCommand = true,
        WasHandled = true,
        ContinueProcessing = result.ContinueProcessing,
        PromptToSend = result.PromptToSend,
        Output = result.Output,
    };
}

public interface ICommandCatalog
{
    ICommand? Resolve(string name);
    IReadOnlyList<ICommand> ListAll();
}

internal sealed class CommandRegistry : ICommandCatalog
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        _commands[command.Name] = command;
        foreach (var alias in command.Aliases)
            _commands[alias] = command;
    }

    public ICommand? Resolve(string name)
        => _commands.GetValueOrDefault(name);

    public IReadOnlyList<ICommand> ListAll()
        => _commands.Values
            .Distinct()
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

internal sealed class SlashCommandDispatcher
{
    private readonly ICommandCatalog _commands;
    private readonly string _prefix;

    public SlashCommandDispatcher(ICommandCatalog commands, string prefix = "/")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _prefix = prefix;
    }

    public async Task<CommandDispatchResult> DispatchAsync(string input, CancellationToken ct = default)
    {
        if (!TryParse(input, out var invocation))
            return CommandDispatchResult.NotACommand();

        var command = _commands.Resolve(invocation.Name);
        if (command is null)
            return CommandDispatchResult.Unknown(invocation.Name);

        var result = await command.ExecuteAsync(invocation, ct).ConfigureAwait(false);
        return CommandDispatchResult.Handled(result);
    }

    public bool TryParse(string? input, out CommandInvocation invocation)
    {
        invocation = null!;
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith(_prefix, StringComparison.Ordinal))
            return false;

        var trimmed = input[_prefix.Length..].Trim();
        if (trimmed.Length == 0)
            return false;

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        invocation = new CommandInvocation
        {
            RawInput = input,
            Name = parts[0],
            Arguments = parts.Skip(1).ToArray(),
            ArgumentText = parts.Length > 1
                ? string.Join(' ', parts.Skip(1))
                : string.Empty,
        };

        return true;
    }
}

public sealed class CommandOptions
{
    public string Prefix { get; set; } = "/";
    public bool IncludeDefaultBuiltins { get; set; } = true;
    public CommandHelpOptions Help { get; } = new();
    internal IList<ICommand> Commands { get; } = [];
    internal IList<CommandDirectoryRegistration> Directories { get; } = [];
}

public sealed record CommandDirectoryRegistration(string Path, CommandSource Source, bool Optional = true);

public interface ICommandLoader
{
    IReadOnlyList<ICommand> LoadFromDirectory(string path, CommandSource source = CommandSource.Custom, bool optional = true);
}

public static class CommandServiceCollectionExtensions
{
    public static CommandBuilder Configure(this CommandBuilder builder, Action<CommandOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = GetOrCreateOptions(builder.Services);
        configure(options);
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    public static CommandBuilder AddCommand<TCommand>(this CommandBuilder builder)
        where TCommand : class, ICommand
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = GetOrCreateOptions(builder.Services);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommand, TCommand>());
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    public static CommandBuilder AddCommand(this CommandBuilder builder, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(command);

        var options = GetOrCreateOptions(builder.Services);
        options.Commands.Add(command);
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    public static CommandBuilder UseDefaults(this CommandBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = GetOrCreateOptions(builder.Services);
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    public static CommandBuilder AddDirectory(
        this CommandBuilder builder,
        string path,
        CommandSource source = CommandSource.Custom,
        bool optional = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var options = GetOrCreateOptions(builder.Services);
        options.Directories.Add(new CommandDirectoryRegistration(path, source, optional));
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    private static CommandOptions GetOrCreateOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(CommandOptions))?.ImplementationInstance as CommandOptions;
        if (existing is not null)
            return existing;

        var created = new CommandOptions();
        services.AddSingleton(created);
        return created;
    }

    private static void EnsureRegistered(IServiceCollection services, CommandOptions options)
    {
        services.TryAddSingleton<ICommandLoader, MarkdownCommandLoader>();

        services.TryAddSingleton<ICommandCatalog>(sp =>
        {
            var registry = new CommandRegistry();
            var loader = sp.GetRequiredService<ICommandLoader>();

            foreach (var directory in options.Directories)
            {
                foreach (var command in loader.LoadFromDirectory(directory.Path, directory.Source, directory.Optional))
                    registry.Register(command);
            }

            foreach (var command in options.Commands)
                registry.Register(command);

            foreach (var command in sp.GetServices<ICommand>())
                registry.Register(command);

            if (options.IncludeDefaultBuiltins)
            {
                foreach (var command in BuiltinCommands.CreateDefaults(() => registry.ListAll(), options.Help))
                    registry.Register(command);
            }

            return registry;
        });

        services.TryAddSingleton(sp => new SlashCommandDispatcher(
            sp.GetRequiredService<ICommandCatalog>(),
            sp.GetRequiredService<CommandOptions>().Prefix));
    }
}