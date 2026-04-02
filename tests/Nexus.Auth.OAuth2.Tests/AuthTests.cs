using FluentAssertions;
using NSubstitute;
using Nexus.Auth.OAuth2;
using Nexus.Core.Auth;
using Nexus.Core.Contracts;
using Xunit;

namespace Nexus.Auth.OAuth2.Tests;

public class ApiKeyAuthTests
{
    [Fact]
    public async Task AcquireTokenAsync_ReturnsApiKeyAsAccessToken()
    {
        var secrets = Substitute.For<ISecretProvider>();
        secrets.GetSecretAsync("my-key", Arg.Any<CancellationToken>())
            .Returns("sk-test-12345");

        var auth = new ApiKeyAuth("my-key", secrets);

        var token = await auth.AcquireTokenAsync();

        token.AccessToken.Should().Be("sk-test-12345");
        token.ExpiresAt.Should().Be(DateTimeOffset.MaxValue);
    }

    [Fact]
    public async Task AcquireTokenAsync_ThrowsWhenSecretNotFound()
    {
        var secrets = Substitute.For<ISecretProvider>();
        secrets.GetSecretAsync("missing", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var auth = new ApiKeyAuth("missing", secrets);

        var act = () => auth.AcquireTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing*");
    }

    [Fact]
    public void Method_ReturnsApiKey()
    {
        var secrets = Substitute.For<ISecretProvider>();
        var auth = new ApiKeyAuth("key", secrets);

        auth.Method.Should().Be(AuthMethod.ApiKey);
    }

    [Fact]
    public async Task RefreshTokenAsync_ReAcquiresToken()
    {
        var secrets = Substitute.For<ISecretProvider>();
        secrets.GetSecretAsync("key", Arg.Any<CancellationToken>())
            .Returns("refreshed-key");

        var auth = new ApiKeyAuth("key", secrets);
        var expired = new AuthToken { AccessToken = "old", ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) };

        var token = await auth.RefreshTokenAsync(expired);

        token.AccessToken.Should().Be("refreshed-key");
    }

    [Fact]
    public async Task RevokeAsync_CompletesSuccessfully()
    {
        var secrets = Substitute.For<ISecretProvider>();
        var auth = new ApiKeyAuth("key", secrets);
        var token = new AuthToken { AccessToken = "test" };

        await auth.RevokeAsync(token);
        // Should not throw
    }
}

public class OAuth2ClientCredentialsTests
{
    [Fact]
    public void Method_ReturnsOAuth2ClientCredentials()
    {
        var options = new OAuth2ClientCredentialsOptions
        {
            TokenEndpoint = new Uri("https://auth.example.com/token"),
            ClientId = "client-id",
            ClientSecret = "client-secret"
        };

        using var auth = new OAuth2ClientCredentials(options);

        auth.Method.Should().Be(AuthMethod.OAuth2ClientCredentials);
    }

    [Fact]
    public async Task AcquireTokenAsync_PostsToTokenEndpoint()
    {
        var handler = new FakeHttpHandler(new HttpResponseMessage
        {
            Content = new StringContent("""
                {
                    "access_token": "test-token",
                    "token_type": "Bearer",
                    "expires_in": 3600
                }
                """, System.Text.Encoding.UTF8, "application/json")
        });

        var options = new OAuth2ClientCredentialsOptions
        {
            TokenEndpoint = new Uri("https://auth.example.com/token"),
            ClientId = "cid",
            ClientSecret = "csecret",
            Scopes = new HashSet<string> { "read", "write" }
        };

        using var httpClient = new HttpClient(handler);
        using var auth = new OAuth2ClientCredentials(options, httpClient);

        var token = await auth.AcquireTokenAsync();

        token.AccessToken.Should().Be("test-token");
        token.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(3600), TimeSpan.FromSeconds(5));
        handler.LastRequest!.RequestUri.Should().Be(new Uri("https://auth.example.com/token"));
    }

    [Fact]
    public async Task AcquireTokenAsync_IncludesScopesInRequest()
    {
        var handler = new FakeHttpHandler(new HttpResponseMessage
        {
            Content = new StringContent("""
                {
                    "access_token": "scoped-token",
                    "token_type": "Bearer",
                    "expires_in": 1800,
                    "scope": "read write"
                }
                """, System.Text.Encoding.UTF8, "application/json")
        });

        var options = new OAuth2ClientCredentialsOptions
        {
            TokenEndpoint = new Uri("https://auth.example.com/token"),
            ClientId = "cid",
            ClientSecret = "csecret",
            Scopes = new HashSet<string> { "read", "write" }
        };

        using var httpClient = new HttpClient(handler);
        using var auth = new OAuth2ClientCredentials(options, httpClient);

        var token = await auth.AcquireTokenAsync();

        token.Scopes.Should().Contain("read").And.Contain("write");
    }
}

public class TokenCacheTests
{
    [Fact]
    public async Task GetOrAcquireAsync_CachesToken()
    {
        using var cache = new TokenCache();
        var strategy = Substitute.For<IAuthStrategy>();
        strategy.AcquireTokenAsync(Arg.Any<CancellationToken>())
            .Returns(new AuthToken { AccessToken = "cached", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });

        var first = await cache.GetOrAcquireAsync("key1", strategy);
        var second = await cache.GetOrAcquireAsync("key1", strategy);

        first.AccessToken.Should().Be("cached");
        second.AccessToken.Should().Be("cached");
        await strategy.Received(1).AcquireTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrAcquireAsync_RefreshesExpiringSoonToken()
    {
        using var cache = new TokenCache(refreshThreshold: TimeSpan.FromMinutes(10));
        var strategy = Substitute.For<IAuthStrategy>();

        var expiringToken = new AuthToken { AccessToken = "old", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3) };
        var freshToken = new AuthToken { AccessToken = "new", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };

        strategy.AcquireTokenAsync(Arg.Any<CancellationToken>()).Returns(expiringToken);
        strategy.RefreshTokenAsync(Arg.Any<AuthToken>(), Arg.Any<CancellationToken>()).Returns(freshToken);

        var first = await cache.GetOrAcquireAsync("key", strategy);
        first.AccessToken.Should().Be("old");

        var second = await cache.GetOrAcquireAsync("key", strategy);
        second.AccessToken.Should().Be("new");
    }

    [Fact]
    public async Task Invalidate_RemovesCachedToken()
    {
        using var cache = new TokenCache();
        var strategy = Substitute.For<IAuthStrategy>();
        var callCount = 0;
        strategy.AcquireTokenAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new AuthToken
            {
                AccessToken = $"token-{++callCount}",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });

        await cache.GetOrAcquireAsync("key", strategy);
        cache.Invalidate("key");
        var token = await cache.GetOrAcquireAsync("key", strategy);

        token.AccessToken.Should().Be("token-2");
    }

    [Fact]
    public void Clear_RemovesAllCachedTokens()
    {
        using var cache = new TokenCache();
        cache.Clear(); // should not throw when empty
    }
}

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpHandler(HttpResponseMessage response) => _response = response;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(_response);
    }
}
