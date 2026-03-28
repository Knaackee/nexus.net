using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Guardrails;

public interface IGuardrail
{
    string Name { get; }
    GuardrailPhase Phase { get; }
    Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default);
}

public enum GuardrailPhase { Input, Output, ToolCall, ToolResult }

public record GuardrailResult
{
    public required bool IsAllowed { get; init; }
    public string? Reason { get; init; }
    public GuardrailAction Action { get; init; } = GuardrailAction.Block;
    public string? SanitizedContent { get; init; }

    public static GuardrailResult Allow() => new() { IsAllowed = true, Action = GuardrailAction.Allow };
    public static GuardrailResult Block(string reason) => new() { IsAllowed = false, Reason = reason, Action = GuardrailAction.Block };
    public static GuardrailResult Redact(string reason, string sanitized) => new() { IsAllowed = true, Reason = reason, Action = GuardrailAction.Redact, SanitizedContent = sanitized };
}

public enum GuardrailAction { Allow, Block, Redact, Warn, RequestApproval }

public record GuardrailContext
{
    public required string Content { get; init; }
    public GuardrailPhase Phase { get; init; }
    public string? ToolName { get; init; }
    public JsonElement? ToolArguments { get; init; }
    public ToolResult? ToolResult { get; init; }
    public IDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

public interface IGuardrailPipeline
{
    Task<GuardrailResult> EvaluateInputAsync(string input, CancellationToken ct = default);
    Task<GuardrailResult> EvaluateOutputAsync(string output, CancellationToken ct = default);
    Task<GuardrailResult> EvaluateToolCallAsync(string toolName, JsonElement args, CancellationToken ct = default);
    Task<GuardrailResult> EvaluateToolResultAsync(string toolName, ToolResult result, CancellationToken ct = default);
}
