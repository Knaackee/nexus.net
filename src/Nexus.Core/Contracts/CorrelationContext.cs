namespace Nexus.Core.Contracts;

public record CorrelationContext
{
    public required string TraceId { get; init; }
    public required string SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string? ThreadId { get; init; }
    public string? UserId { get; init; }
    public IDictionary<string, string> Baggage { get; init; } = new Dictionary<string, string>();

    public static CorrelationContext New(string? threadId = null, string? userId = null) => new()
    {
        TraceId = Guid.NewGuid().ToString("N"),
        SpanId = Guid.NewGuid().ToString("N")[..16],
        ThreadId = threadId,
        UserId = userId
    };

    public CorrelationContext CreateChild() => this with
    {
        ParentSpanId = SpanId,
        SpanId = Guid.NewGuid().ToString("N")[..16]
    };
}
