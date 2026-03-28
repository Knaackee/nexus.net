using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core.Agents;

[JsonConverter(typeof(TaskIdJsonConverter))]
public readonly record struct TaskId(Guid Value)
{
    public static TaskId New() => new(Guid.NewGuid());

    public static TaskId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("N")[..8];
}

public sealed class TaskIdJsonConverter : JsonConverter<TaskId>
{
    public override TaskId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("TaskId cannot be null.");
        return new TaskId(Guid.Parse(value));
    }

    public override void Write(Utf8JsonWriter writer, TaskId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value.ToString("N"));
    }

    public override TaskId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("TaskId property name cannot be null.");
        return new TaskId(Guid.Parse(value));
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TaskId value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.Value.ToString("N"));
    }
}
