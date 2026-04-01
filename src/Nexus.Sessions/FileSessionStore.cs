using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Nexus.Sessions;

public sealed class FileSessionStore : ISessionStore, ISessionTranscript, IDisposable
{
    private const string IndexFileName = "sessions.json";
    private const string TranscriptFileName = "transcript.jsonl";

    private readonly string _baseDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly JsonSerializerOptions _jsonLineOptions = new(JsonSerializerDefaults.Web);

    public FileSessionStore(string baseDirectory)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        Directory.CreateDirectory(_baseDirectory);
    }

    public async Task<SessionInfo> CreateAsync(SessionCreateOptions options, CancellationToken ct = default)
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

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessions = await LoadIndexUnsafeAsync(ct).ConfigureAwait(false);
            sessions.Add(session);
            Directory.CreateDirectory(GetSessionDirectory(session.Id));
            await SaveIndexUnsafeAsync(sessions, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return session;
    }

    public async Task<SessionInfo?> GetAsync(SessionId id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessions = await LoadIndexUnsafeAsync(ct).ConfigureAwait(false);
            return sessions.FirstOrDefault(s => s.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<SessionInfo> ListAsync(
        SessionFilter? filter = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<SessionInfo> sessions;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            sessions = ApplyFilter(await LoadIndexUnsafeAsync(ct).ConfigureAwait(false), filter).ToList();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            yield return session;
            await Task.Yield();
        }
    }

    public async Task UpdateAsync(SessionInfo session, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessions = await LoadIndexUnsafeAsync(ct).ConfigureAwait(false);
            var index = sessions.FindIndex(s => s.Id == session.Id);
            if (index < 0)
                throw new KeyNotFoundException($"Session {session.Id} not found.");

            sessions[index] = session;
            await SaveIndexUnsafeAsync(sessions, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(SessionId id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessions = await LoadIndexUnsafeAsync(ct).ConfigureAwait(false);
            var removed = sessions.RemoveAll(s => s.Id == id) > 0;
            if (!removed)
                return false;

            await SaveIndexUnsafeAsync(sessions, ct).ConfigureAwait(false);
            var sessionDirectory = GetSessionDirectory(id);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken ct = default)
    {
        var transcriptPath = GetTranscriptPath(sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);

        var line = JsonSerializer.Serialize(PersistedChatMessage.From(message), _jsonLineOptions);
        await File.AppendAllTextAsync(transcriptPath, line + Environment.NewLine, Encoding.UTF8, ct).ConfigureAwait(false);

        var session = await GetAsync(sessionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        await UpdateAsync(session with
        {
            MessageCount = session.MessageCount + 1,
            LastActivityAt = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);
    }

    public async Task ReplaceAsync(SessionId sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessions = await LoadIndexUnsafeAsync(ct).ConfigureAwait(false);
            var index = sessions.FindIndex(s => s.Id == sessionId);
            if (index < 0)
                throw new KeyNotFoundException($"Session {sessionId} not found.");

            var transcriptPath = GetTranscriptPath(sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);

            await using (var stream = File.Create(transcriptPath))
            await using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                foreach (var message in messages)
                {
                    var line = JsonSerializer.Serialize(PersistedChatMessage.From(message), _jsonLineOptions);
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }
            }

            sessions[index] = sessions[index] with
            {
                MessageCount = messages.Count,
                LastActivityAt = DateTimeOffset.UtcNow,
            };

            await SaveIndexUnsafeAsync(sessions, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<ChatMessage> ReadAsync(
        SessionId sessionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var transcriptPath = GetTranscriptPath(sessionId);
        if (!File.Exists(transcriptPath))
            yield break;

        await foreach (var line in File.ReadLinesAsync(transcriptPath, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var record = JsonSerializer.Deserialize<PersistedChatMessage>(line, _jsonLineOptions);
            if (record is not null)
                yield return record.ToChatMessage();
        }
    }

    public async IAsyncEnumerable<ChatMessage> ReadLastAsync(
        SessionId sessionId,
        int count,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var transcriptPath = GetTranscriptPath(sessionId);
        if (!File.Exists(transcriptPath))
            yield break;

        var lines = await File.ReadAllLinesAsync(transcriptPath, ct).ConfigureAwait(false);
        foreach (var line in lines.Where(static l => !string.IsNullOrWhiteSpace(l)).TakeLast(count))
        {
            var record = JsonSerializer.Deserialize<PersistedChatMessage>(line, _jsonLineOptions);
            if (record is not null)
                yield return record.ToChatMessage();
        }
    }

    private async Task<List<SessionInfo>> LoadIndexUnsafeAsync(CancellationToken ct)
    {
        var indexPath = GetIndexPath();
        if (!File.Exists(indexPath))
            return [];

        await using var stream = File.OpenRead(indexPath);
        var sessions = await JsonSerializer.DeserializeAsync<List<SessionInfo>>(stream, _jsonOptions, ct).ConfigureAwait(false);
        return sessions ?? [];
    }

    private async Task SaveIndexUnsafeAsync(List<SessionInfo> sessions, CancellationToken ct)
    {
        var indexPath = GetIndexPath();
        await using var stream = File.Create(indexPath);
        await JsonSerializer.SerializeAsync(stream, sessions, _jsonOptions, ct).ConfigureAwait(false);
    }

    private string GetIndexPath() => Path.Combine(_baseDirectory, IndexFileName);

    private string GetSessionDirectory(SessionId sessionId) => Path.Combine(_baseDirectory, sessionId.ToString());

    private string GetTranscriptPath(SessionId sessionId) => Path.Combine(GetSessionDirectory(sessionId), TranscriptFileName);

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

    private sealed record PersistedChatMessage(string Role, string? Text)
    {
        public static PersistedChatMessage From(ChatMessage message)
            => new(message.Role.Value, message.Text);

        public ChatMessage ToChatMessage()
            => new(ParseRole(Role), Text ?? string.Empty);

        private static ChatRole ParseRole(string role)
            => role.ToLowerInvariant() switch
            {
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                "tool" => ChatRole.Tool,
                _ => ChatRole.User,
            };
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}