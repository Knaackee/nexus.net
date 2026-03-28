using System.Collections.Concurrent;

namespace Nexus.Cli;

/// <summary>
/// Manages multiple parallel chat sessions keyed by user-chosen name.
/// </summary>
internal sealed class ChatManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeKey;

    public string? ActiveKey => _activeKey;
    public ChatSession? ActiveSession => _activeKey is not null && _sessions.TryGetValue(_activeKey, out var s) ? s : null;
    public IReadOnlyCollection<ChatSession> Sessions => _sessions.Values.ToList();

    public ChatSession Add(string key, string model)
    {
        var session = new ChatSession(key, model);

        if (!_sessions.TryAdd(key, session))
        {
            session.Dispose();
            throw new InvalidOperationException($"A chat with key '{key}' already exists.");
        }

        _activeKey ??= key;
        return session;
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

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();

        _sessions.Clear();
    }
}
