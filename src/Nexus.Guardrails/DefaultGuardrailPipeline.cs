using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Guardrails;

public sealed class DefaultGuardrailPipeline : IGuardrailPipeline
{
    private readonly IReadOnlyList<IGuardrail> _guards;
    private readonly bool _runInParallel;

    public DefaultGuardrailPipeline(IEnumerable<IGuardrail> guards, bool runInParallel = false)
    {
        _guards = guards.ToList();
        _runInParallel = runInParallel;
    }

    public Task<GuardrailResult> EvaluateInputAsync(string input, CancellationToken ct = default) =>
        EvaluateAsync(new GuardrailContext { Content = input, Phase = GuardrailPhase.Input }, GuardrailPhase.Input, ct);

    public Task<GuardrailResult> EvaluateOutputAsync(string output, CancellationToken ct = default) =>
        EvaluateAsync(new GuardrailContext { Content = output, Phase = GuardrailPhase.Output }, GuardrailPhase.Output, ct);

    public Task<GuardrailResult> EvaluateToolCallAsync(string toolName, JsonElement args, CancellationToken ct = default) =>
        EvaluateAsync(new GuardrailContext
        {
            Content = args.GetRawText(),
            Phase = GuardrailPhase.ToolCall,
            ToolName = toolName,
            ToolArguments = args,
        }, GuardrailPhase.ToolCall, ct);

    public Task<GuardrailResult> EvaluateToolResultAsync(string toolName, ToolResult result, CancellationToken ct = default) =>
        EvaluateAsync(new GuardrailContext
        {
            Content = result.Value?.ToString() ?? string.Empty,
            Phase = GuardrailPhase.ToolResult,
            ToolName = toolName,
            ToolResult = result,
        }, GuardrailPhase.ToolResult, ct);

    private async Task<GuardrailResult> EvaluateAsync(
        GuardrailContext context, GuardrailPhase phase, CancellationToken ct)
    {
        var applicableGuards = _guards.Where(g => g.Phase == phase).ToList();
        if (applicableGuards.Count == 0)
            return GuardrailResult.Allow();

        if (_runInParallel)
        {
            var tasks = applicableGuards.Select(g => g.EvaluateAsync(context, ct));
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var blocked = results.FirstOrDefault(r => !r.IsAllowed);
            return blocked ?? GuardrailResult.Allow();
        }

        string? lastSanitized = null;
        string? lastReason = null;

        foreach (var guard in applicableGuards)
        {
            var result = await guard.EvaluateAsync(context, ct).ConfigureAwait(false);
            if (!result.IsAllowed)
                return result;

            // If guard sanitized, update context for next guard
            if (result.SanitizedContent is not null)
            {
                context = context with { Content = result.SanitizedContent };
                lastSanitized = result.SanitizedContent;
                lastReason = result.Reason;
            }
        }

        return lastSanitized is not null
            ? GuardrailResult.Redact(lastReason ?? "Content sanitized", lastSanitized)
            : GuardrailResult.Allow();
    }
}
