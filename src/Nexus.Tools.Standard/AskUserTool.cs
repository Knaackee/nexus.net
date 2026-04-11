using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class AskUserTool : ITool
{
    private readonly IServiceProvider _services;
    private static readonly Meter Meter = new("Nexus.Tools.Standard.AskUser", "1.0.0");
    private static readonly Counter<long> RequestTypeSourceCounter = Meter.CreateCounter<long>("ask_user.request_type_source");
    private static readonly Counter<long> RequestTypeUnknownCounter = Meter.CreateCounter<long>("ask_user.request_type_unknown");
    private static readonly Counter<long> RequestTypeMismatchCounter = Meter.CreateCounter<long>("ask_user.request_type_mismatch");
    private static readonly Counter<long> ValidationMissingOptionsCounter = Meter.CreateCounter<long>("ask_user.validation_failed_missing_options");
    private static readonly Counter<long> ResolvedInputTypeCounter = Meter.CreateCounter<long>("ask_user.resolved_input_type");

    public AskUserTool(IServiceProvider services)
    {
        _services = services;
    }

    public string Name => "ask_user";

    public string Description => "Asks the user a typed question through the host application's interaction channel.";

    public ToolAnnotations? Annotations => new()
    {
        IsReadOnly = true,
        IsIdempotent = false,
        IsOpenWorld = true,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        var interaction = _services.GetService<IUserInteraction>();
        if (interaction is null)
            return ToolResult.Failure("No IUserInteraction service is registered.");

        try
        {
            var question = CreateQuestion(input);
            var options = new UserInteractionOptions
            {
                DefaultOnTimeout = ToolJson.GetOptionalString(input, "defaultOnTimeout"),
                IsOptional = ToolJson.GetOptionalBool(input, "isOptional"),
                Timeout = ToolJson.GetOptionalInt(input, "timeoutSeconds") is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                Context = new InteractionContext(context.AgentId.ToString(), ToolJson.GetOptionalString(input, "reason")),
            };

            var response = await interaction.AskAsync(question, options, ct).ConfigureAwait(false);
            return ToolResult.Success(response);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }

    private static UserQuestion CreateQuestion(JsonElement input)
    {
        var type = ResolveInputType(input);
        var text = ToolJson.GetRequiredString(input, "question");
        var options = ToolJson.GetOptionalStringArray(input, "options");

        if (type is "select" or "multiSelect" && options.Count == 0)
        {
            ValidationMissingOptionsCounter.Add(1,
                new KeyValuePair<string, object?>("input_type", type));
            throw new InvalidOperationException($"Property 'options' must be a non-empty string array when type is '{type}'.");
        }

        return type switch
        {
            "confirm" => new ConfirmQuestion(text),
            "select" => new SelectQuestion(text, options),
            "multiSelect" => new MultiSelectQuestion(text, options),
            "secret" => new SecretQuestion(text),
            _ => new FreeTextQuestion(text, ToolJson.GetOptionalString(input, "placeholder")),
        };
    }

    private static string ResolveInputType(JsonElement input)
    {
        var rawType = ToolJson.GetOptionalString(input, "type");
        var rawInputType = ToolJson.GetOptionalString(input, "inputType");
        var hasType = !string.IsNullOrWhiteSpace(rawType);
        var hasInputType = !string.IsNullOrWhiteSpace(rawInputType);

        if (hasType)
        {
            var normalized = NormalizeInputType(rawType!);
            if (normalized is null)
            {
                RequestTypeUnknownCounter.Add(1,
                    new KeyValuePair<string, object?>("source", "type"),
                    new KeyValuePair<string, object?>("value", rawType));
                throw new InvalidOperationException($"Unsupported ask_user type '{rawType}'. Allowed values: freeText, confirm, select, multiSelect, secret.");
            }

            RequestTypeSourceCounter.Add(1,
                new KeyValuePair<string, object?>("source", "type"));

            if (hasInputType)
            {
                var normalizedInputType = NormalizeInputType(rawInputType!);
                if (normalizedInputType is null || !string.Equals(normalized, normalizedInputType, StringComparison.Ordinal))
                {
                    RequestTypeMismatchCounter.Add(1,
                        new KeyValuePair<string, object?>("type", rawType),
                        new KeyValuePair<string, object?>("inputType", rawInputType));
                }
            }

            ResolvedInputTypeCounter.Add(1,
                new KeyValuePair<string, object?>("input_type", normalized));
            return normalized;
        }

        if (hasInputType)
        {
            var normalized = NormalizeInputType(rawInputType!);
            if (normalized is null)
            {
                RequestTypeUnknownCounter.Add(1,
                    new KeyValuePair<string, object?>("source", "inputType"),
                    new KeyValuePair<string, object?>("value", rawInputType));
                throw new InvalidOperationException($"Unsupported ask_user inputType '{rawInputType}'. Allowed values: freeText, confirm, select, multiSelect, secret.");
            }

            RequestTypeSourceCounter.Add(1,
                new KeyValuePair<string, object?>("source", "inputType"));
            ResolvedInputTypeCounter.Add(1,
                new KeyValuePair<string, object?>("input_type", normalized));
            return normalized;
        }

        RequestTypeSourceCounter.Add(1,
            new KeyValuePair<string, object?>("source", "default"));
        ResolvedInputTypeCounter.Add(1,
            new KeyValuePair<string, object?>("input_type", "freeText"));
        return "freeText";
    }

    private static string? NormalizeInputType(string value)
        => value.ToLowerInvariant() switch
        {
            "freetext" => "freeText",
            "confirm" => "confirm",
            "select" => "select",
            "multiselect" => "multiSelect",
            "secret" => "secret",
            _ => null,
        };
}