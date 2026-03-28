# Guardrails — Nexus.Guardrails

> Assembly: `Nexus.Guardrails`  
> Deps: `Nexus.Core`

## 1. Architektur

Guardrails sind eine **vier-phasige Security-Pipeline** die als Agent-Middleware läuft.

```
User Input → [INPUT GUARDS] → LLM → [OUTPUT GUARDS] → Response
                                ↓
                          [TOOL CALL GUARDS] → Tool → [TOOL RESULT GUARDS]
```

## 2. Interfaces

```csharp
public interface IGuardrail
{
    string Name { get; }
    GuardrailPhase Phase { get; }
    Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct);
}

public enum GuardrailPhase { Input, Output, ToolCall, ToolResult }

public record GuardrailResult
{
    public required bool IsAllowed { get; init; }
    public string? Reason { get; init; }
    public GuardrailAction Action { get; init; } = GuardrailAction.Block;
    public string? SanitizedContent { get; init; }
}

public enum GuardrailAction { Allow, Block, Redact, Warn, RequestApproval }

public interface IGuardrailPipeline
{
    Task<GuardrailResult> EvaluateInputAsync(string input, CancellationToken ct);
    Task<GuardrailResult> EvaluateOutputAsync(string output, CancellationToken ct);
    Task<GuardrailResult> EvaluateToolCallAsync(string toolName, JsonElement args, CancellationToken ct);
    Task<GuardrailResult> EvaluateToolResultAsync(string toolName, ToolResult result, CancellationToken ct);
}
```

## 3. Built-in Guards

| Guard | Phase | Beschreibung | Latenz |
|-------|-------|-------------|--------|
| `PromptInjectionDetector` | Input | Regex + Heuristik Patterns | <5ms |
| `PiiRedactor` | Input, Output | SSN, Email, Phone, CC Detection + Redaction | <10ms |
| `InputLengthLimiter` | Input | Max Token Count | <1ms |
| `OutputLengthLimiter` | Output | Max Token Count | <1ms |
| `TopicGuard` | Input, Output | Allowed/Blocked Topic Keywords | <5ms |
| `ToolArgumentValidator` | ToolCall | JSON Schema Validation | <2ms |
| `IndirectInjectionDetector` | ToolResult | Prüft Tool-Ergebnisse auf Injection | <10ms |
| `SecretsDetector` | Output | API Keys, Passwords, Tokens | <5ms |

### ML-basierte Guards (Nexus.Guardrails.ML)

| Guard | Phase | Beschreibung | Latenz |
|-------|-------|-------------|--------|
| `OnnxInjectionClassifier` | Input | ONNX-Modell für Prompt Injection | ~50ms |
| `OnnxToxicityClassifier` | Output | ONNX-Modell für toxische Inhalte | ~50ms |

## 4. Registrierung

```csharp
n.AddGuardrails(g =>
{
    g.Add<PromptInjectionDetector>(GuardrailPhase.Input);
    g.Add<PiiRedactor>(GuardrailPhase.Input);
    g.Add<PiiRedactor>(GuardrailPhase.Output);
    g.Add<InputLengthLimiter>(o => o.MaxTokens = 4000);
    g.Add<OutputLengthLimiter>(o => o.MaxTokens = 8000);
    g.Add<IndirectInjectionDetector>(GuardrailPhase.ToolResult);
    g.OnViolation(v => v.RejectWithMessage("Request blocked."));
    g.RunInParallel = true;  // Guards parallel für minimale Latenz
});
```

## 5. Streaming-Integration

Guards laufen als Middleware. Bei Streaming werden Output-Chunks progressiv validiert:

- **Pattern 1: Block before Stream** — Input Guards blocken vor dem ersten Token
- **Pattern 2: Progressive Validation** — Chunks werden geprüft während sie fließen
- **Pattern 3: Post-Stream Recall** — Stream fließt, bei Verstoß wird Korrektur nachgesendet

## 6. Custom Guards

```csharp
public class ComplianceGuard : IGuardrail
{
    public string Name => "compliance";
    public GuardrailPhase Phase => GuardrailPhase.Output;

    public async Task<GuardrailResult> EvaluateAsync(GuardrailContext ctx, CancellationToken ct)
    {
        if (ctx.Content.Contains("confidential", StringComparison.OrdinalIgnoreCase))
            return new() { IsAllowed = false, Action = GuardrailAction.Redact,
                SanitizedContent = ctx.Content.Replace("confidential", "[REDACTED]") };
        return new() { IsAllowed = true };
    }
}
```
