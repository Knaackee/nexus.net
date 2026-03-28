using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nexus.Core.Auth;

namespace Nexus.Auth.OAuth2;

/// <summary>OAuth 2.0 Client Credentials flow for machine-to-machine auth.</summary>
public sealed class OAuth2ClientCredentials : IAuthStrategy, IDisposable
{
    private readonly OAuth2ClientCredentialsOptions _options;
    private readonly HttpClient _httpClient;

    public OAuth2ClientCredentials(OAuth2ClientCredentialsOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _httpClient = httpClient ?? new HttpClient();
    }

    public AuthMethod Method => AuthMethod.OAuth2ClientCredentials;

    public async Task<AuthToken> AcquireTokenAsync(CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
        };

        if (_options.Scopes.Count > 0)
            parameters["scope"] = string.Join(" ", _options.Scopes);

        if (_options.Resource is not null)
            parameters["resource"] = _options.Resource;

        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(_options.TokenEndpoint, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty token response.");

        return new AuthToken
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            Scopes = tokenResponse.Scope?.Split(' ').ToHashSet() ?? new HashSet<string>(),
            ResourceIndicator = _options.Resource,
        };
    }

    public Task<AuthToken> RefreshTokenAsync(AuthToken expired, CancellationToken ct = default)
        => AcquireTokenAsync(ct); // Client credentials always re-acquires

    public Task RevokeAsync(AuthToken token, CancellationToken ct = default)
        => Task.CompletedTask;

    public void Dispose() => _httpClient.Dispose();
}

public record OAuth2ClientCredentialsOptions
{
    public required Uri TokenEndpoint { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public IReadOnlySet<string> Scopes { get; init; } = new HashSet<string>();
    public string? Resource { get; init; }
}

internal sealed record TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; } = 3600;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}
