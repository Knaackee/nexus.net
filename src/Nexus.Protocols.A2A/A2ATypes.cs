using System.Text.Json;

namespace Nexus.Protocols.A2A;

/// <summary>An A2A Agent Card describing a remote agent's capabilities.</summary>
public record AgentCard
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Uri Endpoint { get; init; }
    public required string Version { get; init; }
    public IReadOnlyList<AgentSkill> Skills { get; init; } = [];
    public IReadOnlySet<string> SupportedModalities { get; init; } = new HashSet<string> { "text" };
    public A2AAuthRequirements? Auth { get; init; }
    public string? Signature { get; init; }
}

public record AgentSkill
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public record A2AAuthRequirements
{
    public IReadOnlyList<string> Schemes { get; init; } = ["Bearer"];
    public IReadOnlyList<string> RequiredScopes { get; init; } = [];
}

/// <summary>A2A task request sent to a remote agent.</summary>
public record A2ATaskRequest
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required IReadOnlyList<A2AMessage> Messages { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>A message in the A2A protocol.</summary>
public record A2AMessage
{
    public required string Role { get; init; }
    public required IReadOnlyList<A2AMessagePart> Parts { get; init; }
}

/// <summary>Base type for message parts.</summary>
public abstract record A2AMessagePart;

public record A2ATextPart(string Text) : A2AMessagePart;
public record A2AFilePart(string Name, string MimeType, byte[] Data) : A2AMessagePart;
public record A2ADataPart(string MimeType, JsonElement Data) : A2AMessagePart;

/// <summary>A2A task status.</summary>
public enum A2ATaskStatus
{
    Submitted,
    Working,
    InputRequired,
    Completed,
    Canceled,
    Failed,
}

/// <summary>A2A task with status.</summary>
public record A2ATask
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required A2ATaskStatus Status { get; init; }
    public IReadOnlyList<A2AMessage> Messages { get; init; } = [];
    public IReadOnlyList<A2AArtifact> Artifacts { get; init; } = [];
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>An output artifact from a task.</summary>
public record A2AArtifact
{
    public required string Name { get; init; }
    public required IReadOnlyList<A2AMessagePart> Parts { get; init; }
    public int? Index { get; init; }
}

/// <summary>A2A task update (for streaming).</summary>
public record A2ATaskUpdate
{
    public required string TaskId { get; init; }
    public required A2ATaskStatus Status { get; init; }
    public A2AMessage? Message { get; init; }
    public A2AArtifact? Artifact { get; init; }
    public bool IsFinal { get; init; }
}
