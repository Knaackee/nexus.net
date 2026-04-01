using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Compaction;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Sessions;

namespace Nexus.AgentLoop;

public sealed class DefaultAgentLoop : IAgentLoop
{
    private readonly IServiceProvider _services;
    private readonly IAgentPool _agentPool;
    private readonly ICompactionService? _compactionService;
    private readonly ICompactionRecallService? _compactionRecallService;
    private readonly ISessionStore? _sessionStore;
    private readonly ISessionTranscript? _sessionTranscript;

    public DefaultAgentLoop(
        IServiceProvider services,
        IAgentPool agentPool,
        ICompactionService? compactionService = null,
        ICompactionRecallService? compactionRecallService = null,
        ISessionStore? sessionStore = null,
        ISessionTranscript? sessionTranscript = null)
    {
        _services = services;
        _agentPool = agentPool;
        _compactionService = compactionService;
        _compactionRecallService = compactionRecallService;
        _sessionStore = sessionStore;
        _sessionTranscript = sessionTranscript;
    }

    public async IAsyncEnumerable<AgentLoopEvent> RunAsync(
        AgentLoopOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var session = await ResolveSessionAsync(options, ct).ConfigureAwait(false);
        var sessionId = session?.Id;
        var history = await BuildMessageHistoryAsync(options, sessionId, ct).ConfigureAwait(false);
        if (options.RoutingStrategy is null)
        {
            await foreach (var evt in RunSingleStepAsync(options, sessionId, history, ct).ConfigureAwait(false))
                yield return evt;

            yield break;
        }

        await foreach (var evt in RunRoutedAsync(options, sessionId, history, ct).ConfigureAwait(false))
            yield return evt;
    }

