# Auth & Security — Nexus.Auth.OAuth2 + Nexus.Core

## 1. IAuthStrategy

```csharp
public interface IAuthStrategy
{
    AuthMethod Method { get; }
    Task<AuthToken> AcquireTokenAsync(CancellationToken ct = default);
    Task<AuthToken> RefreshTokenAsync(AuthToken expired, CancellationToken ct = default);
    Task RevokeAsync(AuthToken token, CancellationToken ct = default);
}

public enum AuthMethod
{
    ApiKey, OAuth2ClientCredentials, OAuth2AuthorizationCode,
    OAuth2DeviceFlow, OpenIdConnect, MutualTls, Custom
}

public record AuthToken
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlySet<string> Scopes { get; init; } = new HashSet<string>();
    public string? ResourceIndicator { get; init; }  // RFC 8707
}
```

## 2. Built-in Implementierungen (Nexus.Auth.OAuth2)

| Klasse | AuthMethod | Use Case |
|--------|-----------|----------|
| `ApiKeyAuth` | ApiKey | Einfache Provider (OpenAI, Anthropic) |
| `OAuth2ClientCredentials` | OAuth2ClientCredentials | Machine-to-Machine (Azure, Google) |
| `OAuth2AuthorizationCode` | OAuth2AuthorizationCode | User-Interactive mit PKCE |
| `OAuth2DeviceFlow` | OAuth2DeviceFlow | CLI, IoT, Headless |
| `OpenIdConnectAuth` | OpenIdConnect | Enterprise SSO |

## 3. Token Cache

```csharp
public class TokenCache
{
    // Thread-safe, auto-refresh vor Expiry
    public async Task<AuthToken> GetOrAcquireAsync(IAuthStrategy strategy, CancellationToken ct);
}
```

## 4. ISecretProvider

```csharp
public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
}
```

| Implementierung | Paket |
|----------------|-------|
| `EnvironmentSecretProvider` | `Nexus.Core` |
| `AzureKeyVaultSecretProvider` | `Nexus.Secrets.AzureKeyVault` |
| `AwsSecretsManagerProvider` | `Nexus.Secrets.AwsSecretsManager` |
| `UserSecretsProvider` | `Nexus.Core` (Dev) |

## 5. MCP Auth (2025-11-25)

- Protected Resource Metadata Discovery (3 Methoden)
- Resource Indicators (RFC 8707)
- Incremental Scope Consent
- URL-Mode Elicitation
- Client ID Metadata Documents (CIMD)

## 6. A2A Auth (v0.3)

- Signed Agent Cards (kryptographische Verifikation)
- OAuth 2.1 / OIDC Token Exchange
- Short-lived scoped Tokens

## 7. Security Best Practices

- Secrets nie in Prompts oder Logs
- Tool Sandboxing für untrusted Code
- Principle of Least Privilege für Agent-Permissions
- Audit Trail für alle Tool-Calls und Agent-Aktionen
- PII Redaction in Logs und Traces
