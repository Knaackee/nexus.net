using System.Collections.Concurrent;
using Nexus.Protocols.Mcp;
using Nexus.Sessions;
using Nexus.Skills;

namespace Nexus.Cli;

/// <summary>
/// Manages multiple parallel chat sessions keyed by user-chosen name.
/// </summary>
internal sealed class ChatManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISkillCatalog _skills;
    private readonly string _defaultSkillName;
    private readonly string? _projectRoot;
    private readonly string? _sessionStorePath;
    private readonly IReadOnlyList<McpServerConfig> _mcpServers;
    private string? _activeKey;

    public ChatManager(
        ISkillCatalog? skills = null,
        string defaultSkillName = CliSkillCatalog.DefaultSkillName,
        string? projectRoot = null,
        string? sessionStorePath = null,
        IReadOnlyList<McpServerConfig>? mcpServers = null)
    {
        _skills = skills ?? CliSkillCatalog.CreateDefaultCatalog();
        _defaultSkillName = defaultSkillName;
        _projectRoot = projectRoot;
        _sessionStorePath = sessionStorePath;
        _mcpServers = mcpServers ?? [];
    }

    public string? ActiveKey => _activeKey;
    public ChatSession? ActiveSession => _activeKey is not null && _sessions.TryGetValue(_activeKey, out var s) ? s : null;
    public IReadOnlyCollection<ChatSession> Sessions => _sessions.Values.ToList();

    public ChatSession Add(string key, string model, SkillDefinition? skill = null)
    {
        var session = new ChatSession(key, model, skill ?? ResolveDefaultSkill(), _projectRoot, _sessionStorePath, _mcpServers: _mcpServers);

        if (!_sessions.TryAdd(key, session))
        {
            session.Dispose();
            throw new InvalidOperationException($"A chat with key '{key}' already exists.");
        }

        _activeKey ??= key;
        return session;
    }

    public bool SetSkill(string key, SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var session = Get(key);
        if (session is null)
            return false;

        session.SetSkill(skill);
        return true;
    }

    public ChatSession? Replace(string key, string model, SkillDefinition? skill = null)
    {
        if (!_sessions.TryGetValue(key, out var existing))
            return null;

        if (existing.State == ChatSessionState.Running)
            throw new InvalidOperationException("Cannot replace a running chat session.");

        var replacement = new ChatSession(key, model, skill ?? existing.Skill, _projectRoot, _sessionStorePath, _mcpServers: _mcpServers);
        _sessions[key] = replacement;
        existing.Dispose();

        if (_activeKey is null)
            _activeKey = key;

        return replacement;
    }

    public bool Remove(string key)
    {
        if (!_sessions.TryRemove(key, out var session))
            return false;

        session.Dispose();

        if (string.Equals(_activeKey, key, StringComparison.OrdinalIgnoreCase))
            _activeKey = _sessions.Keys.FirstOrDefault();

        return true;
    }

    public bool Switch(string key)
    {
        if (!_sessions.ContainsKey(key))
            return false;

        _activeKey = key;
        return true;
    }

    public ChatSession? Get(string key) =>
        _sessions.TryGetValue(key, out var s) ? s : null;

    public async Task<ChatSession?> ResumeLatestAsync(string? preferredKey = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_sessionStorePath) || !Directory.Exists(_sessionStorePath))
            return null;

        using var store = new FileSessionStore(_sessionStorePath);
        SessionInfo? selected = null;
        await foreach (var session in store.ListAsync(new SessionFilter(), ct).ConfigureAwait(false))
        {
            if (_sessions.Values.Any(existing => existing.PersistedSessionId == session.Id))
                continue;

            selected = session;
            break;
        }

        if (selected is null)
            return null;

        var key = BuildKey(preferredKey, selected);
        if (_sessions.TryGetValue(key, out var existingSession))
        {
            _activeKey = existingSession.Key;
            return existingSession;
        }

        var model = selected.Metadata.TryGetValue("model", out var storedModel) && !string.IsNullOrWhiteSpace(storedModel)
            ? storedModel
            : CopilotChatClient.AvailableModels[0];
        var skill = selected.Metadata.TryGetValue("skill", out var storedSkill)
            ? _skills.Resolve(storedSkill)
            : null;

        var resumed = new ChatSession(
            key,
            model,
            skill ?? ResolveDefaultSkill(),
            _projectRoot,
            _sessionStorePath,
            selected.Id,
            selected.MessageCount,
            _mcpServers);

        if (!_sessions.TryAdd(key, resumed))
        {
            resumed.Dispose();
            return _sessions[key];
        }

        _activeKey = key;
        return resumed;
    }

    private SkillDefinition ResolveDefaultSkill()
    {
        var configured = _skills.Resolve(_defaultSkillName);
        if (configured is not null)
            return configured;

        var available = _skills.ListAll();
        if (available.Count > 0)
            return available[0];

        return new SkillDefinition { Name = "default" };
    }

    private string BuildKey(string? preferredKey, SessionInfo session)
    {
        var baseKey = !string.IsNullOrWhiteSpace(preferredKey)
            ? preferredKey
            : session.Metadata.TryGetValue("key", out var storedKey) && !string.IsNullOrWhiteSpace(storedKey)
                ? storedKey
                : session.Title;

        if (!_sessions.ContainsKey(baseKey))
            return baseKey;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseKey}-{suffix}";
            if (!_sessions.ContainsKey(candidate))
                return candidate;
        }

        return $"{baseKey}-{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();

        _sessions.Clear();
    }
}
