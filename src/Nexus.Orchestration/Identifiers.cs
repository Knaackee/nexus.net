using System.Text.Json.Serialization;

namespace Nexus.Orchestration;

[JsonConverter(typeof(TaskGraphIdJsonConverter))]
public readonly record struct TaskGraphId(Guid Value)
{
    public static TaskGraphId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}

internal sealed class TaskGraphIdJsonConverter : JsonConverter<TaskGraphId>
{
    public override TaskGraphId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => new(Guid.Parse(reader.GetString()!));

    public override void Write(System.Text.Json.Utf8JsonWriter writer, TaskGraphId value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString("N"));
}

[JsonConverter(typeof(CheckpointIdJsonConverter))]
public readonly record struct CheckpointId(Guid Value)
{
    public static CheckpointId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}

internal sealed class CheckpointIdJsonConverter : JsonConverter<CheckpointId>
{
    public override CheckpointId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => new(Guid.Parse(reader.GetString()!));

    public override void Write(System.Text.Json.Utf8JsonWriter writer, CheckpointId value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString("N"));
}
