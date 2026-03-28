using System.Collections.Concurrent;
using Nexus.Core.Auth;

namespace Nexus.Auth.OAuth2;

/// <summary>
/// Thread-safe token cache that auto-refreshes tokens before expiry.
/// </summary>
public sealed class TokenCache : IDisposable
{
    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();
    private readonly TimeSpan _refreshThreshold;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public TokenCache(TimeSpan? refreshThreshold = null)
    {
        _refreshThreshold = refreshThreshold ?? TimeSpan.FromMinutes(5);
    }

    public void Dispose() => _semaphore.Dispose();

    public async Task<AuthToken> GetOrAcquireAsync(
        string key, IAuthStrategy strategy, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached) && !cached.Token.IsExpiringSoon(_refreshThreshold))
        {
            return cached.Token;
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(key, out cached) && !cached.Token.IsExpiringSoon(_refreshThreshold))
            {
                return cached.Token;
            }

            AuthToken token;
            if (cached is not null && !cached.Token.IsExpired)
            {
                token = await strategy.RefreshTokenAsync(cached.Token, ct).ConfigureAwait(false);
            }
            else
            {
                token = await strategy.AcquireTokenAsync(ct).ConfigureAwait(false);
            }

            _cache[key] = new CachedToken(token, DateTimeOffset.UtcNow);
            return token;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Invalidate(string key) => _cache.TryRemove(key, out _);

    public void Clear() => _cache.Clear();

    private sealed record CachedToken(AuthToken Token, DateTimeOffset AcquiredAt);
}
