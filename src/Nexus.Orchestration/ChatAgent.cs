using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Orchestration;

public class ChatAgent : IAgent
{
    private readonly IChatClient _client;
    private readonly IToolExecutor? _toolExecutor;
    private readonly ChatAgentOptions _options;
    private AgentState _state = AgentState.Created;

    public AgentId Id { get; } = AgentId.New();
    public string Name { get; }
    public AgentState State => _state;

    public ChatAgent(string name, IChatClient client, ChatAgentOptions? options = null, IToolExecutor? toolExecutor = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new ChatAgentOptions();
        _toolExecutor = toolExecutor;
    }

    public async Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var evt in ExecuteStreamingAsync(task, context, ct))
        {
            if (evt is TextChunkEvent text)
                sb.Append(text.Text);
            else if (evt is AgentCompletedEvent completed)
                return completed.Result;
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
        var runtimeToolNames = ResolveToolNames(task.AgentDefinition);

        var systemPrompt = BuildSystemPrompt(CombinePrompts(_options.SystemPrompt, task.AgentDefinition?.SystemPrompt), _options.IncludeExecutionContext);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        if (task.Messages.Count > 0)
            messages.AddRange(task.Messages);
        else
            messages.Add(new ChatMessage(ChatRole.User, task.Description));

        var chatOptions = new ChatOptions();
        SetModelId(chatOptions, task.AgentDefinition?.ModelId);

        if (context.Tools is DefaultToolRegistry registry && runtimeToolNames.Count > 0)
            registry.BindToolsToAgent(Id, runtimeToolNames);

        var tools = context.Tools.ListForAgent(Id);
        if (tools.Count > 0)
        {
            chatOptions.Tools = tools.Select(t => (AITool)t.AsAIFunction()).ToList();
        }

        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            yield return new AgentIterationEvent(Id, iteration + 1, _options.MaxIterations);

            // Stream response and collect all content (text + function calls)
            var responseText = new StringBuilder();
            var functionCalls = new List<FunctionCallContent>();
            var usage = UsageTotals.Empty;
            decimal? estimatedCost = null;

            await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    responseText.Append(text);
                    yield return new TextChunkEvent(Id, text);
                }

                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent fc)
                        functionCalls.Add(fc);
                }

                usage = usage.Merge(ReadUsage(update));
                estimatedCost = MergeCost(estimatedCost, ReadEstimatedCost(update));
            }

            if (usage.HasValues || estimatedCost.HasValue)
                yield return new TokenUsageEvent(Id, usage.InputTokens, usage.OutputTokens, estimatedCost);

            // Build assistant message from streamed content
            var assistantContents = new List<AIContent>();
            if (responseText.Length > 0)
                assistantContents.Add(new TextContent(responseText.ToString()));
            assistantContents.AddRange(functionCalls);
            messages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));

            if (functionCalls.Count == 0)
            {
                var tokenUsage = usage.HasValues
                    ? new TokenUsageSummary(usage.InputTokens, usage.OutputTokens, usage.TotalTokens)
                    : null;
                var result = AgentResult.Success(responseText.ToString(), tokenUsage, estimatedCost);
                _state = AgentState.Completed;
                yield return new AgentStateChangedEvent(Id, AgentState.Running, AgentState.Completed);
                yield return new AgentCompletedEvent(Id, result);
                yield break;
            }

            var toolContext = new AgentToolContext(Id, context);
            var executionRequests = new List<ToolExecutionRequest>();

            // Process tool calls
            foreach (var fc in functionCalls)
            {
                var callId = fc.CallId ?? "unknown";
                var inputJson = fc.Arguments is not null
                    ? JsonSerializer.SerializeToElement(fc.Arguments)
                    : JsonDocument.Parse("{}").RootElement;

                yield return new ToolCallStartedEvent(Id, callId, fc.Name, inputJson);

                var tool = context.Tools.Resolve(fc.Name);
                if (tool is null)
                {
                    var errorResult = ToolResult.Failure($"Tool '{fc.Name}' not found");
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId, errorResult.Error!)]));
                    yield return new ToolCallCompletedEvent(Id, callId, errorResult);
                    continue;
                }

                // Tool Approval Check (T2.1)
                if (context.ApprovalGate is not null && tool.Annotations?.RequiresApproval == true)
                {
                    _state = AgentState.WaitingForApproval;
                    yield return new AgentStateChangedEvent(Id, AgentState.Running, AgentState.WaitingForApproval);

                    var approvalRequest = new ApprovalRequest(
                        $"Execute tool '{fc.Name}'",
                        Id,
                        fc.Name,
                        fc.Arguments is not null ? JsonSerializer.SerializeToElement(fc.Arguments) : null);

                    yield return new ApprovalRequestedEvent(Id, callId, approvalRequest.Description);

                    var approval = await context.ApprovalGate.RequestApprovalAsync(approvalRequest, ct: ct);

                    _state = AgentState.Running;
                    yield return new AgentStateChangedEvent(Id, AgentState.WaitingForApproval, AgentState.Running);

                    if (!approval.IsApproved)
                    {
                        var deniedResult = ToolResult.Denied(approval.Comment ?? "Denied by approval gate");
                        messages.Add(new ChatMessage(ChatRole.Tool,
                            [new FunctionResultContent(callId, deniedResult.Error!)]));
                        yield return new ToolCallCompletedEvent(Id, callId, deniedResult);
                        continue;
                    }
                }

                executionRequests.Add(new ToolExecutionRequest(callId, tool, inputJson));
            }

            var executionResults = _toolExecutor is not null
                ? await _toolExecutor.ExecuteAsync(executionRequests, toolContext, ct).ConfigureAwait(false)
                : await ExecuteSequentiallyAsync(executionRequests, toolContext, ct).ConfigureAwait(false);

            foreach (var executionResult in executionResults)
            {
                var resultStr = executionResult.Result.IsSuccess
                    ? executionResult.Result.Value?.ToString() ?? "null"
                    : $"Error: {executionResult.Result.Error}";

                messages.Add(new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(executionResult.CallId, resultStr)]));

                yield return new ToolCallCompletedEvent(Id, executionResult.CallId, executionResult.Result);
            }
        }

        _state = AgentState.Failed;
        yield return new AgentFailedEvent(Id, new InvalidOperationException($"Max iterations ({_options.MaxIterations}) exceeded"));
    }

    private static decimal? MergeCost(decimal? existing, decimal? candidate)
    {
        if (!candidate.HasValue)
            return existing;

        return existing.HasValue
            ? Math.Max(existing.Value, candidate.Value)
            : candidate.Value;
    }

    private static UsageTotals ReadUsage(object source)
    {
        var usageObject = ReadProperty(source, "Usage", "UsageDetails", "TokenUsage")
            ?? ReadAdditionalProperty(source, "Usage")
            ?? ReadAdditionalProperty(source, "UsageDetails")
            ?? ReadAdditionalProperty(source, "TokenUsage");

        if (usageObject is null)
            return UsageTotals.Empty;

        var inputTokens = ReadIntProperty(usageObject, "InputTokenCount", "InputTokens", "PromptTokenCount", "PromptTokens");
        var outputTokens = ReadIntProperty(usageObject, "OutputTokenCount", "OutputTokens", "CompletionTokenCount", "CompletionTokens");
        var totalTokens = ReadIntProperty(usageObject, "TotalTokenCount", "TotalTokens");
        if (totalTokens == 0)
            totalTokens = inputTokens + outputTokens;

        return new UsageTotals(inputTokens, outputTokens, totalTokens);
    }

    private static decimal? ReadEstimatedCost(object source)
    {
        var cost = ReadAdditionalProperty(source, "NexusEstimatedCost") ?? ReadProperty(source, "EstimatedCost");
        return cost switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => (decimal)doubleValue,
            float floatValue => (decimal)floatValue,
            string text when decimal.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    private static int ReadIntProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = ReadProperty(source, propertyName);
            if (value is null)
                continue;

            return value switch
            {
                int intValue => intValue,
                long longValue => (int)longValue,
                short shortValue => shortValue,
                _ when int.TryParse(value.ToString(), out var parsed) => parsed,
                _ => 0,
            };
        }

        return 0;
    }

    private static object? ReadProperty(object source, params string[] propertyNames)
    {
        var type = source.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property is not null)
                return property.GetValue(source);
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveToolNames(AgentDefinition? runtimeDefinition)
        => runtimeDefinition?.ToolNames is { Count: > 0 } runtimeTools
            ? runtimeTools
            : [];

    private static string? CombinePrompts(string? basePrompt, string? runtimePrompt)
    {
        if (string.IsNullOrWhiteSpace(basePrompt))
            return runtimePrompt;

        if (string.IsNullOrWhiteSpace(runtimePrompt))
            return basePrompt;

        if (string.Equals(basePrompt, runtimePrompt, StringComparison.Ordinal))
            return basePrompt;

        if (runtimePrompt.Contains(basePrompt, StringComparison.Ordinal))
            return runtimePrompt;

        if (basePrompt.Contains(runtimePrompt, StringComparison.Ordinal))
            return basePrompt;

        return $"{basePrompt}{Environment.NewLine}{Environment.NewLine}{runtimePrompt}";
    }

    private static string? BuildSystemPrompt(string? prompt, bool includeExecutionContext)
    {
        if (!includeExecutionContext)
            return prompt;

        var environmentBlock = BuildExecutionEnvironmentBlock();
        if (string.IsNullOrWhiteSpace(prompt))
            return environmentBlock;

        return $"{prompt}{Environment.NewLine}{Environment.NewLine}{environmentBlock}";
    }

    private static void SetModelId(ChatOptions options, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        options.GetType().GetProperty("ModelId")?.SetValue(options, modelId);
    }

    private static string BuildExecutionEnvironmentBlock()
    {
        var shell = OperatingSystem.IsWindows() ? "cmd.exe (/d /c)" : "/bin/bash (-lc)";
        var operatingSystem = OperatingSystem.IsWindows()
            ? "Windows"
            : OperatingSystem.IsMacOS()
                ? "macOS"
                : OperatingSystem.IsLinux()
                    ? "Linux"
                    : System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        var pathStyle = OperatingSystem.IsWindows()
            ? "Windows paths with backslashes and possible drive letters"
            : "POSIX paths with forward slashes";

        return string.Join(Environment.NewLine,
        [
            "Execution environment:",
            $"- Host operating system: {operatingSystem}",
            $"- Default shell wrapper for shell tools: {shell}",
            $"- Preferred path style: {pathStyle}",
            $"- Current process working directory: {Directory.GetCurrentDirectory()}",
            "Do not assume Linux semantics on Windows or Windows semantics on Unix.",
        ]);
    }

    private static object? ReadAdditionalProperty(object source, string key)
    {
        if (ReadProperty(source, "AdditionalProperties") is not IEnumerable entries)
            return null;

        foreach (var entry in entries)
        {
            var entryKey = ReadProperty(entry, "Key")?.ToString();
            if (string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                return ReadProperty(entry, "Value");
        }

        return null;
    }

    private static async Task<IReadOnlyList<ToolExecutionResult>> ExecuteSequentiallyAsync(
        List<ToolExecutionRequest> requests,
        IToolContext context,
        CancellationToken ct)
    {
        var results = new List<ToolExecutionResult>(requests.Count);
        foreach (var request in requests)
        {
            var result = await request.Tool.ExecuteAsync(request.Input, context, ct).ConfigureAwait(false);
            results.Add(new ToolExecutionResult(request.CallId, request.Tool.Name, result));
        }

        return results;
    }

    private sealed class AgentToolContext(AgentId agentId, IAgentContext agentContext) : IToolContext
    {
        public AgentId AgentId => agentId;
        public IToolRegistry Tools => agentContext.Tools;
        public ISecretProvider? Secrets => agentContext.Secrets;
        public IBudgetTracker? Budget => agentContext.Budget;
        public CorrelationContext Correlation => agentContext.Correlation;
    }

    private readonly record struct UsageTotals(int InputTokens, int OutputTokens, int TotalTokens)
    {
        public static UsageTotals Empty => new(0, 0, 0);

        public bool HasValues => InputTokens > 0 || OutputTokens > 0 || TotalTokens > 0;

        public UsageTotals Merge(UsageTotals candidate)
        {
            if (!candidate.HasValues)
                return this;

            return new UsageTotals(
                Math.Max(InputTokens, candidate.InputTokens),
                Math.Max(OutputTokens, candidate.OutputTokens),
                Math.Max(TotalTokens, candidate.TotalTokens));
        }
    }
}

public record ChatAgentOptions
{
    public string? SystemPrompt { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    public int MaxIterations { get; init; } = 25;
    public bool IncludeExecutionContext { get; init; } = true;
}
