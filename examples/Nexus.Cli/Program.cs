using System.Globalization;
using Spectre.Console;

namespace Nexus.Cli;

internal static class Program
{
    private static readonly ChatManager Manager = new();

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
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/quit" or "/exit" or "/q":
                return false;

            case "/new" or "/add":
                HandleNew(parts);
                break;

            case "/switch" or "/sw":
                HandleSwitch(parts);
                break;

            case "/list" or "/ls":
                HandleList();
                break;

            case "/remove" or "/rm":
                HandleRemove(parts);
                break;

            case "/hello":
                HelloWorldTool();
                break;

            case "/models":
                AnsiConsole.MarkupLine("[cyan]Available models:[/]");
                foreach (var m in CopilotChatClient.AvailableModels)
                    AnsiConsole.MarkupLine($"  [grey]•[/] {m}");
                break;

            case "/logout":
                CopilotAuth.Logout();
                AnsiConsole.MarkupLine("[yellow]Logged out. Restart to re-authenticate.[/]");
                return false;

            case "/help" or "/?":
                PrintHelp();
                break;

            case "/status":
                HandleStatus();
                break;

            case "/cancel":
                Manager.ActiveSession?.Cancel();
                AnsiConsole.MarkupLine("[yellow]Cancelled active request.[/]");
                break;

            default:
                AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(cmd)}. Type [cyan]/help[/].");
                break;
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private static void HandleNew(string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] /new <key> [model]");
            return;
        }

        var key = parts[1];
        var model = parts.Length >= 3 ? parts[2] : PickModel();

        try
        {
            var session = Manager.Add(key, model);
            session.OnChunk += chunk => AnsiConsole.Markup(Markup.Escape(chunk));
            session.OnStateChanged += s =>
            {
                if (s.State == ChatSessionState.Failed)
                    AnsiConsole.MarkupLine($"\n[red]Chat '{s.Key}' failed.[/]");
                else if (s.State == ChatSessionState.Idle && s.MessageCount > 0)
                    AnsiConsole.MarkupLine($"\n[green]Chat '{s.Key}' ready.[/]");
            };

            AnsiConsole.MarkupLine($"[green]Created chat[/] [cyan]{key}[/] [grey]({model})[/]");
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        }
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
                $"[cyan]{Markup.Escape(s.Key)}[/] [{stateColor}]{s.State}[/] [grey]({s.Model})[/]");

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
        var table = new Table()
            .Border(TableBorder.Simple)
            .HideHeaders()
            .AddColumn("Command")
            .AddColumn("Description");

        table.AddRow("[cyan]/new <key> [[model]][/]", "Create a new chat session");
        table.AddRow("[cyan]/switch <key>[/]", "Switch active chat");
        table.AddRow("[cyan]/list[/]", "List all chat sessions");
        table.AddRow("[cyan]/status[/]", "Show status & preview of all chats");
        table.AddRow("[cyan]/remove <key>[/]", "Remove a chat session");
        table.AddRow("[cyan]/cancel[/]", "Cancel the active chat's request");
        table.AddRow("[cyan]/models[/]", "List available models");
        table.AddRow("[cyan]/hello[/]", "Run the hello_world tool");
        table.AddRow("[cyan]/logout[/]", "Log out of GitHub Copilot");
        table.AddRow("[cyan]/help[/]", "Show this help");
        table.AddRow("[cyan]/quit[/]", "Exit");
        table.AddRow("[grey]<message>[/]", "Send a message to the active chat");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
