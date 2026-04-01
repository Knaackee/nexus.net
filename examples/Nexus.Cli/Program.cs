using System.Globalization;
using Nexus.Commands;
using Nexus.CostTracking;
using Nexus.Protocols.Mcp;
using Nexus.Sessions;
using Nexus.Skills;
using Spectre.Console;

namespace Nexus.Cli;

internal static class Program
{
    private static readonly CliWorkspaceOptions Workspace = CliWorkspaceOptions.Create(Directory.GetCurrentDirectory());
    private static readonly IReadOnlyList<McpServerConfig> McpServers = CliMcpConfiguration.Load(Workspace);
    private static readonly SkillCatalog Skills = CliSkillCatalog.CreateDefaultCatalog(Workspace);
    private static readonly ChatManager Manager = new(Skills, projectRoot: Workspace.ProjectRoot, sessionStorePath: Workspace.SessionDirectory, mcpServers: McpServers);
    private static readonly CommandRegistry Commands = CreateCommands();
    private static readonly SlashCommandDispatcher Dispatcher = new(Commands);

    private static async Task<int> Main()
    {
        AnsiConsole.Write(new FigletText("Nexus CLI").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]A multi-chat coding agent powered by GitHub Copilot[/]");
        AnsiConsole.WriteLine();

        // ── Authenticate ──
        if (!await AuthenticateAsync().ConfigureAwait(false))
            return 1;

        AnsiConsole.MarkupLine("[green]Authenticated with GitHub Copilot![/]");
        AnsiConsole.WriteLine();
        PrintHelp();

        // ── Main loop ──
        while (true)
        {
            var active = Manager.ActiveSession;
            var prompt = active is not null
                ? $"[cyan]{active.Key}[/] [grey]({active.Model})[/]"
                : "[grey]nexus[/]";

            var input = AnsiConsole.Prompt(
                new TextPrompt<string>($"{prompt}> ")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.StartsWith('/'))
            {
                if (!await HandleCommandAsync(input).ConfigureAwait(false))
                    break;
            }
            else
            {
                HandleChat(input);
            }
        }

        Manager.Dispose();
        return 0;
    }

    private static async Task<bool> AuthenticateAsync()
    {
        try
        {
            AnsiConsole.MarkupLine("[grey]Authenticating with GitHub Copilot...[/]");
            await CopilotAuth.GetTokenAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Authentication failed:[/] {Markup.Escape(ex.Message)}");
            return false;
        }
    }

    private static async Task<bool> HandleCommandAsync(string input)
    {
        var result = await Dispatcher.DispatchAsync(input).ConfigureAwait(false);
        if (!result.WasHandled)
        {
            if (result.UnknownCommandName is not null)
                AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(result.UnknownCommandName)}. Type [cyan]/help[/].");

            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
            AnsiConsole.MarkupLine(Markup.Escape(result.Output));

        if (!string.IsNullOrWhiteSpace(result.PromptToSend))
            HandleChat(result.PromptToSend);

        return result.ContinueProcessing;
    }

