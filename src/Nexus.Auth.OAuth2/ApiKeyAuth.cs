using Nexus.Core.Auth;
using Nexus.Core.Contracts;

namespace Nexus.Auth.OAuth2;

/// <summary>Simple API key authentication strategy.</summary>
public sealed class ApiKeyAuth : IAuthStrategy
{
    private readonly string _secretKey;
    private readonly ISecretProvider _secrets;

    public ApiKeyAuth(string secretKey, ISecretProvider secrets)
    {
        _secretKey = secretKey;
        _secrets = secrets;
    }

    public AuthMethod Method => AuthMethod.ApiKey;

    public async Task<AuthToken> AcquireTokenAsync(CancellationToken ct = default)
    {
        var key = await _secrets.GetSecretAsync(_secretKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Secret '{_secretKey}' not found.");
        return new AuthToken
        {
            AccessToken = key,
            ExpiresAt = DateTimeOffset.MaxValue, // API keys don't expire
        };
    }

    public Task<AuthToken> RefreshTokenAsync(AuthToken expired, CancellationToken ct = default)
        => AcquireTokenAsync(ct); // Re-read the secret

    public Task RevokeAsync(AuthToken token, CancellationToken ct = default)
        => Task.CompletedTask; // API keys can't be revoked via this interface
}
