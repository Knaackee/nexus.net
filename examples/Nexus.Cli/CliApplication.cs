using System.Globalization;
using Nexus.Commands;
using Nexus.Protocols.Mcp;
using Nexus.Skills;
using Spectre.Console;

namespace Nexus.Cli;

internal sealed class CliApplication : IDisposable
{
    private readonly IAnsiConsole _console;
    private readonly CliWorkspaceOptions _workspace;
    private readonly ICliChatProvider _chatProvider;
    private readonly SkillCatalog _skills;
    private readonly IReadOnlyList<McpServerConfig> _mcpServers;
    private readonly ChatManager _manager;
    private readonly CommandRegistry _commands;
    private readonly SlashCommandDispatcher _dispatcher;
    private readonly Func<string?> _lineReader;
    private readonly bool _useInteractivePrompt;
    private bool _initialized;

    public CliApplication(
        IAnsiConsole? console = null,
        CliWorkspaceOptions? workspace = null,
        ICliChatProvider? chatProvider = null,
        SkillCatalog? skills = null,
        IReadOnlyList<McpServerConfig>? mcpServers = null,
        Func<string?>? lineReader = null,
        bool? useInteractivePrompt = null)
    {
        _console = console ?? AnsiConsole.Console;
        _workspace = workspace ?? CliWorkspaceOptions.Create(Directory.GetCurrentDirectory());
        _chatProvider = chatProvider ?? CliChatProviders.CreateFromEnvironment();
        _mcpServers = mcpServers ?? CliMcpConfiguration.Load(_workspace);
        _skills = skills ?? CliSkillCatalog.CreateDefaultCatalog(_workspace);
        _lineReader = lineReader ?? Console.ReadLine;
        _useInteractivePrompt = useInteractivePrompt ?? !Console.IsInputRedirected;
        _manager = new ChatManager(
            _skills,
            projectRoot: _workspace.ProjectRoot,
            sessionStorePath: _workspace.SessionDirectory,
            mcpServers: _mcpServers,
            chatClientFactory: model => _chatProvider.CreateClient(model),
            defaultModel: _chatProvider.DefaultModel,
            defaultModelProvider: () => _chatProvider.DefaultModel);
        _commands = CreateCommands();
        _dispatcher = new SlashCommandDispatcher(_commands);
    }

    internal ChatManager Manager => _manager;

    public static CliApplication CreateDefault() => new();

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _chatProvider.InitializeAsync(ct).ConfigureAwait(false);
        _initialized = true;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        _console.Write(new FigletText("Nexus CLI").Color(Color.Cyan1));
        _console.MarkupLine($"[grey]A multi-chat coding agent powered by {Markup.Escape(_chatProvider.ProviderName)}[/]");
        _console.WriteLine();

        if (!await AuthenticateAsync(ct).ConfigureAwait(false))
            return 1;

        var status = _chatProvider.RequiresAuthentication
            ? $"Authenticated with {_chatProvider.ProviderName}!"
            : $"Connected to {_chatProvider.ProviderName}.";
        _console.MarkupLine($"[green]{Markup.Escape(status)}[/]");
        _console.WriteLine();
        PrintHelp();

        while (!ct.IsCancellationRequested)
        {
            var active = _manager.ActiveSession;
            var prompt = active is not null
                ? $"[cyan]{Markup.Escape(active.Key)}[/] [grey]({Markup.Escape(active.Model)})[/]"
                : "[grey]nexus[/]";

            var input = ReadInput(prompt);
            if (input is null)
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (!await ExecuteInputAsync(input, ct).ConfigureAwait(false))
                break;
        }