    private static string PickModel()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [cyan]model[/]:")
                .AddChoices(CopilotChatClient.AvailableModels));
    }

    private static void HandleSwitch(string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] /switch <key>");
            return;
        }

        if (Manager.Switch(parts[1]))
            AnsiConsole.MarkupLine($"[green]Switched to[/] [cyan]{parts[1]}[/]");
        else
            AnsiConsole.MarkupLine($"[red]No chat with key '{Markup.Escape(parts[1])}'[/]");
    }

    private static void HandleRemove(string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] /remove <key>");
            return;
        }

        if (Manager.Remove(parts[1]))
            AnsiConsole.MarkupLine($"[yellow]Removed chat '{Markup.Escape(parts[1])}'[/]");
        else
            AnsiConsole.MarkupLine($"[red]No chat with key '{Markup.Escape(parts[1])}'[/]");
    }

    private static void HandleList()
    {
        var sessions = Manager.Sessions;
        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No active chats. Use /new <key> to create one.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Key")
            .AddColumn("Model")
            .AddColumn("Skill")
            .AddColumn("State")
            .AddColumn("Messages");

        foreach (var s in sessions)
        {
            var stateColor = s.State switch
            {
                ChatSessionState.Running => "yellow",
                ChatSessionState.Failed => "red",
                _ => "green",
            };

            var isActive = string.Equals(s.Key, Manager.ActiveKey, StringComparison.OrdinalIgnoreCase);
            var keyText = isActive ? $"[bold cyan]{Markup.Escape(s.Key)}[/] *" : Markup.Escape(s.Key);

            table.AddRow(
                keyText,
                s.Model,
                s.SkillName,
                $"[{stateColor}]{s.State}[/]",
                s.MessageCount.ToString(CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);
    }

    private static void HandleStatus()
    {
        var sessions = Manager.Sessions;
        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No active chats.[/]");
            return;
        }

        foreach (var s in sessions)
        {
            var stateColor = s.State switch
            {
                ChatSessionState.Running => "yellow",
                ChatSessionState.Failed => "red",
                _ => "green",
            };

            AnsiConsole.MarkupLine(
                $"[cyan]{Markup.Escape(s.Key)}[/] [{stateColor}]{s.State}[/] [grey]({s.Model}, skill: {Markup.Escape(s.SkillName)})[/]");

            if (s.LastOutput.Length > 0)
            {
                var preview = s.LastOutput.Length > 200
                    ? s.LastOutput[..200] + "..."
                    : s.LastOutput;
                AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(preview)}[/]");
            }
        }
    }

    private static void HandleChat(string input)
    {
        var session = Manager.ActiveSession;
        if (session is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active chat. Use /new <key> to create one first.[/]");
            return;
        }

        if (session.State == ChatSessionState.Running)
        {
            AnsiConsole.MarkupLine("[yellow]Chat is still processing. Use /cancel to stop or /status to check.[/]");
            return;
        }

        session.Send(input);
    }

    private static void HelloWorldTool()
    {
        AnsiConsole.Write(
            new Panel("[bold cyan]Hello, World![/]\n\nThis is the Nexus CLI hello_world tool.\nIt demonstrates tool execution within the agent framework.")
                .Header("[yellow]hello_world tool[/]")
                .Border(BoxBorder.Rounded)
                .Padding(1, 0));
    }

    private static void PrintHelp()
    {
        var helpCommand = BuiltinCommands.CreateHelp(
            () => Commands.ListAll(),
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
            AnsiConsole.WriteLine(help.Output);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Project root:[/] {Markup.Escape(Workspace.ProjectRoot)}");
        AnsiConsole.MarkupLine($"[grey]Session store:[/] {Markup.Escape(Workspace.SessionDirectory)}");
        AnsiConsole.MarkupLine($"[grey]MCP config:[/] {Markup.Escape(Workspace.ProjectMcpConfigPath)} [grey](project),[/] {Markup.Escape(Workspace.UserMcpConfigPath)} [grey](user)[/]");
        if (McpServers.Count > 0)
            AnsiConsole.MarkupLine($"[grey]Loaded MCP servers:[/] {Markup.Escape(string.Join(", ", McpServers.Select(server => server.Name)))}");
        AnsiConsole.WriteLine();
    }

    private static void HandleNew(string key, string model, SkillDefinition? skill)
    {
        try
        {
            var session = Manager.Add(key, model, skill);
            AttachSession(session);
            var skillName = skill?.Name ?? session.SkillName;
            AnsiConsole.MarkupLine($"[green]Created chat[/] [cyan]{key}[/] [grey]({model}, skill: {Markup.Escape(skillName)})[/]");
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        }
    }

    private static void HandleSwitch(string key)
    {
        if (Manager.Switch(key))
            AnsiConsole.MarkupLine($"[green]Switched to[/] [cyan]{Markup.Escape(key)}[/]");
        else
            AnsiConsole.MarkupLine($"[red]No chat with key '{Markup.Escape(key)}'[/]");
    }

    private static void HandleRemove(string key)
    {
        if (Manager.Remove(key))
            AnsiConsole.MarkupLine($"[yellow]Removed chat '{Markup.Escape(key)}'[/]");
        else
            AnsiConsole.MarkupLine($"[red]No chat with key '{Markup.Escape(key)}'[/]");
    }

    private static void HandleSkill(string? skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            var activeSkill = Manager.ActiveSession?.SkillName;
            foreach (var skillDefinition in Skills.ListAll())
            {
                var marker = string.Equals(activeSkill, skillDefinition.Name, StringComparison.OrdinalIgnoreCase) ? "[green]*[/] " : string.Empty;
                AnsiConsole.MarkupLine($"{marker}[cyan]{Markup.Escape(skillDefinition.Name)}[/] [grey]- {Markup.Escape(skillDefinition.Description ?? string.Empty)}[/]");
            }

            return;
        }

        var session = Manager.ActiveSession;
        if (session is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active chat. Use /new <key> to create one first.[/]");
            return;
        }

        var selectedSkill = Skills.Resolve(skillName);
        if (selectedSkill is null)
        {
            AnsiConsole.MarkupLine($"[red]Unknown skill:[/] {Markup.Escape(skillName)}");
            return;
        }

        session.SetSkill(selectedSkill);
        AnsiConsole.MarkupLine($"[green]Switched skill to[/] [cyan]{Markup.Escape(selectedSkill.Name)}[/] [grey]for {Markup.Escape(session.Key)}[/]");
    }

    private static async Task<CommandResult> HandleClearAsync(CancellationToken ct)
    {
        var session = Manager.ActiveSession;
        if (session is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        await session.ResetAsync(ct).ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[green]Cleared chat[/] [cyan]{Markup.Escape(session.Key)}[/] [grey]({session.Model}, skill: {Markup.Escape(session.SkillName)})[/]");
        return CommandResult.Continue();
    }

    private static CommandResult HandleModel(CommandInvocation invocation)
    {
        var session = Manager.ActiveSession;
        if (session is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        if (invocation.Arguments.Count == 0)
        {
            AnsiConsole.MarkupLine($"[cyan]Active model:[/] {Markup.Escape(session.Model)}");
            AnsiConsole.MarkupLine("[cyan]Available models:[/]");
            foreach (var model in CopilotChatClient.AvailableModels)
                AnsiConsole.MarkupLine($"  [grey]•[/] {model}");

            return CommandResult.Continue();
        }

        var requestedModel = invocation.Arguments[0];
        if (!CopilotChatClient.AvailableModels.Contains(requestedModel, StringComparer.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[red]Unknown model:[/] {Markup.Escape(requestedModel)}");
            return CommandResult.Continue();
        }

        var replacement = Manager.Replace(session.Key, requestedModel, session.Skill);
        if (replacement is null)
        {
            AnsiConsole.MarkupLine($"[red]No chat with key '{Markup.Escape(session.Key)}'[/]");
            return CommandResult.Continue();
        }

        AttachSession(replacement);
        AnsiConsole.MarkupLine($"[green]Switched model to[/] [cyan]{Markup.Escape(replacement.Model)}[/] [grey]for {Markup.Escape(replacement.Key)}[/]");
        return CommandResult.Continue();
    }

    private static async Task<CommandResult> HandleCompactAsync(CancellationToken ct)
    {
        var session = Manager.ActiveSession;
        if (session is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        var result = await session.CompactAsync(ct).ConfigureAwait(false);
        if (result is null)
        {
            AnsiConsole.MarkupLine("[grey]No persisted conversation available to compact yet.[/]");
            return CommandResult.Continue();
        }

        if (!result.Applied)
        {
            AnsiConsole.MarkupLine("[grey]Compaction did not reduce the active conversation window.[/]");
            return CommandResult.Continue();
        }

        AnsiConsole.MarkupLine($"[green]Compacted chat[/] [cyan]{Markup.Escape(session.Key)}[/] [grey](strategy: {Markup.Escape(result.StrategyUsed)}, messages: {result.MessagesBefore} -> {result.MessagesAfter}, tokens: {result.TokensBefore} -> {result.TokensAfter})[/]");
        return CommandResult.Continue();
    }

    private static void AttachSession(ChatSession session)
    {
        session.OnChunk += chunk => AnsiConsole.Markup(Markup.Escape(chunk));
        session.OnStateChanged += s =>
        {
            if (s.State == ChatSessionState.Failed)
                AnsiConsole.MarkupLine($"\n[red]Chat '{s.Key}' failed.[/]");
            else if (s.State == ChatSessionState.Idle && s.MessageCount > 0)
                AnsiConsole.MarkupLine($"\n[green]Chat '{s.Key}' ready.[/]");
        };
    }

    private static async Task<CommandResult> HandleResumeAsync(CommandInvocation invocation, CancellationToken ct)
    {
        var resumed = await Manager.ResumeLatestAsync(invocation.Arguments.Count > 0 ? invocation.Arguments[0] : null, ct).ConfigureAwait(false);
        if (resumed is null)
        {
            AnsiConsole.MarkupLine("[yellow]No persisted session available to resume.[/]");
            return CommandResult.Continue();
        }

        AttachSession(resumed);
        var sessionInfo = await resumed.GetSessionInfoAsync(ct).ConfigureAwait(false);
        var messageCount = sessionInfo?.MessageCount ?? resumed.MessageCount;
        AnsiConsole.MarkupLine($"[green]Resumed chat[/] [cyan]{Markup.Escape(resumed.Key)}[/] [grey]({resumed.Model}, skill: {Markup.Escape(resumed.SkillName)}, messages: {messageCount})[/]");
        return CommandResult.Continue();
    }

    private static async Task<CommandResult> HandleCostAsync(CancellationToken ct)
    {
        var session = Manager.ActiveSession;
        if (session is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active chat. Use /new or /resume first.[/]");
            return CommandResult.Continue();
        }

        var liveSnapshot = await session.GetCostSnapshotAsync(ct).ConfigureAwait(false);
        var persisted = await session.GetSessionInfoAsync(ct).ConfigureAwait(false);

        if (liveSnapshot is null && persisted?.CostSnapshot is null)
        {
            AnsiConsole.MarkupLine("[grey]No cost information available yet.[/]");
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

        AnsiConsole.Write(table);
        return CommandResult.Continue();
    }

    private static CommandRegistry CreateCommands()
    {
        var registry = new CommandRegistry();
        var loader = new MarkdownCommandLoader();

        foreach (var command in loader.LoadFromDirectory(Workspace.UserCommandDirectory, CommandSource.User, optional: true))
            registry.Register(command);

        foreach (var command in loader.LoadFromDirectory(Workspace.ProjectCommandDirectory, CommandSource.Project, optional: true))
            registry.Register(command);

        foreach (var directory in Workspace.ExtraCommandDirectories)
        {
            foreach (var command in loader.LoadFromDirectory(directory, CommandSource.Custom, optional: true))
                registry.Register(command);
        }

        registry.Register(BuiltinCommands.CreateQuit("Exit the CLI."));
        registry.Register(new DelegateCommand("new", "Create a new chat session.", "/new <key> [model] [skill]", invocation =>
        {
            if (invocation.Arguments.Count < 1)
            {
                AnsiConsole.MarkupLine("[red]Usage:[/] /new <key> [model] [skill]");
                return CommandResult.Continue();
            }

            var key = invocation.Arguments[0];
            var model = invocation.Arguments.Count >= 2 ? invocation.Arguments[1] : PickModel();
            var requestedSkill = invocation.Arguments.Count >= 3 ? Skills.Resolve(invocation.Arguments[2]) : null;
            if (invocation.Arguments.Count >= 3 && requestedSkill is null)
            {
                AnsiConsole.MarkupLine($"[red]Unknown skill:[/] {Markup.Escape(invocation.Arguments[2])}");
                return CommandResult.Continue();
            }

            HandleNew(key, model, requestedSkill);
            return CommandResult.Continue();
        }, ["add"]));
        registry.Register(new DelegateCommand("switch", "Switch the active chat.", "/switch <key>", invocation =>
        {
            if (invocation.Arguments.Count < 1)
            {
                AnsiConsole.MarkupLine("[red]Usage:[/] /switch <key>");
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
                AnsiConsole.MarkupLine("[red]Usage:[/] /remove <key>");
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
            Manager.ActiveSession?.Cancel();
            AnsiConsole.MarkupLine("[yellow]Cancelled active request.[/]");
            return CommandResult.Continue();
        }));
        registry.Register(new DelegateCommand("models", "List available models.", "/models", _ =>
        {
            AnsiConsole.MarkupLine("[cyan]Available models:[/]");
            foreach (var model in CopilotChatClient.AvailableModels)
                AnsiConsole.MarkupLine($"  [grey]•[/] {model}");

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
        registry.Register(new DelegateCommand("logout", "Log out of GitHub Copilot and exit.", "/logout", _ =>
        {
            CopilotAuth.Logout();
            AnsiConsole.MarkupLine("[yellow]Logged out. Restart to re-authenticate.[/]");
            return CommandResult.Stop();
        }));
        registry.Register(BuiltinCommands.CreateHelp(
            () => Commands.ListAll(),
            new CommandHelpOptions
            {
                MessageHint = "<message>  Send a message to the active chat",
                FooterLines =
                {
                    $"Project root: {Workspace.ProjectRoot}",
                    $"Session store: {Workspace.SessionDirectory}",
                    $"MCP config: {Workspace.ProjectMcpConfigPath} (project)",
                    $"MCP config: {Workspace.UserMcpConfigPath} (user)",
                },
            }));
        return registry;
    }
}