    private async IAsyncEnumerable<AgentLoopEvent> RunSingleStepAsync(
        AgentLoopOptions options,
        SessionId? sessionId,
        List<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var agent = await ResolveAgentAsync(options, ct).ConfigureAwait(false);
        var ownsAgent = options.Agent is null;

        try
        {
            var turnCount = CountTurns(history);
            if (turnCount > options.MaxTurns)
            {
                var maxTurnResult = AgentResult.Failed($"Max turns ({options.MaxTurns}) reached before executing the next loop iteration.");
                yield return new LoopCompletedEvent(sessionId, agent.Id, LoopStopReason.MaxTurnsReached, maxTurnResult);
                yield break;
            }

            await foreach (var evt in CompactIfNeededAsync(sessionId, agent.Id, history, options, options.AgentDefinition, ct).ConfigureAwait(false))
                yield return evt;

            if (sessionId.HasValue && _sessionTranscript is not null)
            {
                foreach (var message in options.Messages)
                    await _sessionTranscript.AppendAsync(sessionId.Value, message, ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(options.UserInput))
                    await _sessionTranscript.AppendAsync(sessionId.Value, new ChatMessage(ChatRole.User, options.UserInput), ct).ConfigureAwait(false);
            }

            var turn = await ExecuteTurnAsync(
                sessionId,
                agent,
                options.AgentDefinition,
                history,
                null,
                options,
                ct).ConfigureAwait(false);

            foreach (var evt in turn.Events)
                yield return evt;

            yield return new LoopCompletedEvent(sessionId, agent.Id, ResolveStopReason(options, turn.Result), turn.Result);
        }
        finally
        {
            if (ownsAgent)
                await _agentPool.KillAsync(agent.Id, ct).ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<AgentLoopEvent> RunRoutedAsync(
        AgentLoopOptions options,
        SessionId? sessionId,
        List<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var completedSteps = new Dictionary<string, AgentResult>(StringComparer.OrdinalIgnoreCase);
        RoutingStepResult? previousStep = null;
        var lastAgentId = options.Agent?.Id ?? AgentId.New();

        while (!ct.IsCancellationRequested)
        {
            if (completedSteps.Count >= options.MaxTurns)
            {
                var maxTurnResult = previousStep?.Result ?? AgentResult.Failed($"Max turns ({options.MaxTurns}) reached.");
                yield return new LoopCompletedEvent(sessionId, lastAgentId, LoopStopReason.MaxTurnsReached, maxTurnResult);
                yield break;
            }

            var decision = await options.RoutingStrategy!.NextAsync(new RoutingContext
            {
                Options = options,
                History = history,
                CompletedSteps = completedSteps,
                PreviousStep = previousStep,
                ApprovalGate = _services.GetService<IApprovalGate>(),
            }, ct).ConfigureAwait(false);

            if (decision is StopRoutingDecision stop)
            {
                var finalResult = previousStep?.Result
                    ?? (string.IsNullOrWhiteSpace(stop.Message)
                        ? AgentResult.Success("Workflow completed.")
                        : AgentResult.Success(stop.Message));
                yield return new LoopCompletedEvent(sessionId, lastAgentId, stop.Reason, finalResult);
                yield break;
            }

            var runDecision = (RunAgentRoutingDecision)decision;
            await foreach (var evt in CompactIfNeededAsync(sessionId, lastAgentId, history, options, runDecision.AgentDefinition, ct).ConfigureAwait(false))
                yield return evt;

            history.Add(new ChatMessage(ChatRole.User, runDecision.InputText));
            if (sessionId.HasValue && _sessionTranscript is not null)
                await _sessionTranscript.AppendAsync(sessionId.Value, history[^1], ct).ConfigureAwait(false);

            var agent = await _agentPool.SpawnAsync(runDecision.AgentDefinition, ct).ConfigureAwait(false);
            try
            {
                lastAgentId = agent.Id;
                var turn = await ExecuteTurnAsync(
                    sessionId,
                    agent,
                    runDecision.AgentDefinition,
                    history,
                    runDecision.StepName,
                    options,
                    ct).ConfigureAwait(false);

                foreach (var evt in turn.Events)
                    yield return evt;

                completedSteps[runDecision.StepId] = turn.Result;
                previousStep = new RoutingStepResult(runDecision.StepId, runDecision.StepName, agent.Id, runDecision.AgentDefinition, turn.Result);
            }
            finally
            {
                await _agentPool.KillAsync(agent.Id, ct).ConfigureAwait(false);
            }
        }
    }

    private async IAsyncEnumerable<AgentLoopEvent> CompactIfNeededAsync(
        SessionId? sessionId,
        AgentId agentId,
        List<ChatMessage> history,
        AgentLoopOptions options,
        AgentDefinition? stepDefinition,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var contextWindow = ResolveContextWindow(options, stepDefinition);
        var systemPrompt = ResolveSystemPrompt(options, stepDefinition);
        var modelId = ResolveModelId(options, stepDefinition);
        if (_compactionService is null || !_compactionService.ShouldCompact(history, contextWindow, systemPrompt, modelId))
            yield break;

        var originalMessages = history.ToArray();

        var compactionResult = await _compactionService.CompactAsync(
            history,
            contextWindow,
            ResolveChatClient(options, stepDefinition),
            systemPrompt,
            modelId,
            ct).ConfigureAwait(false);

        if (compactionResult.CompactedMessages.Count == history.Count && compactionResult.TokensAfter >= compactionResult.TokensBefore)
        {
            var failed = AgentResult.Failed("Compaction failed to reduce the active context window.");
            yield return new LoopCompletedEvent(sessionId, agentId, LoopStopReason.CompactionFailed, failed);
            yield break;
        }

        var activeMessages = compactionResult.CompactedMessages;
        if (_compactionRecallService is not null)
        {
            var recallResult = await _compactionRecallService.RecallAsync(new CompactionRecallContext
            {
                OriginalMessages = originalMessages,
                ActiveMessages = compactionResult.CompactedMessages,
                Compaction = compactionResult,
                WindowOptions = contextWindow,
                SystemPrompt = systemPrompt,
                ModelId = modelId,
            }, ct).ConfigureAwait(false);

            activeMessages = recallResult.Messages;
            if (_compactionService.ShouldCompact(activeMessages, contextWindow, systemPrompt, modelId))
            {
                var failed = AgentResult.Failed("Post-compaction recall exceeded the active context window.");
                yield return new LoopCompletedEvent(sessionId, agentId, LoopStopReason.CompactionFailed, failed);
                yield break;
            }
        }

        history.Clear();
        history.AddRange(activeMessages);
        yield return new CompactionTriggeredLoopEvent(sessionId, agentId, compactionResult.StrategyUsed, compactionResult.TokensBefore, compactionResult.TokensAfter);
    }

    private async Task<TurnExecution> ExecuteTurnAsync(
        SessionId? sessionId,
        IAgent agent,
        AgentDefinition? agentDefinition,
        List<ChatMessage> history,
        string? stepName,
        AgentLoopOptions options,
        CancellationToken ct)
    {
        var events = new List<AgentLoopEvent>
        {
            new LoopStartedEvent(sessionId, agent.Id, history.Count)
        };

        var task = AgentTask.Create(stepName ?? ResolveDescription(options, history)) with
        {
            AssignedAgent = agent.Id,
            AgentDefinition = agentDefinition,
            Messages = history,
        };

        AgentResult? completedResult = null;
        await foreach (var evt in ExecuteAgentStreamingAsync(agent, task, new AgentLoopContext(agent, _services), ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case TextChunkEvent text:
                    events.Add(new TextChunkLoopEvent(sessionId, agent.Id, text.Text));
                    break;
                case ReasoningChunkEvent reasoning:
                    events.Add(new ReasoningChunkLoopEvent(sessionId, agent.Id, reasoning.Text));
                    break;
                case ToolCallStartedEvent toolStarted:
                    events.Add(new ToolCallStartedLoopEvent(sessionId, agent.Id, toolStarted.ToolCallId, toolStarted.ToolName));
                    break;
                case ToolCallProgressEvent toolProgress:
                    events.Add(new ToolCallProgressLoopEvent(sessionId, agent.Id, toolProgress.ToolCallId, toolProgress.Message, toolProgress.Progress));
                    break;
                case ToolCallCompletedEvent toolCompleted:
                    events.Add(new ToolCallCompletedLoopEvent(sessionId, agent.Id, toolCompleted.ToolCallId, toolCompleted.Result));
                    break;
                case ApprovalRequestedEvent approval:
                    events.Add(new ApprovalRequestedLoopEvent(sessionId, agent.Id, approval.ApprovalId, approval.Description));
                    break;
                case TokenUsageEvent usage:
                    events.Add(new TokenUsageLoopEvent(sessionId, agent.Id, usage.InputTokens, usage.OutputTokens, usage.EstimatedCost));
                    break;
                case AgentCompletedEvent completed:
                    completedResult = completed.Result;
                    events.Add(new TurnCompletedLoopEvent(sessionId, agent.Id, completed.Result));
                    break;
                case AgentFailedEvent failed:
                    events.Add(new LoopErrorEvent(sessionId, agent.Id, failed.Error));
                    completedResult = AgentResult.Failed(failed.Error.Message);
                    break;
            }
        }

        completedResult ??= AgentResult.Failed("Agent loop completed without a terminal result.");
        if (!string.IsNullOrWhiteSpace(completedResult.Text))
        {
            var assistantMessage = new ChatMessage(ChatRole.Assistant, completedResult.Text);
            history.Add(assistantMessage);
            if (sessionId.HasValue && _sessionTranscript is not null)
                await _sessionTranscript.AppendAsync(sessionId.Value, assistantMessage, ct).ConfigureAwait(false);
        }

        if (sessionId.HasValue && _sessionStore is not null)
            await UpdateSessionSnapshotAsync(sessionId.Value, completedResult, ct).ConfigureAwait(false);

        return new TurnExecution(completedResult, events);
    }

    private async Task<SessionInfo?> ResolveSessionAsync(AgentLoopOptions options, CancellationToken ct)
    {
        if (_sessionStore is null)
            return null;

        if (options.SessionId is { } sessionId)
            return await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);

        if (options.ResumeLastSession)
        {
            await foreach (var existing in _sessionStore.ListAsync(new SessionFilter { Limit = 1 }, ct).ConfigureAwait(false))
                return existing;
        }

        if (options.Messages.Count == 0 && string.IsNullOrWhiteSpace(options.UserInput))
            return null;

        return await _sessionStore.CreateAsync(new SessionCreateOptions
        {
            Title = options.SessionTitle ?? options.UserInput ?? options.Messages.LastOrDefault()?.Text ?? "session",
            Metadata = options.SessionMetadata,
        }, ct).ConfigureAwait(false);
    }

    private async Task<List<ChatMessage>> BuildMessageHistoryAsync(AgentLoopOptions options, SessionId? sessionId, CancellationToken ct)
    {
        var history = new List<ChatMessage>();
        if (sessionId.HasValue && _sessionTranscript is not null)
        {
            await foreach (var message in _sessionTranscript.ReadAsync(sessionId.Value, ct).ConfigureAwait(false))
                history.Add(message);
        }

        history.AddRange(options.Messages);

        if (!string.IsNullOrWhiteSpace(options.UserInput))
            history.Add(new ChatMessage(ChatRole.User, options.UserInput));

        return history;
    }

    private async Task<IAgent> ResolveAgentAsync(AgentLoopOptions options, CancellationToken ct)
    {
        if (options.Agent is not null)
            return options.Agent;

        if (options.AgentDefinition is null)
            throw new InvalidOperationException("AgentLoopOptions requires either Agent or AgentDefinition.");

        return await _agentPool.SpawnAsync(options.AgentDefinition, ct).ConfigureAwait(false);
    }

    private async Task UpdateSessionSnapshotAsync(SessionId sessionId, AgentResult result, CancellationToken ct)
    {
        if (_sessionStore is null)
            return;

        var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
            return;

        var usage = result.TokenUsage;
        await _sessionStore.UpdateAsync(session with
        {
            LastActivityAt = DateTimeOffset.UtcNow,
            CostSnapshot = usage is null && !result.EstimatedCost.HasValue
                ? session.CostSnapshot
                : new SessionCostSnapshot(
                    usage?.TotalInputTokens ?? session.CostSnapshot?.InputTokens ?? 0,
                    usage?.TotalOutputTokens ?? session.CostSnapshot?.OutputTokens ?? 0,
                    usage?.TotalTokens ?? session.CostSnapshot?.TotalTokens ?? 0,
                    result.EstimatedCost ?? session.CostSnapshot?.EstimatedCost),
        }, ct).ConfigureAwait(false);
    }

    private static string ResolveDescription(AgentLoopOptions options, IReadOnlyList<ChatMessage> history)
        => options.UserInput
            ?? history.LastOrDefault(m => m.Role == ChatRole.User)?.Text
            ?? options.Messages.LastOrDefault()?.Text
            ?? "Run agent loop";

    private IAsyncEnumerable<AgentEvent> ExecuteAgentStreamingAsync(
        IAgent agent,
        AgentTask task,
        IAgentContext context,
        CancellationToken ct)
    {
        var builder = new AgentPipelineBuilder();
        foreach (var middleware in _services.GetServices<IAgentMiddleware>())
            builder.Use(middleware);

        var pipeline = builder.BuildStreaming((innerTask, innerContext, innerCt) => agent.ExecuteStreamingAsync(innerTask, innerContext, innerCt));
        return pipeline(task, context, ct);
    }

    private IChatClient ResolveChatClient(AgentLoopOptions options, AgentDefinition? stepDefinition = null)
    {
        var chatClientName = stepDefinition?.ChatClientName ?? options.AgentDefinition?.ChatClientName;
        return chatClientName is not null
            ? _services.GetRequiredKeyedService<IChatClient>(chatClientName)
            : _services.GetRequiredService<IChatClient>();
    }

    private static string? ResolveSystemPrompt(AgentLoopOptions options, AgentDefinition? stepDefinition = null)
        => stepDefinition?.SystemPrompt ?? options.AgentDefinition?.SystemPrompt;

    private static string? ResolveModelId(AgentLoopOptions options, AgentDefinition? stepDefinition = null)
        => stepDefinition?.ModelId ?? stepDefinition?.ChatClientName ?? options.AgentDefinition?.ModelId ?? options.AgentDefinition?.ChatClientName;

    private static ContextWindowOptions ResolveContextWindow(AgentLoopOptions options, AgentDefinition? stepDefinition = null)
        => options.ContextWindow ?? stepDefinition?.ContextWindow ?? options.AgentDefinition?.ContextWindow ?? new ContextWindowOptions();

    private static int CountTurns(IReadOnlyList<ChatMessage> messages)
        => messages.Count(static message => message.Role == ChatRole.User);

    private static LoopStopReason ResolveStopReason(AgentLoopOptions options, AgentResult completedResult)
    {
        if (options.StopWhen?.Invoke(completedResult) == true)
            return LoopStopReason.StopConditionMet;

        return completedResult.Status switch
        {
            AgentResultStatus.BudgetExceeded => LoopStopReason.BudgetExhausted,
            AgentResultStatus.Failed => LoopStopReason.Error,
            _ => LoopStopReason.AgentCompleted,
        };
    }

    private sealed record TurnExecution(AgentResult Result, IReadOnlyList<AgentLoopEvent> Events);

    private sealed class AgentLoopContext : IAgentContext
    {
        private readonly IServiceProvider _services;

        public AgentLoopContext(IAgent agent, IServiceProvider services)
        {
            Agent = agent;
            _services = services;
        }

        public IAgent Agent { get; }

        public IChatClient GetChatClient(string? name = null)
            => name is not null
                ? _services.GetRequiredKeyedService<IChatClient>(name)
                : _services.GetRequiredService<IChatClient>();

        public IToolRegistry Tools => _services.GetRequiredService<IToolRegistry>();
        public IConversationStore? Conversations => _services.GetService<IConversationStore>();
        public IWorkingMemory? WorkingMemory => _services.GetService<IWorkingMemory>();
        public IMessageBus? MessageBus => _services.GetService<IMessageBus>();
        public IApprovalGate? ApprovalGate => _services.GetService<IApprovalGate>();
        public IBudgetTracker? Budget => _services.GetService<IBudgetTracker>();
        public ISecretProvider? Secrets => _services.GetService<ISecretProvider>();
        public CorrelationContext Correlation { get; } = CorrelationContext.New();

        public Task<IAgent> SpawnChildAsync(AgentDefinition definition, CancellationToken ct = default)
        {
            var pool = _services.GetRequiredService<IAgentPool>();
            return pool.SpawnAsync(definition, ct);
        }
    }
}