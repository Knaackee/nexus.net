using Microsoft.Extensions.AI;
using Nexus.Core.Agents;

namespace Nexus.Core.Contracts;

/// <summary>Forward declaration in Core; full implementation lives in Nexus.Memory.</summary>
public interface IConversationStore
{
    Task<ConversationId> CreateAsync(string? threadId = null, CancellationToken ct = default);
    Task AppendAsync(ConversationId id, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ConversationId id, int? maxMessages = null, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetWindowAsync(ConversationId id, int maxTokens, ContextTrimStrategy strategy, CancellationToken ct = default);
    Task<ConversationId> ForkAsync(ConversationId parentId, Func<ChatMessage, bool>? filter = null, CancellationToken ct = default);
}

[System.Text.Json.Serialization.JsonConverter(typeof(ConversationIdJsonConverter))]
public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}

internal sealed class ConversationIdJsonConverter : System.Text.Json.Serialization.JsonConverter<ConversationId>
{
    public override ConversationId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => new(Guid.Parse(reader.GetString()!));

    public override void Write(System.Text.Json.Utf8JsonWriter writer, ConversationId value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString("N"));
}
