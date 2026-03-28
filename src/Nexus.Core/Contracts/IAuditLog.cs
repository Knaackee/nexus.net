using System.Text.Json;
using Nexus.Core.Agents;

namespace Nexus.Core.Contracts;

public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
    IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

public record AuditEntry(
    DateTimeOffset Timestamp,
    string Action,
    AgentId AgentId,
    string? UserId = null,
    string? CorrelationId = null,
    JsonElement? Details = null,
    AuditSeverity Severity = AuditSeverity.Info);

public record AuditQuery
{
    public AgentId? AgentId { get; init; }
    public string? UserId { get; init; }
    public string? Action { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int MaxResults { get; init; } = 100;
}

public enum AuditSeverity { Debug, Info, Warning, Error, Critical }

public class NullAuditLog : IAuditLog
{
    public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;

    public async IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
