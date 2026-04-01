using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Compaction;
using Nexus.Configuration;
using Nexus.Core.Agents;
using Nexus.Core.Tools;
using Nexus.CostTracking;
using Nexus.Protocols.Mcp;
using Nexus.Sessions;
using Nexus.Skills;
using McpServerConfig = Nexus.Protocols.Mcp.McpServerConfig;

namespace Nexus.Cli;

/// <summary>
/// Represents a single chat session running against a Copilot model.
/// Captures streamed output and tracks conversation history.
/// </summary>
internal sealed class ChatSession : IDisposable
{
    private const string BaseSystemPrompt = "You are Nexus CLI, an interactive coding agent. Be concise, grounded in the current workspace, and use tools only when they improve the answer.";

    private readonly global::Nexus.Defaults.NexusDefaultHost _host;
    private readonly StringBuilder _lastResponse = new();
    private CancellationTokenSource? _cts;
    private SessionId? _sessionId;
    private int _messageCount;
    private SkillDefinition _skill;
    private readonly string? _projectRoot;
    private readonly IReadOnlyList<McpServerConfig> _mcpServers;
    private readonly SemaphoreSlim _mcpInitializationGate = new(1, 1);
    private bool _mcpToolsInitialized;

    public string Key { get; }
    public string Model { get; }
    public SkillDefinition Skill => _skill;
    public string SkillName => _skill.Name;
    public SessionId? PersistedSessionId => _sessionId;
    public ChatSessionState State { get; private set; } = ChatSessionState.Idle;
    public string LastOutput => _lastResponse.ToString();
    public int MessageCount => _messageCount;

    /// <summary>Raised for each streamed text chunk so the UI can update in real time.</summary>
    public event Action<string>? OnChunk;

    /// <summary>Raised when a run completes or fails.</summary>
    public event Action<ChatSession>? OnStateChanged;

