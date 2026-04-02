using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Configuration;
using Nexus.Core.Agents;
using Nexus.Permissions;
using Nexus.Tools.Standard;

var workspaceRoot = Path.Combine(Path.GetTempPath(), "nexus-chat-edit-example", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspaceRoot);

var filePath = Path.Combine(workspaceRoot, "notes.txt");
await File.WriteAllTextAsync(filePath, "alpha" + Environment.NewLine + "beta" + Environment.NewLine);

Console.WriteLine($"Workspace: {workspaceRoot}");
Console.WriteLine("Before edit:");
Console.WriteLine(await File.ReadAllTextAsync(filePath));

await using var host = global::Nexus.Nexus.CreateDefault(_ => new FileEditExampleChatClient(), options =>
{
    options.SessionTitle = "chat-editing-with-diff-and-revert";
    options.DefaultAgentDefinition = new AgentDefinition
    {
        Name = "FileEditor",
        ModelId = "example-model",
        SystemPrompt = "Use the file_edit tool when the user asks you to update a file. After the tool runs, explain what changed.",
        ToolNames = ["file_edit"],
    };

    options.ConfigureConfiguration = configuration => configuration.SetProjectRoot(workspaceRoot);
    options.ConfigureTools = tools => tools
        .Only(StandardToolCategory.FileSystem)
        .Configure(toolOptions =>
        {
            toolOptions.BaseDirectory = workspaceRoot;
            toolOptions.WorkingDirectory = workspaceRoot;
        });
    options.ConfigurePermissions = permissions => permissions.UsePreset(PermissionPreset.AllowAll);
    options.ConfigureServices = services => services.AddFileChangeTracking(tracking => tracking.BaseDirectory = workspaceRoot);
});

await foreach (var update in host.RunAsync(new AgentLoopOptions
{
    UserInput = "Update notes.txt so beta becomes gamma. Then explain the change.",
    SessionTitle = "chat-editing-with-diff-and-revert",
    AgentDefinition = new AgentDefinition
    {
        Name = "FileEditor",
        ModelId = "example-model",
        SystemPrompt = "Use the file_edit tool when the user asks you to update a file. After the tool runs, explain what changed.",
        ToolNames = ["file_edit"],
    },
}))
{
    switch (update)
    {
        case TextChunkLoopEvent text when !string.IsNullOrWhiteSpace(text.Text):
            Console.Write(text.Text);
            break;

        case ToolCallCompletedLoopEvent toolCompleted:
            Console.WriteLine();
            Console.WriteLine($"Tool completed: {toolCompleted.Result.Value}");
            break;

        case LoopCompletedEvent completed:
            Console.WriteLine();
            Console.WriteLine($"Loop finished: {completed.FinalResult.Text}");
            break;
    }
}

var journal = host.Services.GetRequiredService<IFileChangeJournal>();
var change = journal.ListChanges().Single();

Console.WriteLine();
Console.WriteLine($"Tracked change #{change.ChangeId}: {change.Path}");
Console.WriteLine(change.UnifiedDiff);

await journal.RevertAsync(change.ChangeId);

Console.WriteLine("After revert:");
Console.WriteLine(await File.ReadAllTextAsync(filePath));

sealed class FileEditExampleChatClient : IChatClient
{
    private int _callCount;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _callCount++;
        if (_callCount == 1)
        {
            var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "file_edit", new Dictionary<string, object?>
            {
                ["path"] = "notes.txt",
                ["oldText"] = "beta",
                ["newText"] = "gamma",
            })]);

            return Task.FromResult(new ChatResponse(message));
        }

        var lastToolMessage = messages.LastOrDefault(message => message.Role == ChatRole.Tool);
        var summary = ExtractToolSummary(lastToolMessage) ?? "The file was updated.";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Updated notes.txt. {summary}")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = response.Messages[^1].Contents,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static string? ExtractToolSummary(ChatMessage? toolMessage)
    {
        if (toolMessage is null)
            return null;

        if (!string.IsNullOrWhiteSpace(toolMessage.Text))
            return toolMessage.Text;

        foreach (var content in toolMessage.Contents)
        {
            var property = content.GetType().GetProperty("Result") ?? content.GetType().GetProperty("Value");
            var value = property?.GetValue(content)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}