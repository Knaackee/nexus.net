namespace Nexus.Core.Auth;

public interface IAuthStrategy
{
    AuthMethod Method { get; }
    Task<AuthToken> AcquireTokenAsync(CancellationToken ct = default);
    Task<AuthToken> RefreshTokenAsync(AuthToken expired, CancellationToken ct = default);
    Task RevokeAsync(AuthToken token, CancellationToken ct = default);
}

public enum AuthMethod
{
    ApiKey,
    OAuth2ClientCredentials,
    OAuth2AuthorizationCode,
    OAuth2DeviceFlow,
    OpenIdConnect,
    MutualTls,
    Custom
}

public record AuthToken
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlySet<string> Scopes { get; init; } = new HashSet<string>();
    public string? ResourceIndicator { get; init; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsExpiringSoon(TimeSpan threshold) => DateTimeOffset.UtcNow >= ExpiresAt - threshold;
}
