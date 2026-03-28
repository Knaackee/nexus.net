# Auth & Security

Authentication strategies, token management, and secret providers for agent-to-LLM and agent-to-service communication.

## IAuthStrategy

```csharp
public interface IAuthStrategy
{
    AuthMethod Method { get; }
    Task<AuthToken> AcquireTokenAsync(CancellationToken ct = default);
    Task<AuthToken> RefreshTokenAsync(AuthToken expired, CancellationToken ct = default);
    Task RevokeAsync(AuthToken token, CancellationToken ct = default);
}
```

### AuthMethod

```csharp
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
```

### AuthToken

```csharp
public record AuthToken
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlySet<string> Scopes { get; init; }
    public string? ResourceIndicator { get; init; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsExpiringSoon(TimeSpan threshold)
        => DateTimeOffset.UtcNow >= ExpiresAt - threshold;
}
```

## Usage

### API Key Authentication

```csharp
services.AddNexus(nexus =>
{
    nexus.AddSecrets(s =>
    {
        // Register secret providers
    });
});
```

### OAuth2 Client Credentials

For server-to-server authentication:

```csharp
var authStrategy = new OAuth2ClientCredentialsStrategy(new OAuth2Options
{
    TokenEndpoint = new Uri("https://auth.example.com/token"),
    ClientId = "my-app",
    ClientSecret = secretProvider.GetSecret("oauth-client-secret"),
    Scopes = ["api.read", "api.write"],
});

var token = await authStrategy.AcquireTokenAsync();
// token.AccessToken, token.ExpiresAt, etc.
```

### Token Lifecycle

```csharp
// Acquire initial token
var token = await strategy.AcquireTokenAsync();

// Check expiration
if (token.IsExpiringSoon(TimeSpan.FromMinutes(5)))
{
    token = await strategy.RefreshTokenAsync(token);
}

// Revoke when done
await strategy.RevokeAsync(token);
```

## Approval Gate

The `IApprovalGate` interface enables human-in-the-loop approval for sensitive operations:

```csharp
services.AddNexus(nexus =>
{
    nexus.AddApprovalGate<SlackApprovalGate>();
});
```

The default `AutoApproveGate` approves all operations automatically. Replace it for production use.

## Security Best Practices

1. **Never hardcode secrets** — Use `ISecretProvider` or environment variables
2. **Token caching** — The OAuth2 implementation caches tokens and refreshes proactively
3. **Scope limiting** — Request only the scopes your agents need
4. **Guardrails** — Use `SecretsDetector` guardrail to prevent accidental secret leakage in agent outputs
5. **Audit logging** — Enable `IAuditLog` to track all authentication events
