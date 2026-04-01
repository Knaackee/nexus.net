using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class AskUserTool : ITool
{
    private readonly IServiceProvider _services;

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
        var type = ToolJson.GetOptionalString(input, "type") ?? "freeText";
        var text = ToolJson.GetRequiredString(input, "question");
        return type switch
        {
            "confirm" => new ConfirmQuestion(text),
            "select" => new SelectQuestion(text, ToolJson.GetOptionalStringArray(input, "options")),
            "multiSelect" => new MultiSelectQuestion(text, ToolJson.GetOptionalStringArray(input, "options")),
            "secret" => new SecretQuestion(text),
            _ => new FreeTextQuestion(text, ToolJson.GetOptionalString(input, "placeholder")),
        };
    }
}