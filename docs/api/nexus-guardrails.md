# Nexus.Guardrails API Reference

## Namespace: Nexus.Guardrails

### IGuardrail

```csharp
public interface IGuardrail
{
    string Name { get; }
    GuardrailPhase Phase { get; }
    Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default);
}
```

### GuardrailPhase

```csharp
public enum GuardrailPhase { Input, Output, ToolCall, ToolResult }
```

### GuardrailResult

```csharp
public record GuardrailResult
{
    public required bool IsAllowed { get; init; }
    public string? Reason { get; init; }
    public GuardrailAction Action { get; init; }
    public string? SanitizedContent { get; init; }

    public static GuardrailResult Allow();
    public static GuardrailResult Block(string reason);
    public static GuardrailResult Redact(string reason, string sanitized);
}
```

### GuardrailAction

```csharp
public enum GuardrailAction { Allow, Block, Redact, Warn, RequestApproval }
```

### GuardrailContext

```csharp
public record GuardrailContext
{
    public required string Content { get; init; }
    public GuardrailPhase Phase { get; init; }
    public string? ToolName { get; init; }
    public JsonElement? ToolArguments { get; init; }
    public ToolResult? ToolResult { get; init; }
    public IDictionary<string, object> Metadata { get; init; }
}
```

### IGuardrailPipeline

```csharp
public interface IGuardrailPipeline
{
    Task<GuardrailResult> EvaluateInputAsync(string input, CancellationToken ct = default);
    Task<GuardrailResult> EvaluateOutputAsync(string output, CancellationToken ct = default);
    Task<GuardrailResult> EvaluateToolCallAsync(string toolName, JsonElement args, CancellationToken ct = default);
    Task<GuardrailResult> EvaluateToolResultAsync(string toolName, ToolResult result, CancellationToken ct = default);
}
```

## Built-In Guardrails

| Class | Phase | Description |
|-------|-------|-------------|
| `PromptInjectionDetector` | Input | Detects prompt injection patterns |
| `PiiRedactor` | Output (configurable) | Redacts emails, phone numbers, SSNs |
| `SecretsDetector` | Input/Output | Detects API keys and tokens |
| `InputLengthLimiter` | Input | Enforces token limits |
