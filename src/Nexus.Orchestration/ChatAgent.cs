using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Tools;

namespace Nexus.Orchestration;

public class ChatAgent : IAgent
{
    private readonly IChatClient _client;
    private readonly ChatAgentOptions _options;
    private AgentState _state = AgentState.Created;

    public AgentId Id { get; } = AgentId.New();
    public string Name { get; }
    public AgentState State => _state;

    public ChatAgent(string name, IChatClient client, ChatAgentOptions? options = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new ChatAgentOptions();
    }

    public async Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var evt in ExecuteStreamingAsync(task, context, ct))
        {
            if (evt is TextChunkEvent text)
                sb.Append(text.Text);
            else if (evt is AgentFailedEvent failed)
                return AgentResult.Failed(failed.Error.Message);
        }

        return AgentResult.Success(sb.ToString());
    }

    public async IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(
        AgentTask task, IAgentContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _state = AgentState.Running;
        yield return new AgentStateChangedEvent(Id, AgentState.Idle, AgentState.Running);

        var messages = new List<ChatMessage>();

        if (_options.SystemPrompt is not null)
            messages.Add(new ChatMessage(ChatRole.System, _options.SystemPrompt));

        messages.Add(new ChatMessage(ChatRole.User, task.Description));

        var chatOptions = new ChatOptions();
        var tools = context.Tools.ListForAgent(Id);
        if (tools.Count > 0)
        {
            chatOptions.Tools = tools.Select(t => (AITool)t.AsAIFunction()).ToList();
        }

        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            yield return new AgentIterationEvent(Id, iteration + 1, _options.MaxIterations);

            var response = _client.GetStreamingResponseAsync(messages, chatOptions, ct);
            var fullResponse = new StringBuilder();
            var toolCallsInProgress = new Dictionary<string, StringBuilder>();

            await foreach (var update in response.WithCancellation(ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    fullResponse.Append(text);
                    yield return new TextChunkEvent(Id, text);
                }
            }

            // Check for function call results
            var lastResponse = await _client.GetResponseAsync(messages, chatOptions, ct);
            messages.AddRange(lastResponse.Messages);

            bool hasFunctionCalls = lastResponse.Messages
                .Any(m => m.Contents.OfType<FunctionCallContent>().Any());

            if (!hasFunctionCalls)
            {
                var resultText = fullResponse.Length > 0
                    ? fullResponse.ToString()
                    : lastResponse.Text ?? string.Empty;

                var result = AgentResult.Success(resultText);
                _state = AgentState.Completed;
                yield return new AgentStateChangedEvent(Id, AgentState.Running, AgentState.Completed);
                yield return new AgentCompletedEvent(Id, result);
                yield break;
            }

            // Process tool calls
            foreach (var msg in lastResponse.Messages)
            {
                foreach (var fc in msg.Contents.OfType<FunctionCallContent>())
                {
                    yield return new ToolCallStartedEvent(Id, fc.CallId ?? "unknown", fc.Name, JsonSerializer.SerializeToElement(fc.Arguments));

                    var tool = context.Tools.Resolve(fc.Name);
                    if (tool is null)
                    {
                        var errorResult = ToolResult.Failure($"Tool '{fc.Name}' not found");
                        messages.Add(new ChatMessage(ChatRole.Tool,
                            [new FunctionResultContent(fc.CallId ?? "unknown", errorResult.Error!)]));
                        yield return new ToolCallCompletedEvent(Id, fc.CallId ?? "unknown", errorResult);
                        continue;
                    }

                    var toolContext = new AgentToolContext(Id, context);
                    var inputJson = fc.Arguments is not null
                        ? JsonSerializer.SerializeToElement(fc.Arguments)
                        : JsonDocument.Parse("{}").RootElement;
                    var toolResult = await tool.ExecuteAsync(inputJson, toolContext, ct);

                    var resultStr = toolResult.IsSuccess
                        ? toolResult.Value?.ToString() ?? "null"
                        : $"Error: {toolResult.Error}";

                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(fc.CallId ?? "unknown", resultStr)]));

                    yield return new ToolCallCompletedEvent(Id, fc.CallId ?? "unknown", toolResult);
                }
            }
        }

        _state = AgentState.Failed;
        yield return new AgentFailedEvent(Id, new InvalidOperationException($"Max iterations ({_options.MaxIterations}) exceeded"));
    }

    private sealed class AgentToolContext(AgentId agentId, IAgentContext agentContext) : IToolContext
    {
        public AgentId AgentId => agentId;
        public IToolRegistry Tools => agentContext.Tools;
        public ISecretProvider? Secrets => agentContext.Secrets;
        public IBudgetTracker? Budget => agentContext.Budget;
        public CorrelationContext Correlation => agentContext.Correlation;
    }
}

public record ChatAgentOptions
{
    public string? SystemPrompt { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    public int MaxIterations { get; init; } = 25;
}
