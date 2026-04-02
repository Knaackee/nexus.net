using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Compaction;
using Nexus.Configuration;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.CostTracking;
using Nexus.Permissions;
using Nexus.Protocols.Mcp;
using Nexus.Sessions;
using Nexus.Skills;
using Nexus.Tools.Standard;
using McpServerConfig = Nexus.Protocols.Mcp.McpServerConfig;

namespace Nexus.Cli;

internal sealed class ChatSession : IDisposable
{
    private const string BaseSystemPrompt = "You are Nexus CLI, an interactive coding agent. Be concise, grounded in the current workspace, and use tools only when they improve the answer.";

    private readonly global::Nexus.Defaults.NexusDefaultHost _host;
    private readonly Func<string, IChatClient> _chatClientFactory;
    private readonly StringBuilder _lastResponse = new();
    private readonly IReadOnlyList<McpServerConfig> _mcpServers;
    private readonly SemaphoreSlim _mcpInitializationGate = new(1, 1);
    private readonly List<CliToolActivity> _toolActivity = [];
    private readonly object _activityGate = new();
    private readonly string? _projectRoot;
    private CancellationTokenSource? _cts;
    private SessionId? _sessionId;
    private int _messageCount;
    private SkillDefinition _skill;
    private bool _mcpToolsInitialized;

    public ChatSession(
        string key,
        string model,
        SkillDefinition? skill = null,
        string? projectRoot = null,
        string? sessionStoreDirectory = null,
        SessionId? sessionId = null,
        int messageCount = 0,
        IReadOnlyList<McpServerConfig>? mcpServers = null,
        Func<string, IChatClient>? chatClientFactory = null)
    {
        Key = key;
        Model = model;
        _skill = skill ?? new SkillDefinition { Name = CliSkillCatalog.DefaultSkillName };
        _projectRoot = projectRoot;
        _sessionId = sessionId;
        _messageCount = messageCount;
        _chatClientFactory = chatClientFactory ?? (resolvedModel => new CopilotChatClient(resolvedModel));
        _mcpServers = mcpServers ?? [];

        _host = global::Nexus.Nexus.CreateDefault(_ => _chatClientFactory(model), options =>
        {
            var allowShell = CliApprovalGate.IsShellAllowedFromEnvironment();

            options.ConfigureServices = services =>
            {
                services.AddSingleton<IApprovalGate>(CliApprovalGate.FromEnvironment());
                services.AddFileChangeTracking(tracking =>
                {
                    tracking.BaseDirectory = projectRoot ?? Directory.GetCurrentDirectory();
                });
            };

            options.ConfigurePermissions = permissions => permissions.Configure(permissionOptions =>
            {
                permissionOptions.Rules.Clear();
                permissionOptions.DefaultAction = PermissionAction.Allow;

                if (!allowShell)
                {
                    permissionOptions.Rules.Add(new ToolPermissionRule
                    {
                        Pattern = "shell",
                        Action = PermissionAction.Deny,
                        Reason = "Shell tool execution is disabled in Nexus.Cli by default. Set NEXUS_CLI_ALLOW_SHELL=1 to enable it.",
                    });
                }
            });

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

            if (_mcpServers.Count > 0)
            {
                options.ConfigureMcp = mcp =>
                {
                    foreach (var server in _mcpServers)
                        mcp.AddServer(server);
                };
            }
        });
    }

    public string Key { get; }

    public string Model { get; }

    public SkillDefinition Skill => _skill;

    public string SkillName => _skill.Name;

    public SessionId? PersistedSessionId => _sessionId;

    public ChatSessionState State { get; private set; } = ChatSessionState.Idle;

    public string LastOutput => _lastResponse.ToString();

    public int MessageCount => _messageCount;

    public IReadOnlyList<CliToolActivity> ToolActivity
    {
        get
        {
            lock (_activityGate)
                return _toolActivity.ToArray();
        }
    }

    public event Action<string>? OnChunk;

    public event Action<ChatSession>? OnStateChanged;

    public event Action<CliToolActivity>? OnToolActivity;

    public event Action<TrackedFileChange>? OnFileChanged;

