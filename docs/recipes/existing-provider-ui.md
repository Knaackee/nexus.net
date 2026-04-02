# Recipe: Existing Provider UI

Use this when your application already has its own UI for provider and model selection and you want Nexus only for runtime execution.

## Not A Good Fit

Do not start here if Nexus itself should own chat state, session UX, or an interactive command surface. In those cases start with [Nexus CLI](../examples/nexus-cli.md) or a loop-based recipe.

## Good Fit

This recipe is a good fit if:

- your frontend already lets users choose provider, model, and related runtime options
- you do not want Nexus to own that UI
- you want to map those choices into `IChatClient`, `AgentDefinition`, and optional cost tracking

## Core Idea

Let your UI stay the source of truth for selection.

Nexus only needs the translated runtime values:

- which `IChatClient` to use
- which `ModelId` or named chat client is active
- optional pricing information for cost tracking
- optional loop, session, or compaction settings

## Recommended Split

| Concern | Owner |
|---------|-------|
| Provider/model picker UX | Your application UI |
| Provider SDK client creation | Your application composition layer |
| Model pricing table | Your application or `Nexus.CostTracking` registration |
| Agent execution, tools, sessions, routing | Nexus |

## Minimal Flow

```text
User selects provider/model in UI
  -> app maps selection to IChatClient + model/pricing settings
      -> Nexus executes with those runtime values
```

## Example UI Model

```csharp
public sealed record ProviderSelection(
    string Provider,
    string ModelId,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens);
```

## Mapping Into Nexus

```csharp
ProviderSelection selection = GetSelectionFromUi();

var services = new ServiceCollection();

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => CreateChatClient(selection));

    nexus.AddCostTracking(cost =>
    {
        if (selection.InputCostPerMillionTokens.HasValue && selection.OutputCostPerMillionTokens.HasValue)
        {
            cost.AddModel(
                selection.ModelId,
                selection.InputCostPerMillionTokens.Value,
                selection.OutputCostPerMillionTokens.Value);
        }
    });

    nexus.AddDefaults(options =>
    {
        options.DefaultAgentDefinition = new AgentDefinition
        {
            Name = "RuntimeAgent",
            ModelId = selection.ModelId,
            SystemPrompt = "You are a helpful assistant.",
        };
    });
});
```

The critical step is `CreateChatClient(selection)`. Nexus does not decide how your UI provider names map to concrete SDK clients. Your application does that translation once.

## Named Clients Variant

If your UI switches among a fixed provider/model matrix, named clients can be clearer:

```csharp
services.AddNexus(nexus =>
{
    nexus.UseChatClient("openai-gpt4o", _ => openAiClient);
    nexus.UseChatClient("ollama-qwen", _ => ollamaClient);
    nexus.AddDefaults();
});

var definition = new AgentDefinition
{
    Name = "UiSelectedAgent",
    ChatClientName = selection.Provider == "openai" ? "openai-gpt4o" : "ollama-qwen",
    ModelId = selection.ModelId,
};
```

## Cost Tracking Rule

If your UI already knows per-model pricing, hand it to `Nexus.CostTracking`.

If it does not, Nexus can still run without it. The only trade-off is that estimated USD cost will be missing or zero for unknown models.

## Common Mistake

Do not duplicate the provider/model picker inside Nexus.

That creates two separate configuration surfaces. Keep selection in your UI and treat Nexus as the execution engine beneath it.

## Related Recipes

- [Single Agent With Tools](single-agent-with-tools.md)
- [Chat Session With Memory](chat-session-with-memory.md)
- [Task System + Graph Brain](task-system-graph-brain.md)

## Read Next

- package map: [Package Index](../api/README.md)
- common default composition: [Nexus.Defaults](../api/nexus-defaults.md)