using System.Text.RegularExpressions;

namespace Nexus.Guardrails.BuiltIn;

public sealed partial class PromptInjectionDetector : IGuardrail
{
    public string Name => "prompt-injection-detector";
    public GuardrailPhase Phase => GuardrailPhase.Input;

    [GeneratedRegex(@"(?i)(ignore\s+(previous|above|all)\s+(instructions?|prompts?|rules?)|you\s+are\s+now|system\s*prompt|forget\s+(everything|all)|do\s+not\s+follow|disregard\s+(the|any|all)|override\s+(the|system)|new\s+instructions?|jailbreak|DAN\s+mode)", RegexOptions.Compiled)]
    private static partial Regex InjectionPattern();

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        if (InjectionPattern().IsMatch(context.Content))
            return Task.FromResult(GuardrailResult.Block("Potential prompt injection detected"));

        return Task.FromResult(GuardrailResult.Allow());
    }
}

public sealed partial class PiiRedactor : IGuardrail
{
    public string Name => "pii-redactor";
    public GuardrailPhase Phase { get; }

    public PiiRedactor(GuardrailPhase phase = GuardrailPhase.Output) => Phase = phase;

    [GeneratedRegex(@"\b\d{3}[-.]?\d{2}[-.]?\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3}\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardPattern();

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        var sanitized = context.Content;
        bool redacted = false;

        sanitized = RedactIfMatch(SsnPattern(), sanitized, "[SSN-REDACTED]", ref redacted);
        sanitized = RedactIfMatch(EmailPattern(), sanitized, "[EMAIL-REDACTED]", ref redacted);
        sanitized = RedactIfMatch(PhonePattern(), sanitized, "[PHONE-REDACTED]", ref redacted);
        sanitized = RedactIfMatch(CreditCardPattern(), sanitized, "[CC-REDACTED]", ref redacted);

        return Task.FromResult(redacted
            ? GuardrailResult.Redact("PII detected and redacted", sanitized)
            : GuardrailResult.Allow());
    }

    private static string RedactIfMatch(Regex pattern, string input, string replacement, ref bool modified)
    {
        if (pattern.IsMatch(input))
        {
            modified = true;
            return pattern.Replace(input, replacement);
        }

        return input;
    }
}

public sealed class InputLengthLimiter : IGuardrail
{
    public string Name => "input-length-limiter";
    public GuardrailPhase Phase => GuardrailPhase.Input;
    public int MaxTokens { get; init; } = 4000;

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        var estimatedTokens = context.Content.Length / 4;
        return Task.FromResult(estimatedTokens > MaxTokens
            ? GuardrailResult.Block($"Input exceeds {MaxTokens} tokens (estimated: {estimatedTokens})")
            : GuardrailResult.Allow());
    }
}

public sealed class OutputLengthLimiter : IGuardrail
{
    public string Name => "output-length-limiter";
    public GuardrailPhase Phase => GuardrailPhase.Output;
    public int MaxTokens { get; init; } = 8000;

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        var estimatedTokens = context.Content.Length / 4;
        return Task.FromResult(estimatedTokens > MaxTokens
            ? GuardrailResult.Block($"Output exceeds {MaxTokens} tokens (estimated: {estimatedTokens})")
            : GuardrailResult.Allow());
    }
}

public sealed partial class SecretsDetector : IGuardrail
{
    public string Name => "secrets-detector";
    public GuardrailPhase Phase => GuardrailPhase.Output;

    [GeneratedRegex(@"(?i)(sk-[a-zA-Z0-9]{20,}|ghp_[a-zA-Z0-9]{36}|AKIA[A-Z0-9]{16}|(?:password|secret|api[_-]?key)\s*[:=]\s*\S+)", RegexOptions.Compiled)]
    private static partial Regex SecretPattern();

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        if (SecretPattern().IsMatch(context.Content))
        {
            var sanitized = SecretPattern().Replace(context.Content, "[SECRET-REDACTED]");
            return Task.FromResult(GuardrailResult.Redact("Potential secrets detected", sanitized));
        }

        return Task.FromResult(GuardrailResult.Allow());
    }
}

public sealed partial class IndirectInjectionDetector : IGuardrail
{
    public string Name => "indirect-injection-detector";
    public GuardrailPhase Phase => GuardrailPhase.ToolResult;

    [GeneratedRegex(@"(?i)(IMPORTANT:\s*ignore|<\s*system\s*>|<\|im_start\|>|<<SYS>>|\[INST\]|<\|endoftext\|>)", RegexOptions.Compiled)]
    private static partial Regex IndirectPattern();

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        if (IndirectPattern().IsMatch(context.Content))
            return Task.FromResult(GuardrailResult.Block("Potential indirect prompt injection in tool result"));

        return Task.FromResult(GuardrailResult.Allow());
    }
}