    public void SetSkill(SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (State == ChatSessionState.Running)
            throw new InvalidOperationException("Cannot change the skill of a running chat session.");

        _skill = skill;
    }

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
                var toolNamesByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                    switch (update)
                    {
                        case TextChunkLoopEvent text when !string.IsNullOrEmpty(text.Text):
                            _lastResponse.Append(text.Text);
                            OnChunk?.Invoke(text.Text);
                            break;

                        case ToolCallStartedLoopEvent started:
                            toolNamesByCallId[started.ToolCallId] = started.ToolName;
                            PublishToolActivity(new CliToolActivity(
                                DateTimeOffset.UtcNow,
                                started.ToolName,
                                "started",
                                $"Started {started.ToolName}."));
                            break;

                        case ToolCallProgressLoopEvent progress:
                            PublishToolActivity(new CliToolActivity(
                                DateTimeOffset.UtcNow,
                                toolNamesByCallId.GetValueOrDefault(progress.ToolCallId, "tool"),
                                "progress",
                                progress.Message));
                            break;

                        case ToolCallCompletedLoopEvent toolCompleted:
                            HandleCompletedToolEvent(toolCompleted, toolNamesByCallId);
                            break;

                        case ApprovalRequestedLoopEvent approval:
                            PublishToolActivity(new CliToolActivity(
                                DateTimeOffset.UtcNow,
                                "approval",
                                "approval",
                                approval.Description));
                            break;

                        case TokenUsageLoopEvent usage:
                            var suffix = usage.EstimatedCost.HasValue
                                ? $", ${usage.EstimatedCost.Value:F4}"
                                : string.Empty;
                            PublishToolActivity(new CliToolActivity(
                                DateTimeOffset.UtcNow,
                                "usage",
                                "usage",
                                $"Tokens in/out: {usage.InputTokens}/{usage.OutputTokens}{suffix}"));
                            break;

                        case LoopCompletedEvent completed:
                            finalResult = completed.FinalResult;
                            break;
                    }
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

                _lastResponse.Append(CultureInfo.InvariantCulture, $"[ERROR] {ex.Message}");
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
        var chatClient = _host.Services.GetRequiredService<IChatClient>();

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

    public IReadOnlyList<TrackedFileChange> GetTrackedChanges()
        => _host.Services.GetService<IFileChangeJournal>()?.ListChanges() ?? [];

    public TrackedFileChange? GetTrackedChange(int? changeId = null)
    {
        var journal = _host.Services.GetService<IFileChangeJournal>();
        if (journal is null)
            return null;

        return changeId.HasValue ? journal.GetChange(changeId.Value) : journal.GetLatestChange();
    }

    public Task<FileChangeRevertResult> RevertTrackedChangeAsync(int? changeId = null, CancellationToken ct = default)
    {
        var journal = _host.Services.GetService<IFileChangeJournal>();
        if (journal is null)
            return Task.FromResult(new FileChangeRevertResult(false, "No file-change journal is registered for this session."));

        var target = changeId.HasValue ? journal.GetChange(changeId.Value) : journal.GetLatestChange();
        if (target is null)
            return Task.FromResult(new FileChangeRevertResult(false, "No tracked file change is available."));

        return journal.RevertAsync(target.ChangeId, ct);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mcpInitializationGate.Dispose();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void HandleCompletedToolEvent(ToolCallCompletedLoopEvent completedTool, IReadOnlyDictionary<string, string> toolNamesByCallId)
    {
        var toolName = toolNamesByCallId.GetValueOrDefault(completedTool.ToolCallId, "tool");
        var changeId = TryReadMetadataInt(completedTool.Result.Metadata, "changeId");
        var path = TryReadMetadataString(completedTool.Result.Metadata, "path");
        var message = completedTool.Result.IsSuccess
            ? changeId.HasValue
                ? $"Applied change #{changeId.Value} to {path}."
                : $"Completed {toolName}."
            : completedTool.Result.Error ?? $"{toolName} failed.";

        PublishToolActivity(new CliToolActivity(
            DateTimeOffset.UtcNow,
            toolName,
            completedTool.Result.IsSuccess ? "completed" : "failed",
            message,
            changeId,
            path));

        if (changeId.HasValue)
        {
            var change = GetTrackedChange(changeId.Value);
            if (change is not null)
                OnFileChanged?.Invoke(change);
        }
    }

    private void PublishToolActivity(CliToolActivity activity)
    {
        lock (_activityGate)
        {
            _toolActivity.Add(activity);
            if (_toolActivity.Count > 200)
                _toolActivity.RemoveRange(0, _toolActivity.Count - 200);
        }

        OnToolActivity?.Invoke(activity);
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

    private static int? TryReadMetadataInt(IReadOnlyDictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt32(out var number) => number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }

    private static string? TryReadMetadataString(IReadOnlyDictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => value.ToString(),
        };
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
