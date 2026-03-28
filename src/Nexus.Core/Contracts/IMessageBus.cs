using Nexus.Core.Agents;

namespace Nexus.Core.Contracts;

/// <summary>Forward declaration in Core; full implementation lives in Nexus.Messaging.</summary>
public interface IMessageBus
{
    Task SendAsync(AgentId target, AgentMessage message, CancellationToken ct = default);
    Task PublishAsync(string topic, AgentMessage message, CancellationToken ct = default);
    Task<AgentMessage> RequestAsync(AgentId target, AgentMessage request, TimeSpan timeout, CancellationToken ct = default);
    IDisposable Subscribe(AgentId subscriber, string topic, Func<AgentMessage, Task> handler);
    Task BroadcastAsync(AgentMessage message, CancellationToken ct = default);
}

[System.Text.Json.Serialization.JsonConverter(typeof(MessageIdJsonConverter))]
public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}

internal sealed class MessageIdJsonConverter : System.Text.Json.Serialization.JsonConverter<MessageId>
{
    public override MessageId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => new(Guid.Parse(reader.GetString()!));

    public override void Write(System.Text.Json.Utf8JsonWriter writer, MessageId value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString("N"));
}

public record AgentMessage
{
    public required MessageId Id { get; init; }
    public required AgentId Sender { get; init; }
    public required string Type { get; init; }
    public required object Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public MessageId? CorrelationId { get; init; }
}