        return 0;
    }

    public async Task<bool> ExecuteInputAsync(string input, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        if (input.StartsWith('/'))
            return await HandleCommandAsync(input, ct).ConfigureAwait(false);

        HandleChat(input);
        return true;
    }

    public void Dispose()
    {
        _manager.Dispose();
        _chatProvider.Dispose();
    }

    private async Task<bool> AuthenticateAsync(CancellationToken ct)
    {
        try
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            var label = _chatProvider.RequiresAuthentication ? "Authenticating" : "Connecting";
            _console.MarkupLine($"[grey]{label} with {Markup.Escape(_chatProvider.ProviderName)}...[/]");
            await _chatProvider.AuthenticateAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Startup failed:[/] {Markup.Escape(ex.Message)}");
            return false;
        }
    }

    private async Task<bool> HandleCommandAsync(string input, CancellationToken ct)
    {
        var result = await _dispatcher.DispatchAsync(input, ct).ConfigureAwait(false);
        if (!result.WasHandled)
        {
            if (result.UnknownCommandName is not null)
                _console.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(result.UnknownCommandName)}. Type [cyan]/help[/].");

            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
            _console.MarkupLine(Markup.Escape(result.Output));

        if (!string.IsNullOrWhiteSpace(result.PromptToSend))
            HandleChat(result.PromptToSend);

        return result.ContinueProcessing;
    }

    private string PickModel()
    {
        EnsureInitialized();
        var models = _chatProvider.AvailableModels.Count > 0 ? _chatProvider.AvailableModels : [_chatProvider.DefaultModel];

        if (!_useInteractivePrompt)
            return _chatProvider.DefaultModel;

        return _console.Prompt(new SelectionPrompt<string>().Title("Select a model:").AddChoices(models));
    }

    private void HandleList()
    {
        var sessions = _manager.Sessions;
        if (sessions.Count == 0)
        {
            _console.MarkupLine("[grey]No active chats. Use /new <key> to create one.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Key")
            .AddColumn("Model")
            .AddColumn("Skill")
            .AddColumn("State")
            .AddColumn("Messages");

        foreach (var session in sessions)
        {
            var stateColor = session.State switch
            {
                ChatSessionState.Running => "yellow",
                ChatSessionState.Failed => "red",
                _ => "green",
            };

            var isActive = string.Equals(session.Key, _manager.ActiveKey, StringComparison.OrdinalIgnoreCase);
            var keyText = isActive ? $"[bold cyan]{Markup.Escape(session.Key)}[/] *" : Markup.Escape(session.Key);

            table.AddRow(
                keyText,
                Markup.Escape(session.Model),
                Markup.Escape(session.SkillName),
                $"[{stateColor}]{session.State}[/]",
                session.MessageCount.ToString(CultureInfo.InvariantCulture));
        }

        _console.Write(table);
    }

    private void HandleStatus()
    {
        var sessions = _manager.Sessions;
        if (sessions.Count == 0)
        {
            _console.MarkupLine("[grey]No active chats.[/]");
            return;
        }

        foreach (var session in sessions)
        {
            var stateColor = session.State switch
            {
                ChatSessionState.Running => "yellow",
                ChatSessionState.Failed => "red",
                _ => "green",
            };

            _console.MarkupLine($"[cyan]{Markup.Escape(session.Key)}[/] [{stateColor}]{session.State}[/] [grey]({Markup.Escape(session.Model)}, skill: {Markup.Escape(session.SkillName)})[/]");

            if (session.LastOutput.Length > 0)
            {
                var preview = session.LastOutput.Length > 200
                    ? session.LastOutput[..200] + "..."
                    : session.LastOutput;
                _console.MarkupLine($"  [grey]{Markup.Escape(preview)}[/]");
            }
        }
    }

    private void HandleChat(string input)
    {
        var session = _manager.ActiveSession;
        if (session is null)
        {
            _console.MarkupLine("[yellow]No active chat. Use /new <key> to create one first.[/]");
            return;
        }

        if (session.State == ChatSessionState.Running)
        {
            _console.MarkupLine("[yellow]Chat is still processing. Use /cancel to stop or /status to check.[/]");
            return;
        }

        session.Send(input);
    }

    private void HelloWorldTool()
    {
        _console.Write(
            new Panel("[bold cyan]Hello, World![/]\n\nThis is the Nexus CLI hello_world tool.\nIt demonstrates tool execution within the agent framework.")
                .Header("[yellow]hello_world tool[/]")
                .Border(BoxBorder.Rounded)
                .Padding(1, 0));
    }

    private void PrintHelp()
    {
        var helpCommand = BuiltinCommands.CreateHelp(
            () => _commands.ListAll(),
            new CommandHelpOptions
            {
                MessageHint = "<message>  Send a message to the active chat",
            });

        var help = helpCommand.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/help",
            Name = "help",
        }).GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(help.Output))
            _console.WriteLine(help.Output);

        _console.WriteLine();
        _console.MarkupLine($"[grey]Project root:[/] {Markup.Escape(_workspace.ProjectRoot)}");
        _console.MarkupLine($"[grey]Session store:[/] {Markup.Escape(_workspace.SessionDirectory)}");
        _console.MarkupLine($"[grey]MCP config:[/] {Markup.Escape(_workspace.ProjectMcpConfigPath)} [grey](project),[/] {Markup.Escape(_workspace.UserMcpConfigPath)} [grey](user)[/]");
        if (_mcpServers.Count > 0)
            _console.MarkupLine($"[grey]Loaded MCP servers:[/] {Markup.Escape(string.Join(", ", _mcpServers.Select(server => server.Name)))}");
        _console.WriteLine();
    }

    private void HandleNew(string key, string model, SkillDefinition? skill)
    {
        try
        {
            var session = _manager.Add(key, model, skill);
            AttachSession(session);
            var skillName = skill?.Name ?? session.SkillName;
            _console.MarkupLine($"[green]Created chat[/] [cyan]{Markup.Escape(key)}[/] [grey]({Markup.Escape(model)}, skill: {Markup.Escape(skillName)})[/]");
        }
        catch (InvalidOperationException ex)
        {
            _console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        }
    }

    private void HandleSwitch(string key)
    {
        if (_manager.Switch(key))
            _console.MarkupLine($"[green]Switched to[/] [cyan]{Markup.Escape(key)}[/]");
        else
            _console.MarkupLine($"[red]No chat with key '{Markup.Escape(key)}'[/]");
    }

    private void HandleRemove(string key)
    {
        if (_manager.Remove(key))
            _console.MarkupLine($"[yellow]Removed chat '{Markup.Escape(key)}'[/]");
        else
            _console.MarkupLine($"[red]No chat with key '{Markup.Escape(key)}'[/]");
    }

    private void HandleSkill(string? skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            var activeSkill = _manager.ActiveSession?.SkillName;
            foreach (var skillDefinition in _skills.ListAll())
            {
                var marker = string.Equals(activeSkill, skillDefinition.Name, StringComparison.OrdinalIgnoreCase) ? "[green]*[/] " : string.Empty;
                _console.MarkupLine($"{marker}[cyan]{Markup.Escape(skillDefinition.Name)}[/] [grey]- {Markup.Escape(skillDefinition.Description ?? string.Empty)}[/]");
            }

            return;
        }

        var session = _manager.ActiveSession;
        if (session is null)
        {
            _console.MarkupLine("[yellow]No active chat. Use /new <key> to create one first.[/]");
            return;
        }

        var selectedSkill = _skills.Resolve(skillName);
        if (selectedSkill is null)
        {
            _console.MarkupLine($"[red]Unknown skill:[/] {Markup.Escape(skillName)}");
            return;
        }

        session.SetSkill(selectedSkill);
        _console.MarkupLine($"[green]Switched skill to[/] [cyan]{Markup.Escape(selectedSkill.Name)}[/] [grey]for {Markup.Escape(session.Key)}[/]");
    }

    private async Task<CommandResult> HandleClearAsync(CancellationToken ct)
    {
        var session = _manager.ActiveSession;
        if (session is null)
        {
            _console.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        await session.ResetAsync(ct).ConfigureAwait(false);
        _console.MarkupLine($"[green]Cleared chat[/] [cyan]{Markup.Escape(session.Key)}[/] [grey]({Markup.Escape(session.Model)}, skill: {Markup.Escape(session.SkillName)})[/]");
        return CommandResult.Continue();
    }

    private CommandResult HandleModel(CommandInvocation invocation)
    {
        var session = _manager.ActiveSession;
        if (session is null)
        {
            _console.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        if (invocation.Arguments.Count == 0)
        {
            _console.MarkupLine($"[cyan]Active model:[/] {Markup.Escape(session.Model)}");
            _console.MarkupLine("[cyan]Available models:[/]");
            foreach (var model in _chatProvider.AvailableModels)
                _console.MarkupLine($"  [grey]•[/] {Markup.Escape(model)}");

            return CommandResult.Continue();
        }

        var requestedModel = invocation.Arguments[0];
        if (!_chatProvider.SupportsModel(requestedModel))
        {
            _console.MarkupLine($"[red]Unknown model:[/] {Markup.Escape(requestedModel)}");
            return CommandResult.Continue();
        }

        var replacement = _manager.Replace(session.Key, requestedModel, session.Skill);
        if (replacement is null)
        {
            _console.MarkupLine($"[red]No chat with key '{Markup.Escape(session.Key)}'[/]");
            return CommandResult.Continue();
        }

        AttachSession(replacement);
        _console.MarkupLine($"[green]Switched model to[/] [cyan]{Markup.Escape(replacement.Model)}[/] [grey]for {Markup.Escape(replacement.Key)}[/]");
        return CommandResult.Continue();
    }

    private async Task<CommandResult> HandleCompactAsync(CancellationToken ct)
    {
        var session = _manager.ActiveSession;
        if (session is null)
        {
            _console.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        var result = await session.CompactAsync(ct).ConfigureAwait(false);
        if (result is null)
        {
            _console.MarkupLine("[grey]No persisted conversation available to compact yet.[/]");
            return CommandResult.Continue();
        }

        if (!result.Applied)
        {
            _console.MarkupLine("[grey]Compaction did not reduce the active conversation window.[/]");
            return CommandResult.Continue();
        }

        _console.MarkupLine($"[green]Compacted chat[/] [cyan]{Markup.Escape(session.Key)}[/] [grey](strategy: {Markup.Escape(result.StrategyUsed)}, messages: {result.MessagesBefore} -> {result.MessagesAfter}, tokens: {result.TokensBefore} -> {result.TokensAfter})[/]");
        return CommandResult.Continue();
    }

    private void AttachSession(ChatSession session)
    {
        session.OnChunk += chunk => _console.Markup(Markup.Escape(chunk));
        session.OnStateChanged += changedSession =>
        {
            if (changedSession.State == ChatSessionState.Failed)
                _console.MarkupLine($"\n[red]Chat '{Markup.Escape(changedSession.Key)}' failed.[/]");
            else if (changedSession.State == ChatSessionState.Idle && changedSession.MessageCount > 0)
                _console.MarkupLine($"\n[green]Chat '{Markup.Escape(changedSession.Key)}' ready.[/]");
        };
    }

    private async Task<CommandResult> HandleResumeAsync(CommandInvocation invocation, CancellationToken ct)
    {
        var resumed = await _manager.ResumeLatestAsync(invocation.Arguments.Count > 0 ? invocation.Arguments[0] : null, ct).ConfigureAwait(false);
        if (resumed is null)
        {
            _console.MarkupLine("[yellow]No persisted session available to resume.[/]");
            return CommandResult.Continue();
        }

        AttachSession(resumed);
        var sessionInfo = await resumed.GetSessionInfoAsync(ct).ConfigureAwait(false);
        var messageCount = sessionInfo?.MessageCount ?? resumed.MessageCount;
        _console.MarkupLine($"[green]Resumed chat[/] [cyan]{Markup.Escape(resumed.Key)}[/] [grey]({Markup.Escape(resumed.Model)}, skill: {Markup.Escape(resumed.SkillName)}, messages: {messageCount})[/]");
        return CommandResult.Continue();
    }

    private async Task<CommandResult> HandleCostAsync(CancellationToken ct)
    {
        var session = _manager.ActiveSession;
        if (session is null)
        {
            _console.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        var liveSnapshot = await session.GetCostSnapshotAsync(ct).ConfigureAwait(false);
        var persisted = await session.GetSessionInfoAsync(ct).ConfigureAwait(false);

        if (liveSnapshot is null && persisted?.CostSnapshot is null)
        {
            _console.MarkupLine("[grey]No cost information available yet.[/]");
            return CommandResult.Continue();
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Scope")
            .AddColumn("Input")
            .AddColumn("Output")
            .AddColumn("Total")
            .AddColumn("Cost");

        if (liveSnapshot is not null)
        {
            table.AddRow(
                "Current host",
                liveSnapshot.TotalInputTokens.ToString(CultureInfo.InvariantCulture),
                liveSnapshot.TotalOutputTokens.ToString(CultureInfo.InvariantCulture),
                liveSnapshot.TotalTokens.ToString(CultureInfo.InvariantCulture),
                $"${liveSnapshot.TotalCost:F4}");
        }

        if (persisted?.CostSnapshot is { } snapshot)
        {
            table.AddRow(
                "Persisted session",
                snapshot.InputTokens.ToString(CultureInfo.InvariantCulture),
                snapshot.OutputTokens.ToString(CultureInfo.InvariantCulture),
                snapshot.TotalTokens.ToString(CultureInfo.InvariantCulture),
                snapshot.EstimatedCost.HasValue ? $"${snapshot.EstimatedCost.Value:F4}" : "n/a");
        }

        _console.Write(table);
        return CommandResult.Continue();
    }

    private CommandRegistry CreateCommands()
    {
        var registry = new CommandRegistry();
        var loader = new MarkdownCommandLoader();

        foreach (var command in loader.LoadFromDirectory(_workspace.UserCommandDirectory, CommandSource.User, optional: true))
            registry.Register(command);

        foreach (var command in loader.LoadFromDirectory(_workspace.ProjectCommandDirectory, CommandSource.Project, optional: true))
            registry.Register(command);

        foreach (var directory in _workspace.ExtraCommandDirectories)
        {
            foreach (var command in loader.LoadFromDirectory(directory, CommandSource.Custom, optional: true))
                registry.Register(command);
        }

        registry.Register(BuiltinCommands.CreateQuit("Exit the CLI."));
        registry.Register(new DelegateCommand("new", "Create a new chat session.", "/new <key> [model] [skill]", invocation =>
        {
            if (invocation.Arguments.Count < 1)
            {
                WriteUsage("/new <key> [model] [skill]");
                return CommandResult.Continue();
            }

            var key = invocation.Arguments[0];
            var model = invocation.Arguments.Count >= 2 ? invocation.Arguments[1] : PickModel();
            var requestedSkill = invocation.Arguments.Count >= 3 ? _skills.Resolve(invocation.Arguments[2]) : null;
            if (invocation.Arguments.Count >= 3 && requestedSkill is null)
            {
                _console.MarkupLine($"[red]Unknown skill:[/] {Markup.Escape(invocation.Arguments[2])}");
                return CommandResult.Continue();
            }

            HandleNew(key, model, requestedSkill);
            return CommandResult.Continue();
        }, ["add"]));
        registry.Register(new DelegateCommand("switch", "Switch the active chat.", "/switch <key>", invocation =>
        {
            if (invocation.Arguments.Count < 1)
            {
                WriteUsage("/switch <key>");
                return CommandResult.Continue();
            }

            HandleSwitch(invocation.Arguments[0]);
            return CommandResult.Continue();
        }, ["sw"]));
        registry.Register(new DelegateCommand("list", "List all chat sessions.", "/list", _ =>
        {
            HandleList();
            return CommandResult.Continue();
        }, ["ls"]));
        registry.Register(new DelegateCommand("remove", "Remove a chat session.", "/remove <key>", invocation =>
        {
            if (invocation.Arguments.Count < 1)
            {
                WriteUsage("/remove <key>");
                return CommandResult.Continue();
            }

            HandleRemove(invocation.Arguments[0]);
            return CommandResult.Continue();
        }, ["rm"]));
        registry.Register(BuiltinCommands.CreateStatus(_ =>
        {
            HandleStatus();
            return CommandResult.Continue();
        }));
        registry.Register(BuiltinCommands.CreateResume(HandleResumeAsync));
        registry.Register(BuiltinCommands.CreateCost((_, ct) => HandleCostAsync(ct)));
        registry.Register(BuiltinCommands.CreateClear((_, ct) => HandleClearAsync(ct)));
        registry.Register(BuiltinCommands.CreateModel(HandleModel));
        registry.Register(BuiltinCommands.CreateCompact((_, ct) => HandleCompactAsync(ct)));
        registry.Register(new DelegateCommand("cancel", "Cancel the active chat request.", "/cancel", _ =>
        {
            _manager.ActiveSession?.Cancel();
            _console.MarkupLine("[yellow]Cancelled active request.[/]");
            return CommandResult.Continue();
        }));
        registry.Register(new DelegateCommand("models", "List available models.", "/models", _ =>
        {
            _console.MarkupLine("[cyan]Available models:[/]");
            foreach (var model in _chatProvider.AvailableModels)
                _console.MarkupLine($"  [grey]•[/] {Markup.Escape(model)}");

            return CommandResult.Continue();
        }));
        registry.Register(new DelegateCommand("skill", "List skills or switch the active chat skill.", "/skill [name]", invocation =>
        {
            HandleSkill(invocation.Arguments.Count > 0 ? invocation.Arguments[0] : null);
            return CommandResult.Continue();
        }));
        registry.Register(new DelegateCommand("hello", "Show the local hello-world panel.", "/hello", _ =>
        {
            HelloWorldTool();
            return CommandResult.Continue();
        }));
        registry.Register(new DelegateCommand("logout", "Log out of the active provider and exit.", "/logout", _ =>
        {
            _chatProvider.Logout();
            var message = _chatProvider.RequiresAuthentication
                ? "Logged out. Restart to re-authenticate."
                : "Provider session closed.";
            _console.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
            return CommandResult.Stop();
        }));
        registry.Register(BuiltinCommands.CreateHelp(
            () => _commands.ListAll(),
            new CommandHelpOptions
            {
                MessageHint = "<message>  Send a message to the active chat",
                FooterLines =
                {
                    $"Project root: {_workspace.ProjectRoot}",
                    $"Session store: {_workspace.SessionDirectory}",
                    $"MCP config: {_workspace.ProjectMcpConfigPath} (project)",
                    $"MCP config: {_workspace.UserMcpConfigPath} (user)",
                },
            }));
        return registry;
    }

    private void WriteUsage(string usage)
        => _console.MarkupLine($"[red]Usage:[/] {Markup.Escape(usage)}");

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        InitializeAsync().GetAwaiter().GetResult();
    }

    private string? ReadInput(string prompt)
    {
        if (_useInteractivePrompt)
            return _console.Prompt(new TextPrompt<string>($"{prompt}> ").AllowEmpty());

        Console.Write($"{Markup.Remove(prompt)}> ");
        return _lineReader();
    }
}