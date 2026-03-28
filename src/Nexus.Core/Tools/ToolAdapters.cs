using System.Text.Json;
using Microsoft.Extensions.AI;
using Nexus.Core.Events;

namespace Nexus.Core.Tools;

public sealed class AIFunctionToolAdapter : ITool
{
    private readonly AIFunction _function;

    public AIFunctionToolAdapter(AIFunction function)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
    }

    public string Name => _function.Name;
    public string Description => _function.Description;

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct)
    {
        try
        {
            var args = new AIFunctionArguments();
            if (input.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in input.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()!,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null!,
                        _ => prop.Value.GetRawText()
                    };
                }
            }

            var result = await _function.InvokeAsync(args, ct);
            return ToolResult.Success(result ?? "null");
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }
}

public sealed class NexusToolAIFunctionAdapter : AIFunction
{
    private readonly ITool _tool;

    public NexusToolAIFunctionAdapter(ITool tool)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
    }

    public override string Name => _tool.Name;
    public override string Description => _tool.Description;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var jsonArgs = arguments.Count > 0
            ? JsonSerializer.SerializeToElement(
                arguments.ToDictionary(k => k.Key, k => k.Value))
            : JsonDocument.Parse("{}").RootElement;

        var dummyContext = new MinimalToolContext();
        var result = await _tool.ExecuteAsync(jsonArgs, dummyContext, cancellationToken);
        return result.IsSuccess ? result.Value : result.Error;
    }

    private sealed class MinimalToolContext : IToolContext
    {
        public Agents.AgentId AgentId => default;
        public IToolRegistry Tools => DefaultToolRegistry.Empty;
        public Contracts.ISecretProvider? Secrets => null;
        public Contracts.IBudgetTracker? Budget => null;
        public Contracts.CorrelationContext Correlation => new()
        {
            TraceId = Guid.NewGuid().ToString("N"),
            SpanId = Guid.NewGuid().ToString("N")[..16]
        };
    }
}

public static class ToolExtensions
{
    public static ITool AsNexusTool(this AIFunction function)
        => new AIFunctionToolAdapter(function);

    public static AIFunction AsAIFunction(this ITool tool)
        => new NexusToolAIFunctionAdapter(tool);
}
