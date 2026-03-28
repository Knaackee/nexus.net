namespace Nexus.Core.Contracts;

public interface IRateLimiter
{
    Task<RateLimitLease> AcquireAsync(string resource, int tokens = 1, CancellationToken ct = default);
}

public record RateLimitLease(bool IsAcquired, TimeSpan? RetryAfter = null) : IDisposable
{
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