    public ChatSession(
        string key,
        string model,
        SkillDefinition? skill = null,
        string? projectRoot = null,
        string? sessionStoreDirectory = null,
        SessionId? sessionId = null,
        int messageCount = 0,
        IReadOnlyList<McpServerConfig>? _mcpServers = null)
    {
        Key = key;
        Model = model;
        _skill = skill ?? new SkillDefinition { Name = CliSkillCatalog.DefaultSkillName };
        _projectRoot = projectRoot;
        _sessionId = sessionId;
        _messageCount = messageCount;
        this._mcpServers = _mcpServers ?? [];
        _host = global::Nexus.Nexus.CreateDefault(_ => new CopilotChatClient(model), options =>
        {
            options.SessionTitle = key;
            options.DefaultAgentDefinition = new AgentDefinition
            {
                Name = "NexusCli",
                ModelId = model,
                SystemPrompt = BaseSystemPrompt,
            };

            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                options.ConfigureConfiguration = configuration => configuration.SetProjectRoot(projectRoot);
                options.ConfigureTools = tools => tools.Configure(toolOptions =>
                {
                    toolOptions.BaseDirectory = projectRoot;
                    toolOptions.WorkingDirectory = projectRoot;
                });
            }

            if (!string.IsNullOrWhiteSpace(sessionStoreDirectory))
                options.ConfigureSessions = sessions => sessions.UseFileSystem(sessionStoreDirectory);

            if (this._mcpServers.Count > 0)
            {
                options.ConfigureMcp = mcp =>
                {
                    foreach (var server in this._mcpServers)
                        mcp.AddServer(server);
                };
            }
        });
    }

    public void SetSkill(SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (State == ChatSessionState.Running)
            throw new InvalidOperationException("Cannot change the skill of a running chat session.");

        _skill = skill;
    }

    /// <summary>Sends a user message and starts streaming the response in the background.</summary>
    public void Send(string userMessage)
    {
        if (State == ChatSessionState.Running)
            return;

        _lastResponse.Clear();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        State = ChatSessionState.Running;
        OnStateChanged?.Invoke(this);

        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureMcpToolsAsync(token).ConfigureAwait(false);

                AgentResult? finalResult = null;
                await foreach (var update in _host.RunAsync(new AgentLoopOptions
                {
                    SessionId = _sessionId,
                    SessionTitle = Key,
                    SessionMetadata = BuildSessionMetadata(),
                    UserInput = userMessage,
                    AgentDefinition = _skill.ApplyTo(new AgentDefinition
                    {
                        Name = $"cli-{Key}",
                        ModelId = Model,
                        SystemPrompt = BaseSystemPrompt,
                    }),
                }, token).ConfigureAwait(false))
                {
                    if (update.SessionId.HasValue)
                        _sessionId = update.SessionId.Value;

                    if (update is TextChunkLoopEvent text && !string.IsNullOrEmpty(text.Text))
                    {
                        _lastResponse.Append(text.Text);
                        OnChunk?.Invoke(text.Text);
                    }

                    if (update is LoopCompletedEvent completed)
                        finalResult = completed.FinalResult;
                }

                finalResult ??= AgentResult.Success(_lastResponse.ToString());

                if (_lastResponse.Length == 0 && !string.IsNullOrWhiteSpace(finalResult.Text))
                    _lastResponse.Append(finalResult.Text);

                await RefreshMessageCountAsync(token).ConfigureAwait(false);
                State = finalResult.Status == AgentResultStatus.Success
                    ? ChatSessionState.Idle
                    : ChatSessionState.Failed;
            }
            catch (OperationCanceledException)
            {
                State = ChatSessionState.Idle;
            }
            catch (Exception ex)
            {
                if (_lastResponse.Length > 0)
                    _lastResponse.AppendLine();

                _lastResponse.Append(System.Globalization.CultureInfo.InvariantCulture, $"[ERROR] {ex.Message}");
                State = ChatSessionState.Failed;
            }

            OnStateChanged?.Invoke(this);
        }, CancellationToken.None);
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (State == ChatSessionState.Running)
            throw new InvalidOperationException("Cannot clear a running chat session.");

        if (_sessionId.HasValue)
        {
            var store = _host.Services.GetService<ISessionStore>();
            if (store is not null)
                await store.DeleteAsync(_sessionId.Value, ct).ConfigureAwait(false);
        }

        _sessionId = null;
        _messageCount = 0;
        _lastResponse.Clear();
        State = ChatSessionState.Idle;
    }

    public async Task<SessionInfo?> GetSessionInfoAsync(CancellationToken ct = default)
    {
        if (!_sessionId.HasValue)
            return null;

        var store = _host.Services.GetService<ISessionStore>();
        if (store is null)
            return null;

        return await store.GetAsync(_sessionId.Value, ct).ConfigureAwait(false);
    }

    public async Task<CostTrackingSnapshot?> GetCostSnapshotAsync(CancellationToken ct = default)
    {
        var tracker = _host.Services.GetService<ICostTracker>();
        if (tracker is null)
            return null;

        return await tracker.GetSnapshotAsync(ct).ConfigureAwait(false);
    }

    public async Task<ManualCompactionResult?> CompactAsync(CancellationToken ct = default)
    {
        if (State == ChatSessionState.Running)
            throw new InvalidOperationException("Cannot compact a running chat session.");

        if (!_sessionId.HasValue)
            return null;

        var transcript = _host.Services.GetService<ISessionTranscript>();
        var compaction = _host.Services.GetService<ICompactionService>();
        if (transcript is null || compaction is null)
            return null;

        var messages = new List<ChatMessage>();
        await foreach (var message in transcript.ReadAsync(_sessionId.Value, ct).ConfigureAwait(false))
            messages.Add(message);

        if (messages.Count < 2)
            return null;

        var definition = _skill.ApplyTo(new AgentDefinition
        {
            Name = $"cli-{Key}",
            ModelId = Model,
            SystemPrompt = BaseSystemPrompt,
        });

        var contextWindow = definition.ContextWindow ?? new ContextWindowOptions();
        var systemPrompt = definition.SystemPrompt;
        var modelId = definition.ModelId ?? Model;
        var chatClient = _host.Services.GetRequiredService<Microsoft.Extensions.AI.IChatClient>();

        var result = await compaction.CompactAsync(messages, contextWindow, chatClient, systemPrompt, modelId, ct).ConfigureAwait(false);
        if (result.CompactedMessages.Count == messages.Count && result.TokensAfter >= result.TokensBefore)
            return new ManualCompactionResult(result.StrategyUsed, result.TokensBefore, result.TokensAfter, messages.Count, result.CompactedMessages.Count, false);

        var activeMessages = result.CompactedMessages;
        var recall = _host.Services.GetService<ICompactionRecallService>();
        if (recall is not null)
        {
            var recallResult = await recall.RecallAsync(new CompactionRecallContext
            {
                OriginalMessages = messages,
                ActiveMessages = result.CompactedMessages,
                Compaction = result,
                WindowOptions = contextWindow,
                SystemPrompt = systemPrompt,
                ModelId = modelId,
            }, ct).ConfigureAwait(false);

            activeMessages = recallResult.Messages;
            if (compaction.ShouldCompact(activeMessages, contextWindow, systemPrompt, modelId))
                throw new InvalidOperationException("Post-compaction recall exceeded the active context window.");
        }

        await transcript.ReplaceAsync(_sessionId.Value, activeMessages, ct).ConfigureAwait(false);
        _messageCount = activeMessages.Count;

        return new ManualCompactionResult(result.StrategyUsed, result.TokensBefore, result.TokensAfter, messages.Count, activeMessages.Count, true);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mcpInitializationGate.Dispose();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task EnsureMcpToolsAsync(CancellationToken ct)
    {
        if (_mcpToolsInitialized || _mcpServers.Count == 0)
            return;

        await _mcpInitializationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_mcpToolsInitialized)
                return;

            var registry = _host.Services.GetService<IToolRegistry>();
            var mcpHostManager = _host.Services.GetService<IMcpHostManager>();
            if (registry is null || mcpHostManager is null)
                return;

            var functions = await mcpHostManager.DiscoverFunctionsAsync(ct).ConfigureAwait(false);
            foreach (var function in functions)
                registry.Register(function);

            _mcpToolsInitialized = true;
        }
        finally
        {
            _mcpInitializationGate.Release();
        }
    }

    private async Task RefreshMessageCountAsync(CancellationToken ct)
    {
        if (!_sessionId.HasValue)
            return;

        var store = _host.Services.GetService<ISessionStore>();
        if (store is null)
            return;

        var session = await store.GetAsync(_sessionId.Value, ct).ConfigureAwait(false);
        if (session is not null)
            _messageCount = session.MessageCount;
    }

    private Dictionary<string, string> BuildSessionMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = Key,
            ["model"] = Model,
            ["skill"] = _skill.Name,
        };

        if (!string.IsNullOrWhiteSpace(_projectRoot))
            metadata["projectRoot"] = _projectRoot;

        return metadata;
    }
}

internal enum ChatSessionState
{
    Idle,
    Running,
    Failed,
}

internal sealed record ManualCompactionResult(
    string StrategyUsed,
    int TokensBefore,
    int TokensAfter,
    int MessagesBefore,
    int MessagesAfter,
    bool Applied);
