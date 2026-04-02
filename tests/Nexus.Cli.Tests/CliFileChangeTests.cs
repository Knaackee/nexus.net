using FluentAssertions;
using Microsoft.Extensions.AI;
using Nexus.Testing.Mocks;
using Spectre.Console.Testing;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliFileChangeTests
{
    [Fact]
    public async Task FileEdit_Changes_Are_Tracked_Diffed_And_Reverted()
    {
        var workspaceRoot = TestWorkspace.CreateWorkspaceRoot();
        var filePath = Path.Combine(workspaceRoot, "notes.txt");
        await File.WriteAllTextAsync(filePath, "alpha" + Environment.NewLine + "beta" + Environment.NewLine);

        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("call-1", "file_edit", new Dictionary<string, object?>
            {
                ["path"] = "notes.txt",
                ["oldText"] = "beta",
                ["newText"] = "gamma",
            }))
            .WithResponse("Applied the edit.");

        var console = new TestConsole();
        using var app = new CliApplication(
            console: console,
            workspace: CliWorkspaceOptions.Create(workspaceRoot),
            chatProvider: new ScriptedCliChatProvider(client));

        await app.InitializeAsync();
        (await app.ExecuteInputAsync("/new edit test-model")).Should().BeTrue();

        var session = app.Manager.ActiveSession;
        session.Should().NotBeNull();

        (await app.ExecuteInputAsync("Update notes.txt so beta becomes gamma.")).Should().BeTrue();
        await WaitForDoneAsync(session!, TimeSpan.FromSeconds(20));

        (await File.ReadAllTextAsync(filePath)).Should().Contain("gamma");
        session.GetTrackedChanges().Should().ContainSingle();

        (await app.ExecuteInputAsync("/changes")).Should().BeTrue();
        (await app.ExecuteInputAsync("/diff 1")).Should().BeTrue();
        (await app.ExecuteInputAsync("/revert 1")).Should().BeTrue();

        (await File.ReadAllTextAsync(filePath)).Should().Contain("beta");
        console.Output.Should().Contain("change #1");
        console.Output.Should().Contain("Diff for change #1");
        console.Output.Should().Contain("Reverted change #1");
    }

    [Fact]
    public async Task Ollama_FileEdit_Smoke_Tracks_Diff_And_Revert()
    {
        var provider = await CliLiveOllamaSmokeTestsAccessor.CreateOllamaProviderOrSkipAsync();
        if (provider is null)
            return;

        using (provider)
        {
            var workspaceRoot = TestWorkspace.CreateWorkspaceRoot();
            var filePath = Path.Combine(workspaceRoot, "notes.txt");
            const string originalMarker = "CLI_EDIT_ORIGINAL";
            const string updatedMarker = "CLI_EDIT_UPDATED";
            await File.WriteAllTextAsync(filePath, originalMarker + Environment.NewLine);

            var console = new TestConsole();
            using var app = new CliApplication(
                console: console,
                workspace: CliWorkspaceOptions.Create(workspaceRoot),
                chatProvider: provider);

            await app.InitializeAsync();
            (await app.ExecuteInputAsync($"/new live-edit {provider.DefaultModel}")).Should().BeTrue();
            var session = app.Manager.ActiveSession;
            session.Should().NotBeNull();

            await SendEditPromptAsync(app, session!, originalMarker, updatedMarker);

            (await File.ReadAllTextAsync(filePath)).Should().Contain(updatedMarker);
            session.GetTrackedChanges().Should().NotBeEmpty();

            (await app.ExecuteInputAsync("/changes")).Should().BeTrue();
            (await app.ExecuteInputAsync("/diff")).Should().BeTrue();
            (await app.ExecuteInputAsync("/revert")).Should().BeTrue();

            (await File.ReadAllTextAsync(filePath)).Should().Contain(originalMarker);
        }
    }

    private static async Task SendEditPromptAsync(CliApplication app, ChatSession session, string originalMarker, string updatedMarker)
    {
        var prompts = new[]
        {
            $"Use the file_edit tool to replace '{originalMarker}' with '{updatedMarker}' in notes.txt. After the tool succeeds, reply with exactly EDIT_READY.",
            $"You must use the file_edit tool now. Replace '{originalMarker}' with '{updatedMarker}' in notes.txt and then reply with exactly EDIT_READY.",
        };

        foreach (var prompt in prompts)
        {
            (await app.ExecuteInputAsync(prompt)).Should().BeTrue();
            await WaitForDoneAsync(session, TimeSpan.FromMinutes(2));
            if (session.GetTrackedChanges().Count > 0 && session.LastOutput.Contains("EDIT_READY", StringComparison.Ordinal))
                return;
        }

        session.GetTrackedChanges().Should().NotBeEmpty("the model should have used the file_edit tool");
        session.LastOutput.Should().Contain("EDIT_READY");
    }

    private static async Task WaitForDoneAsync(ChatSession session, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (session.State == ChatSessionState.Running && !cts.IsCancellationRequested)
            await Task.Delay(100, cts.Token);
    }

    private sealed class ScriptedCliChatProvider : ICliChatProvider
    {
        private readonly IChatClient _client;

        public ScriptedCliChatProvider(IChatClient client)
        {
            _client = client;
        }

        public string ProviderName => "scripted";
        public bool RequiresAuthentication => false;
        public string DefaultModel => "test-model";
        public IReadOnlyList<string> AvailableModels => [DefaultModel];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task AuthenticateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public bool SupportsModel(string model) => string.Equals(model, DefaultModel, StringComparison.OrdinalIgnoreCase);
        public IChatClient CreateClient(string model) => _client;
        public void Logout() { }
        public void Dispose() => _client.Dispose();
    }
}

internal static class CliLiveOllamaSmokeTestsAccessor
{
    public static async Task<OllamaCliChatProvider?> CreateOllamaProviderOrSkipAsync()
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
}