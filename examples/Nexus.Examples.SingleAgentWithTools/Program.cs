using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Events;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Permissions;

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => new ToolCallingChatClient());
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddPermissions(p => p.UsePreset(PermissionPreset.Interactive));
});

await using var serviceProvider = services.BuildServiceProvider();

var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
toolRegistry.Register(new LambdaTool(
    "get_time",
    "Returns the current UTC timestamp.",
    (_, _, _) => Task.FromResult(ToolResult.Success(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))))
{
    Annotations = new ToolAnnotations { IsReadOnly = true, IsIdempotent = true },
});

var pool = serviceProvider.GetRequiredService<IAgentPool>();
var orchestrator = serviceProvider.GetRequiredService<IOrchestrator>();

var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Assistant",
    SystemPrompt = "Use the get_time tool when the user asks for the current time.",
    ToolNames = ["get_time"],
});

string? finalText = null;

await foreach (var evt in orchestrator.ExecuteSequenceStreamingAsync([
    AgentTask.Create("What time is it right now?") with { AssignedAgent = agent.Id }
]))
{
    switch (evt)
    {
        case AgentEventInGraph { InnerEvent: ToolCallStartedEvent toolStart }:
            Console.WriteLine($"Tool call started: {toolStart.ToolName}");
            break;

        case AgentEventInGraph { InnerEvent: TextChunkEvent textChunk }:
            Console.Write(textChunk.Text);
            break;

        case NodeCompletedEvent completed:
            finalText = completed.Result.Text;
            break;
    }
}

Console.WriteLine();
Console.WriteLine($"Final answer: {finalText}");

sealed class ToolCallingChatClient : IChatClient
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
            var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_time")]);
            return Task.FromResult(new ChatResponse(message));
        }

        var toolMessage = messages.LastOrDefault(m => m.Role == ChatRole.Tool);
        var text = ExtractToolText(toolMessage);

        if (string.IsNullOrWhiteSpace(text))
            text = "the tool result returned no displayable text";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"The current UTC time is {text}.")));
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

    private static string? ExtractToolText(ChatMessage? toolMessage)
    {
        if (toolMessage is null)
            return null;

        if (!string.IsNullOrWhiteSpace(toolMessage.Text))
            return toolMessage.Text;

        foreach (var content in toolMessage.Contents)
        {
            var contentType = content.GetType();
            var property = contentType.GetProperty("Result") ?? contentType.GetProperty("Value");
            var value = property?.GetValue(content)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}