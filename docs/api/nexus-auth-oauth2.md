# Nexus.Auth.OAuth2 API Reference

`Nexus.Auth.OAuth2` provides authentication strategies for machine-to-machine and secret-backed access.

Use it when your runtime, provider adapter, or host needs an `IAuthStrategy` implementation instead of embedding token acquisition logic directly into your app.

## Key Types

### `OAuth2ClientCredentials`

OAuth 2.0 client credentials flow for service-to-service access.

```csharp
public sealed class OAuth2ClientCredentials : IAuthStrategy, IDisposable
```

It posts a token request to the configured endpoint, supports `scope`, optional `resource`, and re-acquires a token on refresh.

### `OAuth2ClientCredentialsOptions`

```csharp
public record OAuth2ClientCredentialsOptions
{
    public required Uri TokenEndpoint { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public IReadOnlySet<string> Scopes { get; init; }
    public string? Resource { get; init; }
}
```

### `ApiKeyAuth`

Wraps an `ISecretProvider` and turns a stored API key into an `AuthToken`.

```csharp
public sealed class ApiKeyAuth : IAuthStrategy
```

This is useful when the calling component wants a unified auth abstraction but the upstream service still uses API keys.

## Typical Use

```csharp
var auth = new OAuth2ClientCredentials(new OAuth2ClientCredentialsOptions
{
    TokenEndpoint = new Uri("https://issuer.example.com/oauth/token"),
    ClientId = "client-id",
    ClientSecret = "client-secret",
    Scopes = new HashSet<string> { "nexus.runtime" },
});

var token = await auth.AcquireTokenAsync();
```

## When To Use It

- provider SDK integration needs a reusable auth strategy
- secrets should stay behind `ISecretProvider`
- token acquisition should be shared and testable

## Related Docs

- [Auth Guide](../guides/auth.md)