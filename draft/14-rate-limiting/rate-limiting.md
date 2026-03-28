# Rate Limiting — Nexus.Core

> Definiert in: `Nexus.Core` (Interface)  
> Implementierung: `Nexus.Orchestration` (Built-in)

## 1. IRateLimiter

```csharp
public interface IRateLimiter
{
    Task<RateLimitLease> AcquireAsync(string resource, int tokens = 1, CancellationToken ct = default);
}

public record RateLimitLease(bool IsAcquired, TimeSpan? RetryAfter = null) : IDisposable
{
    public void Dispose() { }
}
```

## 2. Drei Ebenen

| Ebene | Resource Key | Zweck |
|-------|-------------|-------|
| Provider | `"provider:openai"` | API Rate Limits respektieren |
| Agent | `"agent:{agentId}"` | Max parallele Agent-Ausführungen |
| Tool | `"tool:{toolName}"` | Tool-spezifische Limits |

## 3. Strategien

```csharp
public record TokenBucketOptions
{
    public int TokensPerMinute { get; init; }
    public int RequestsPerMinute { get; init; }
    public int BurstSize { get; init; }
}

public record SlidingWindowOptions
{
    public int RequestsPerMinute { get; init; }
    public int TokensPerMinute { get; init; }
    public TimeSpan WindowSize { get; init; } = TimeSpan.FromMinutes(1);
}

public record FixedWindowOptions
{
    public int RequestsPerMinute { get; init; }
    public TimeSpan WindowSize { get; init; } = TimeSpan.FromMinutes(1);
}

public record ConcurrencyOptions
{
    public int MaxConcurrent { get; init; }
}
```

## 4. Registrierung

```csharp
n.AddRateLimiting(r =>
{
    r.ForProvider("openai", new TokenBucketOptions
    {
        RequestsPerMinute = 500,
        TokensPerMinute = 90_000,
        BurstSize = 50
    });

    r.ForProvider("anthropic", new SlidingWindowOptions
    {
        RequestsPerMinute = 60,
        TokensPerMinute = 80_000
    });

    r.ForTool("web_search", new FixedWindowOptions { RequestsPerMinute = 30 });

    r.MaxConcurrentAgents = 10;
});
```

## 5. Integration

Rate Limiting wird automatisch von der Agent- und Tool-Middleware angewendet. Der Consumer muss es nur konfigurieren.

Bei Überschreitung: `RateLimitLease.IsAcquired == false` → Middleware wartet `RetryAfter` oder gibt einen Fehler zurück (konfigurierbar).
