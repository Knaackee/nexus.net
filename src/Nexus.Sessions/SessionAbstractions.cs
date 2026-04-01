using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Nexus.Sessions;

public interface ISessionStore
{
    Task<SessionInfo> CreateAsync(SessionCreateOptions options, CancellationToken ct = default);
    Task<SessionInfo?> GetAsync(SessionId id, CancellationToken ct = default);
    IAsyncEnumerable<SessionInfo> ListAsync(SessionFilter? filter = null, CancellationToken ct = default);
    Task UpdateAsync(SessionInfo session, CancellationToken ct = default);
    Task<bool> DeleteAsync(SessionId id, CancellationToken ct = default);
}

public interface ISessionTranscript
{
    Task AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken ct = default);
    Task ReplaceAsync(SessionId sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    IAsyncEnumerable<ChatMessage> ReadAsync(SessionId sessionId, CancellationToken ct = default);
    IAsyncEnumerable<ChatMessage> ReadLastAsync(SessionId sessionId, int count, CancellationToken ct = default);
}

public sealed record SessionCreateOptions
{
    public required string Title { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record SessionFilter
{
    public string? SearchText { get; init; }
    public int? Limit { get; init; }
    public SessionOrderBy OrderBy { get; init; } = SessionOrderBy.LastActivityDescending;
}

public enum SessionOrderBy
{
    CreatedAtDescending,
    LastActivityDescending,
    TitleAscending,
}

public sealed record SessionCostSnapshot(int InputTokens, int OutputTokens, int TotalTokens, decimal? EstimatedCost);

public sealed record SessionInfo
{
    public required SessionId Id { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
    public int MessageCount { get; init; }
    public SessionCostSnapshot? CostSnapshot { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.NewGuid());

    public static SessionId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("N");
}

public sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    public override SessionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("SessionId cannot be null.");
        return new SessionId(Guid.Parse(value));
    }

    public override void Write(Utf8JsonWriter writer, SessionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString("N"));
}