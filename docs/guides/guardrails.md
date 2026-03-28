# Guardrails

Guardrails validate and sanitize content at four phases of agent execution: input, output, tool call, and tool result.

## Interfaces

### IGuardrail

```csharp
public interface IGuardrail
{
    string Name { get; }
    GuardrailPhase Phase { get; }  // Input, Output, ToolCall, ToolResult
    Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default);
}
```

### GuardrailResult

```csharp
public record GuardrailResult
{
    public required bool IsAllowed { get; init; }
    public string? Reason { get; init; }
    public GuardrailAction Action { get; init; }     // Allow, Block, Redact, Warn, RequestApproval
    public string? SanitizedContent { get; init; }    // Used with Redact action
}
```

Factory methods:

```csharp
GuardrailResult.Allow();
GuardrailResult.Block("Contains prohibited content");
GuardrailResult.Redact("PII detected", sanitizedText);
```

### IGuardrailPipeline

Evaluates all registered guardrails for a given phase:

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

### PromptInjectionDetector

Detects common prompt injection patterns in user input:

```csharp
var detector = new PromptInjectionDetector();
var result = await detector.EvaluateAsync(new GuardrailContext
{
    Content = userInput,
    Phase = GuardrailPhase.Input,
});
```

### PiiRedactor

Redacts personally identifiable information (emails, phone numbers, SSNs):

```csharp
var redactor = new PiiRedactor(GuardrailPhase.Output);
var result = await redactor.EvaluateAsync(new GuardrailContext
{
    Content = agentOutput,
    Phase = GuardrailPhase.Output,
});
// result.SanitizedContent contains the redacted text
```

### SecretsDetector

Detects API keys, tokens, and other secrets in content:

```csharp
var detector = new SecretsDetector();
```

### InputLengthLimiter

Enforces token limits on input:

```csharp
var limiter = new InputLengthLimiter { MaxTokens = 5000 };
```

## Using the Pipeline

Compose multiple guardrails into a pipeline:

```csharp
using Nexus.Guardrails;
using Nexus.Guardrails.BuiltIn;

var pipeline = new DefaultGuardrailPipeline([
    new PromptInjectionDetector(),
    new PiiRedactor(GuardrailPhase.Output),
    new SecretsDetector(),
    new InputLengthLimiter { MaxTokens = 5000 },
]);

// Check user input
var inputResult = await pipeline.EvaluateInputAsync(userMessage);
if (!inputResult.IsAllowed)
{
    Console.WriteLine($"Blocked: {inputResult.Reason}");
    return;
}

// Check agent output before showing to user
var outputResult = await pipeline.EvaluateOutputAsync(agentResponse);
var safeOutput = outputResult.SanitizedContent ?? agentResponse;
```

## Configuration

Register guardrails via the builder:

```csharp
services.AddNexus(nexus =>
{
    nexus.AddGuardrails(g =>
    {
        g.AddPromptInjectionDetector();
        g.AddPiiRedactor();
        g.AddSecretsDetector();
        g.AddInputLengthLimiter(maxTokens: 10_000);
    });
});
```

## Custom Guardrails

Implement `IGuardrail` for domain-specific validation:

```csharp
public class ProfanityFilter : IGuardrail
{
    public string Name => "profanity-filter";
    public GuardrailPhase Phase => GuardrailPhase.Input;

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct)
    {
        if (ContainsProfanity(context.Content))
            return Task.FromResult(GuardrailResult.Block("Profanity detected"));

        return Task.FromResult(GuardrailResult.Allow());
    }
}
```

## Guardrail Actions

| Action | Behavior |
|--------|----------|
| `Allow` | Content passes without modification |
| `Block` | Content is rejected; execution stops |
| `Redact` | Content is sanitized; execution continues with `SanitizedContent` |
| `Warn` | Content passes but a warning is logged |
| `RequestApproval` | Content is held pending human approval |

## GuardrailContext

The context object provides full information about what's being evaluated:

```csharp
public record GuardrailContext
{
    public required string Content { get; init; }
    public GuardrailPhase Phase { get; init; }
    public string? ToolName { get; init; }           // Set for ToolCall/ToolResult phases
    public JsonElement? ToolArguments { get; init; }  // Set for ToolCall phase
    public ToolResult? ToolResult { get; init; }      // Set for ToolResult phase
    public IDictionary<string, object> Metadata { get; init; }
}
```
