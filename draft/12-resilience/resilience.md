# Resilience — Error Handling & Recovery

> Betrifft: `Nexus.Orchestration`, `Nexus.Core`  
> Deps: `Microsoft.Extensions.Resilience` (Polly v8)

## 1. Fehlerquellen in Multi-Agent-Systemen

| Quelle | Häufigkeit | Impact |
|--------|-----------|--------|
| LLM Rate Limits | Hoch | Temporärer Ausfall |
| LLM Timeout | Mittel | Task-Verzögerung |
| LLM Halluzination | Hoch | Falsches Ergebnis |
| Tool-Fehler (API Down) | Mittel | Node-Failure |
| Context Overflow | Hoch (still) | Qualitätsverlust |
| Agent-Loop (Endlosschleife) | Mittel | Kostexplosion |
| Budget-Überschreitung | Gering | Harter Stop |
| Netzwerk-Fehler (A2A, MCP) | Gering | Node-Failure |

## 2. TaskErrorPolicy — Pro Node konfigurierbar

```csharp
public record TaskErrorPolicy
{
    public RetryOptions? Retry { get; init; }
    public FallbackOptions? Fallback { get; init; }
    public Func<AgentResult, IAgentContext, Task>? CompensationAction { get; init; }
    public AgentResult? SkipWithDefault { get; init; }
    public bool EscalateToHuman { get; init; }
    public bool SendToDeadLetter { get; init; }
    public CircuitBreakerOptions? CircuitBreaker { get; init; }
    public int MaxIterations { get; init; } = 25;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
```

## 3. Retry

```csharp
public record RetryOptions
{
    public int MaxRetries { get; init; } = 3;
    public BackoffType BackoffType { get; init; } = BackoffType.ExponentialWithJitter;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<Type>? RetryOn { get; init; }  // null = alle Exceptions
}

public enum BackoffType { Constant, Linear, Exponential, ExponentialWithJitter }
```

## 4. Fallback

```csharp
public record FallbackOptions
{
    public string? AlternateModelId { get; init; }           // Anderes Modell
    public string? AlternateChatClientName { get; init; }    // Anderer Provider
    public IAgent? FallbackAgent { get; init; }              // Komplett anderer Agent
    public Func<AgentTask, Task<AgentResult>>? FallbackFunc { get; init; }  // Custom Logik
}
```

## 5. Circuit Breaker

```csharp
public record CircuitBreakerOptions
{
    public double FailureThreshold { get; init; } = 0.5;      // 50% Fehlerrate
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(60);
    public int MinimumThroughput { get; init; } = 5;           // Mindestens 5 Requests
}
```

## 6. Compensation (Saga Pattern)

```csharp
// Wenn ein Node fehlschlägt, können vorherige Nodes rückgängig gemacht werden:
graph.AddTask(new AgentTask
{
    Id = new("deploy"),
    Description = "Deploy to production",
    ErrorPolicy = new TaskErrorPolicy
    {
        CompensationAction = async (result, ctx) =>
        {
            await ctx.Tools.Resolve("rollback")!.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { version = "previous" }),
                ctx.AsToolContext(), default);
        }
    }
});
```

## 7. Dead Letter Queue

```csharp
// Fehlgeschlagene Tasks werden persistiert für manuelle Wiederholung:
var policy = new TaskErrorPolicy { SendToDeadLetter = true };

// Später:
await foreach (var failed in deadLetterQueue.DequeueAsync(ct))
{
    _logger.LogWarning("Failed task: {TaskId}, Error: {Error}", failed.OriginalTask.Id, failed.Error.Message);
    // Manuell re-queuen:
    await deadLetterQueue.RetryAsync(failed, ct);
}
```

## 8. Timeout & Loop Prevention

```csharp
// Max Iterations verhindert Endlosschleifen:
new AgentDefinition { Budget = new(MaxIterations: 25) }

// Timeout pro Agent-Aufruf:
new TaskErrorPolicy { Timeout = TimeSpan.FromMinutes(3) }

// Globaler Timeout für die gesamte Orchestrierung:
new OrchestrationOptions { GlobalTimeout = TimeSpan.FromMinutes(30) }
```

## 9. IChatClient Resilience (Provider-Level)

```csharp
// Polly v8 direkt auf IChatClient:
n.UseChatClient("openai", sp =>
    new ChatClientBuilder(new OpenAIClient(key).GetChatClient("gpt-4o").AsIChatClient())
        .UseOpenTelemetry()
        .Use(new ResiliencePipelineBuilder<ChatResponse>()
            .AddRetry(new RetryStrategyOptions<ChatResponse>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<ChatResponse>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<ChatResponse>())
            .AddTimeout(TimeSpan.FromSeconds(120))
            .Build())
        .Build());
```
