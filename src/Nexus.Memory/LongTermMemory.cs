namespace Nexus.Memory;

public interface ILongTermMemory
{
    Task StoreAsync(string content, IDictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryResult>> RecallAsync(string query, int maxResults = 5, CancellationToken ct = default);
}

public record MemoryResult(string Content, double Relevance, IDictionary<string, string> Metadata);

public sealed class InMemoryLongTermMemory : ILongTermMemory
{
    private readonly List<(string Content, IDictionary<string, string> Metadata)> _entries = [];

    public Task StoreAsync(string content, IDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        lock (_entries)
        {
            _entries.Add((content, metadata ?? new Dictionary<string, string>()));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryResult>> RecallAsync(string query, int maxResults = 5, CancellationToken ct = default)
    {
        lock (_entries)
        {
            // Naive keyword-based relevance for InMemory implementation
            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var results = _entries
                .Select(e =>
                {
                    var matchCount = queryWords.Count(w =>
                        e.Content.Contains(w, StringComparison.OrdinalIgnoreCase));
                    var relevance = queryWords.Length > 0 ? (double)matchCount / queryWords.Length : 0;
                    return new MemoryResult(e.Content, relevance, e.Metadata);
                })
                .OrderByDescending(r => r.Relevance)
                .Take(maxResults)
                .Where(r => r.Relevance > 0)
                .ToList();

            return Task.FromResult<IReadOnlyList<MemoryResult>>(results);
        }
    }
}
