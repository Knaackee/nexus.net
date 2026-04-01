using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Nexus.Sessions;

public sealed class InMemorySessionStore : ISessionStore, ISessionTranscript
{
    private readonly ConcurrentDictionary<SessionId, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<SessionId, List<ChatMessage>> _transcripts = new();

    public Task<SessionInfo> CreateAsync(SessionCreateOptions options, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new SessionInfo
        {
            Id = SessionId.New(),
            Title = options.Title,
            CreatedAt = now,
            LastActivityAt = now,
            MessageCount = 0,
            Metadata = new Dictionary<string, string>(options.Metadata, StringComparer.OrdinalIgnoreCase),
        };

        _sessions[session.Id] = session;
        _transcripts[session.Id] = [];
        return Task.FromResult(session);
    }

    public Task<SessionInfo?> GetAsync(SessionId id, CancellationToken ct = default)
    {
        _sessions.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public async IAsyncEnumerable<SessionInfo> ListAsync(
        SessionFilter? filter = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var session in ApplyFilter(_sessions.Values, filter))
        {
            ct.ThrowIfCancellationRequested();
            yield return session;
            await Task.Yield();
        }
    }

    public Task UpdateAsync(SessionInfo session, CancellationToken ct = default)
    {
        _sessions[session.Id] = session;
        _transcripts.TryAdd(session.Id, []);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(SessionId id, CancellationToken ct = default)
    {
        var removed = _sessions.TryRemove(id, out _);
        _transcripts.TryRemove(id, out _);
        return Task.FromResult(removed);
    }

    public Task AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken ct = default)
    {
        var transcript = _transcripts.GetOrAdd(sessionId, _ => []);
        lock (transcript)
        {
            transcript.Add(message);
        }

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with
            {
                MessageCount = session.MessageCount + 1,
                LastActivityAt = DateTimeOffset.UtcNow,
            };
        }

        return Task.CompletedTask;
    }

    public Task ReplaceAsync(SessionId sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        var transcript = _transcripts.GetOrAdd(sessionId, _ => []);
        lock (transcript)
        {
            transcript.Clear();
            transcript.AddRange(messages);
        }

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with
            {
                MessageCount = messages.Count,
                LastActivityAt = DateTimeOffset.UtcNow,
            };
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ChatMessage> ReadAsync(
        SessionId sessionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_transcripts.TryGetValue(sessionId, out var transcript))
            yield break;

        List<ChatMessage> snapshot;
        lock (transcript)
        {
            snapshot = [.. transcript];
        }

        foreach (var message in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return message;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<ChatMessage> ReadLastAsync(
        SessionId sessionId,
        int count,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_transcripts.TryGetValue(sessionId, out var transcript))
            yield break;

        List<ChatMessage> snapshot;
        lock (transcript)
        {
            snapshot = [.. transcript.TakeLast(count)];
        }

        foreach (var message in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return message;
            await Task.Yield();
        }
    }

    private static IEnumerable<SessionInfo> ApplyFilter(IEnumerable<SessionInfo> sessions, SessionFilter? filter)
    {
        var query = sessions;
        if (!string.IsNullOrWhiteSpace(filter?.SearchText))
        {
            query = query.Where(s =>
                s.Title.Contains(filter.SearchText, StringComparison.OrdinalIgnoreCase)
                || s.Metadata.Values.Any(v => v.Contains(filter.SearchText, StringComparison.OrdinalIgnoreCase)));
        }

        query = filter?.OrderBy switch
        {
            SessionOrderBy.CreatedAtDescending => query.OrderByDescending(s => s.CreatedAt),
            SessionOrderBy.TitleAscending => query.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(s => s.LastActivityAt ?? s.CreatedAt),
        };

        if (filter?.Limit is int limit)
            query = query.Take(limit);

        return query;
    }
}