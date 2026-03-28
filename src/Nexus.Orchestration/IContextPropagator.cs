using Nexus.Core.Agents;

namespace Nexus.Orchestration;

public interface IContextPropagator
{
    Task<PropagatedContext> ExtractAsync(
        AgentResult result, AgentTask nextTask,
        int maxTokens, CancellationToken ct = default);
}

public record PropagatedContext
{
    public required string Summary { get; init; }
    public IReadOnlyDictionary<string, object> StructuredData { get; init; } = new Dictionary<string, object>();
    public IReadOnlyList<ArtifactReference> Artifacts { get; init; } = [];
    public int EstimatedTokens { get; init; }
}

public record ArtifactReference(string Name, string Uri, string MimeType);
