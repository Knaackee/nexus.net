using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;
using Xunit;

namespace Nexus.Commands.Tests;

public sealed class SlashCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_Resolves_Command_By_Alias()
    {
        var registry = new CommandRegistry();
        registry.Register(new TestCommand());
        var dispatcher = new SlashCommandDispatcher(registry);

        var result = await dispatcher.DispatchAsync("/ls");

        result.WasCommand.Should().BeTrue();
        result.WasHandled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_Unknown_Command_Is_Reported()
    {
        var dispatcher = new SlashCommandDispatcher(new CommandRegistry());

        var result = await dispatcher.DispatchAsync("/missing");

        result.WasCommand.Should().BeTrue();
        result.WasHandled.Should().BeFalse();
        result.UnknownCommandName.Should().Be("missing");
    }

    [Fact]
    public void TryParse_Captures_Arguments_And_Text()
    {
        var dispatcher = new SlashCommandDispatcher(new CommandRegistry());

        var parsed = dispatcher.TryParse("/switch alpha beta", out var invocation);

        parsed.Should().BeTrue();
        invocation.Name.Should().Be("switch");
        invocation.Arguments.Should().Equal(["alpha", "beta"]);
        invocation.ArgumentText.Should().Be("alpha beta");
    }

    [Fact]
    public async Task MarkdownCommandLoader_Loads_Prompt_Command_From_Frontmatter()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "review.md"), """
---
name: review
description: Review current changes
type: prompt
aliases:
  - rv
---
Review the current changes and focus on:
- bugs
- tests

Arguments: {{args}}
""");

        var loader = new MarkdownCommandLoader();

        var commands = loader.LoadFromDirectory(root, CommandSource.Project, optional: false);

        commands.Should().ContainSingle();
        var command = commands[0];
        command.Name.Should().Be("review");
        command.Aliases.Should().Contain("rv");
        command.Type.Should().Be(CommandType.Prompt);

        var result = await command.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/review auth",
            Name = "review",
            Arguments = ["auth"],
            ArgumentText = "auth",
        });

        result.PromptToSend.Should().Contain("Arguments: auth");
    }

    [Fact]
    public void MarkdownCommandLoader_MissingOptionalDirectory_ReturnsEmpty()
    {
        var loader = new MarkdownCommandLoader();

        var commands = loader.LoadFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), optional: true);

        commands.Should().BeEmpty();
    }

    [Fact]
    public async Task BuiltinHelpCommand_Renders_Registered_Commands()
    {
        var registry = new CommandRegistry();
        registry.Register(new TestCommand());
        registry.Register(BuiltinCommands.CreateHelp(
            () => registry.ListAll(),
            new CommandHelpOptions
            {
                MessageHint = "<message>  Send a message to the active agent",
                FooterLines = { "Project root: D:/repo" },
            }));

        var help = registry.Resolve("help");
        var result = await help!.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/help",
            Name = "help",
        });

        result.Output.Should().Contain("Available commands:");
        result.Output.Should().Contain("/help");
        result.Output.Should().Contain("/list");
        result.Output.Should().Contain("<message>  Send a message to the active agent");
        result.Output.Should().Contain("Project root: D:/repo");
    }

    [Fact]
    public async Task UseDefaults_Registers_Framework_Builtins()
    {
        var services = new ServiceCollection();
        var builder = new CommandBuilder(services);
        builder.UseDefaults();
        using var provider = services.BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<SlashCommandDispatcher>();

        var help = await dispatcher.DispatchAsync("/help");
        var quit = await dispatcher.DispatchAsync("/quit");

        help.WasHandled.Should().BeTrue();
        help.Output.Should().Contain("Available commands:");
        quit.WasHandled.Should().BeTrue();
        quit.ContinueProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task BuiltinStatusCommand_Uses_Builtin_Metadata_And_Handler()
    {
        var command = BuiltinCommands.CreateStatus(_ => CommandResult.Continue(output: "status-ok"));

        command.Name.Should().Be("status");
        command.Usage.Should().Be("/status");
        command.Source.Should().Be(CommandSource.Builtin);

        var result = await command.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/status",
            Name = "status",
        });

        result.Output.Should().Be("status-ok");
    }

    [Fact]
    public async Task BuiltinResumeCommand_Uses_Standard_Usage_And_Handler()
    {
        var command = BuiltinCommands.CreateResume(invocation => CommandResult.Continue(output: invocation.ArgumentText));

        command.Name.Should().Be("resume");
        command.Usage.Should().Be("/resume [key]");
        command.Source.Should().Be(CommandSource.Builtin);

        var result = await command.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/resume alpha",
            Name = "resume",
            Arguments = ["alpha"],
            ArgumentText = "alpha",
        });

        result.Output.Should().Be("alpha");
    }

    [Fact]
    public async Task BuiltinCostCommand_Uses_Builtin_Metadata_And_Handler()
    {
        var command = BuiltinCommands.CreateCost((_, _) => Task.FromResult(CommandResult.Continue(output: "cost-ok")));

        command.Name.Should().Be("cost");
        command.Usage.Should().Be("/cost");
        command.Source.Should().Be(CommandSource.Builtin);

        var result = await command.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/cost",
            Name = "cost",
        });

        result.Output.Should().Be("cost-ok");
    }

    [Fact]
    public async Task BuiltinClearCommand_Uses_Builtin_Metadata_And_Handler()
    {
        var command = BuiltinCommands.CreateClear(_ => CommandResult.Continue(output: "clear-ok"));

        command.Name.Should().Be("clear");
        command.Usage.Should().Be("/clear");
        command.Source.Should().Be(CommandSource.Builtin);

        var result = await command.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/clear",
            Name = "clear",
        });

        result.Output.Should().Be("clear-ok");
    }

    [Fact]
    public async Task BuiltinModelCommand_Uses_Standard_Usage_And_Handler()
    {
        var command = BuiltinCommands.CreateModel(invocation => CommandResult.Continue(output: invocation.ArgumentText));

        command.Name.Should().Be("model");
        command.Usage.Should().Be("/model [name]");
        command.Source.Should().Be(CommandSource.Builtin);

        var result = await command.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/model gpt-5",
            Name = "model",
            Arguments = ["gpt-5"],
            ArgumentText = "gpt-5",
        });

        result.Output.Should().Be("gpt-5");
    }

    [Fact]
    public async Task BuiltinCompactCommand_Uses_Builtin_Metadata_And_Handler()
    {
        var command = BuiltinCommands.CreateCompact(_ => CommandResult.Continue(output: "compact-ok"));

        command.Name.Should().Be("compact");
        command.Usage.Should().Be("/compact");
        command.Source.Should().Be(CommandSource.Builtin);

        var result = await command.ExecuteAsync(new CommandInvocation
        {
            RawInput = "/compact",
            Name = "compact",
        });

        result.Output.Should().Be("compact-ok");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexus-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestCommand : ICommand
    {
        public string Name => "list";
        public string Description => "List sessions.";
        public IReadOnlyList<string> Aliases => ["ls"];

        public Task<CommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken ct = default)
            => Task.FromResult(CommandResult.Continue());
    }
}