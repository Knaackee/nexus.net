namespace Nexus.Core.Agents;

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

public record RetryOptions
{
    public int MaxRetries { get; init; } = 3;
    public BackoffType BackoffType { get; init; } = BackoffType.ExponentialWithJitter;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<Type>? RetryOn { get; init; }
}

public record FallbackOptions
{
    public string? AlternateModelId { get; init; }
    public string? AlternateChatClientName { get; init; }
    public IAgent? FallbackAgent { get; init; }
    public Func<AgentTask, Task<AgentResult>>? FallbackFunc { get; init; }
}

public record CircuitBreakerOptions
{
    public double FailureThreshold { get; init; } = 0.5;
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(60);
    public int MinimumThroughput { get; init; } = 5;
}

public enum BackoffType
{
    Constant,
    Linear,
    Exponential,
    ExponentialWithJitter
}
