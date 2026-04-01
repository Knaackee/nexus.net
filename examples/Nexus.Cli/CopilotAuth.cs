using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace Nexus.Cli;

/// <summary>
/// GitHub Copilot device flow authentication.
/// Uses the same client ID as copilot.vim / VS Code Copilot extensions.
/// Token is cached to ~/.nexus-cli/token.json.
/// </summary>
internal static class CopilotAuth
{
    // Well-known GitHub Copilot OAuth App client ID (public, used by all Copilot clients)
    private const string ClientId = "Iv1.b507a08c87ecfe98";
    private const string DeviceCodeUrl = "https://github.com/login/device/code";
    private const string AccessTokenUrl = "https://github.com/login/oauth/access_token";
    private const string CopilotTokenUrl = "https://api.github.com/copilot_internal/v2/token";

    private static readonly string TokenDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus-cli");
    private static readonly string GithubTokenPath = Path.Combine(TokenDir, "github-token.json");
    private static readonly string CopilotTokenPath = Path.Combine(TokenDir, "copilot-token.json");

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim TokenCacheGate = new(1, 1);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NexusCli/0.1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>
    /// Gets a valid Copilot API token, refreshing or re-authenticating as needed.
    /// </summary>
    public static async Task<CopilotToken> GetTokenAsync(CancellationToken ct = default)
    {
        await TokenCacheGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Try cached copilot token
            var cached = LoadCopilotToken();
            if (cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
                return cached;

            // Need a GitHub OAuth token first
            var githubToken = LoadGithubToken();
            if (githubToken is null)
            {
                githubToken = await DeviceFlowAsync(ct).ConfigureAwait(false);
                SaveGithubToken(githubToken);
            }

            // Exchange GitHub token for Copilot API token
            var copilotToken = await ExchangeForCopilotTokenAsync(githubToken.AccessToken, ct).ConfigureAwait(false);
            SaveCopilotToken(copilotToken);
            return copilotToken;
        }
        finally
        {
            TokenCacheGate.Release();
        }
    }

    /// <summary>Runs the GitHub device flow to get an OAuth access token.</summary>
    private static async Task<GithubOAuthToken> DeviceFlowAsync(CancellationToken ct)
    {
        // Step 1: Request device code
        var codeResponse = await Http.PostAsync(DeviceCodeUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = "read:user",
            }), ct).ConfigureAwait(false);
        codeResponse.EnsureSuccessStatusCode();

        var deviceCode = await codeResponse.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Failed to get device code.");

        // Step 2: Show user the code
        AnsiConsole.MarkupLine($"\n[bold yellow]GitHub Copilot Authentication[/]");
        AnsiConsole.MarkupLine($"Open [link={deviceCode.VerificationUri}]{deviceCode.VerificationUri}[/]");
        AnsiConsole.MarkupLine($"Enter code: [bold green]{deviceCode.UserCode}[/]\n");

        // Step 3: Poll for token
        var interval = Math.Max(deviceCode.Interval, 5);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);

            var tokenResponse = await Http.PostAsync(AccessTokenUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = deviceCode.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }), ct).ConfigureAwait(false);

            var json = await tokenResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var atProp))
            {
                AnsiConsole.MarkupLine("[bold green]Authenticated![/]\n");
                return new GithubOAuthToken { AccessToken = atProp.GetString()! };
            }

            if (root.TryGetProperty("error", out var errProp))
            {
                var error = errProp.GetString();
                if (error == "authorization_pending")
                    continue;
                if (error == "slow_down")
                {
                    interval += 5;
                    continue;
                }
                throw new InvalidOperationException($"Device flow error: {error}");
            }
        }

        throw new OperationCanceledException();
    }

    /// <summary>Exchanges a GitHub OAuth token for a Copilot API session token.</summary>
    private static async Task<CopilotToken> ExchangeForCopilotTokenAsync(string githubToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CopilotTokenUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", githubToken);

        var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Failed to get Copilot token (HTTP {(int)response.StatusCode}). " +
                "Make sure you have an active GitHub Copilot subscription. " + body);
        }

        var token = await response.Content.ReadFromJsonAsync<CopilotToken>(cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Failed to deserialize Copilot token.");

        return token;
    }

    private static GithubOAuthToken? LoadGithubToken()
    {
        if (!File.Exists(GithubTokenPath)) return null;
        try
        {
            var json = File.ReadAllText(GithubTokenPath);
            return JsonSerializer.Deserialize<GithubOAuthToken>(json);
        }
        catch { return null; }
    }

    private static void SaveGithubToken(GithubOAuthToken token)
    {
        Directory.CreateDirectory(TokenDir);
        File.WriteAllText(GithubTokenPath, JsonSerializer.Serialize(token));
    }

    private static CopilotToken? LoadCopilotToken()
    {
        if (!File.Exists(CopilotTokenPath)) return null;
        try
        {
            var json = File.ReadAllText(CopilotTokenPath);
            return JsonSerializer.Deserialize<CopilotToken>(json);
        }
        catch { return null; }
    }

    private static void SaveCopilotToken(CopilotToken token)
    {
        Directory.CreateDirectory(TokenDir);
        File.WriteAllText(CopilotTokenPath, JsonSerializer.Serialize(token));
    }

    /// <summary>Removes cached tokens (logout).</summary>
    public static void Logout()
    {
        TokenCacheGate.Wait();
        try
        {
            if (File.Exists(GithubTokenPath)) File.Delete(GithubTokenPath);
            if (File.Exists(CopilotTokenPath)) File.Delete(CopilotTokenPath);
        }
        finally
        {
            TokenCacheGate.Release();
        }
    }
}

internal sealed class DeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public required string DeviceCode { get; init; }

    [JsonPropertyName("user_code")]
    public required string UserCode { get; init; }

    [JsonPropertyName("verification_uri")]
    public required string VerificationUri { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; } = 5;
}

internal sealed class GithubOAuthToken
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }
}

internal sealed class CopilotToken
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAtUnix { get; init; }

    [JsonIgnore]
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);

    [JsonPropertyName("endpoints")]
    public CopilotEndpoints? Endpoints { get; init; }
}

internal sealed class CopilotEndpoints
{
    [JsonPropertyName("api")]
    public string? Api { get; init; }
}
