using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core.Agents;

[JsonConverter(typeof(AgentIdJsonConverter))]
public readonly record struct AgentId(Guid Value)
{
    public static AgentId New() => new(Guid.NewGuid());

    public static AgentId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("N")[..8];

    public static implicit operator string(AgentId id) => id.ToString();
}

public sealed class AgentIdJsonConverter : JsonConverter<AgentId>
{
    public override AgentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("AgentId cannot be null.");
        return new AgentId(Guid.Parse(value));
    }

    public override void Write(Utf8JsonWriter writer, AgentId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value.ToString("N"));
    }
}
