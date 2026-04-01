using FluentAssertions;
using Microsoft.Extensions.AI;
using Spectre.Console.Testing;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliRegressionTests
{
    [Fact]
    public async Task New_Without_Arguments_Prints_Usage_Without_Throwing()
    {
        var workspaceRoot = TestWorkspace.CreateWorkspaceRoot();
        var console = new TestConsole();
        using var app = new CliApplication(
            console: console,
            workspace: CliWorkspaceOptions.Create(workspaceRoot),
            chatProvider: new NoOpCliChatProvider());

        var continueProcessing = await app.ExecuteInputAsync("/new");

        continueProcessing.Should().BeTrue();
        console.Output.Should().Contain("Usage:");
        console.Output.Should().Contain("/new <key> [model] [skill]");
    }
}

public sealed class CliLiveOllamaSmokeTests
{
    [Fact]
    public async Task Ollama_Smoke_Covers_Core_Cli_Flow()
    {
        var provider = await CreateOllamaProviderOrSkipAsync();
        if (provider is null)
            return;

        using (provider)
        {
            var workspaceRoot = TestWorkspace.CreateWorkspaceRoot();
            var console = new TestConsole();
            using var app = new CliApplication(
                console: console,
                workspace: CliWorkspaceOptions.Create(workspaceRoot),
                chatProvider: provider);

            await app.InitializeAsync();

            (await app.ExecuteInputAsync("/help")).Should().BeTrue();
            (await app.ExecuteInputAsync("/models")).Should().BeTrue();
            (await app.ExecuteInputAsync($"/new smoke {provider.DefaultModel}")).Should().BeTrue();

            var activeSession = app.Manager.ActiveSession;
            activeSession.Should().NotBeNull();

            (await app.ExecuteInputAsync("Reply with exactly SMOKE_READY")).Should().BeTrue();
            await WaitForDoneAsync(activeSession!, TimeSpan.FromMinutes(2));

            activeSession.State.Should().Be(ChatSessionState.Idle, activeSession.LastOutput);
            activeSession.LastOutput.Should().NotBeNullOrWhiteSpace();

            (await app.ExecuteInputAsync("/cost")).Should().BeTrue();
            (await app.ExecuteInputAsync("/status")).Should().BeTrue();
            (await app.ExecuteInputAsync("/list")).Should().BeTrue();
            (await app.ExecuteInputAsync($"/model {provider.DefaultModel}")).Should().BeTrue();
            (await app.ExecuteInputAsync("/skill")).Should().BeTrue();
            (await app.ExecuteInputAsync("/remove smoke")).Should().BeTrue();
            (await app.ExecuteInputAsync("/resume resumed")).Should().BeTrue();
            (await app.ExecuteInputAsync("/hello")).Should().BeTrue();
            (await app.ExecuteInputAsync("/clear")).Should().BeTrue();

            app.Manager.ActiveSession.Should().NotBeNull();
            app.Manager.ActiveSession!.Key.Should().Be("resumed");
            console.Output.Should().Contain("Created chat");
            console.Output.Should().Contain("Resumed chat");
            console.Output.Should().Contain("Available models:");
        }
    }

    [Fact]
    public async Task Ollama_Logout_Stops_Command_Processing()
    {
        var provider = await CreateOllamaProviderOrSkipAsync();
        if (provider is null)
            return;

        using (provider)
        {
            var workspaceRoot = TestWorkspace.CreateWorkspaceRoot();
            var console = new TestConsole();
            using var app = new CliApplication(
                console: console,
                workspace: CliWorkspaceOptions.Create(workspaceRoot),
                chatProvider: provider);

            await app.InitializeAsync();

            var continueProcessing = await app.ExecuteInputAsync("/logout");

            continueProcessing.Should().BeFalse();
            console.Output.Should().Contain("Provider session closed.");
        }
    }

    private static async Task<OllamaCliChatProvider?> CreateOllamaProviderOrSkipAsync()
    {
        var provider = OllamaCliChatProvider.FromEnvironment();
        try
        {
            await provider.InitializeAsync();
            return provider;
        }
        catch
        {
            provider.Dispose();
            return null;
        }
    }

    private static async Task WaitForDoneAsync(ChatSession session, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (session.State == ChatSessionState.Running && !cts.IsCancellationRequested)
            await Task.Delay(100, cts.Token);
    }
}

internal sealed class NoOpCliChatProvider : ICliChatProvider
{
    public string ProviderName => "test";
    public bool RequiresAuthentication => false;
    public string DefaultModel => "test-model";
    public IReadOnlyList<string> AvailableModels => [DefaultModel];

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task AuthenticateAsync(CancellationToken ct = default) => Task.CompletedTask;

    public bool SupportsModel(string model) => true;

    public IChatClient CreateClient(string model) => throw new InvalidOperationException("No chat client should be created in this regression test.");

    public void Logout()
    {
    }

    public void Dispose()
    {
    }
}

internal static class TestWorkspace
{
    public static string CreateWorkspaceRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexus-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}