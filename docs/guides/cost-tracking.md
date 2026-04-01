# Cost Tracking

`Nexus.CostTracking` adds provider-agnostic token and price aggregation around the registered `IChatClient`.

It works in two steps:

1. Register model pricing in USD per million tokens.
2. Resolve `ICostTracker` to inspect cumulative usage and estimated cost.

## Register Cost Tracking

```csharp
using Nexus.CostTracking;

services.AddNexus(nexus =>
{
    nexus.UseChatClient(_ => myChatClient);
    nexus.AddCostTracking(c => c
        .AddModel("gpt-4o", input: 2.50m, output: 10.00m)
        .AddModel("gpt-4o-mini", input: 0.15m, output: 0.60m));
    nexus.AddOrchestration(o => o.UseDefaults());
});
```

`Nexus.CostTracking` wraps the existing `IChatClient` registration. When the provider includes usage metadata in `ChatResponse` or `ChatResponseUpdate`, the wrapper extracts token counts and records them automatically.

`AddCostTracking(...)` also registers the default `IBudgetTracker`, so agent budgets such as `MaxCostUsd`, `MaxInputTokens`, and `MaxOutputTokens` can be enforced by the built-in orchestration middleware.

## Read Aggregated Usage

```csharp
var tracker = sp.GetRequiredService<ICostTracker>();
var snapshot = await tracker.GetSnapshotAsync();

Console.WriteLine(snapshot.TotalInputTokens);
Console.WriteLine(snapshot.TotalOutputTokens);
Console.WriteLine(snapshot.TotalCost);
```

## Result Metadata

When you execute through `ChatAgent` or the default orchestrator, the same tracked values are copied onto `AgentResult`:

```csharp
var taskResult = result.TaskResults.Values.First();

Console.WriteLine(taskResult.TokenUsage?.TotalInputTokens);
Console.WriteLine(taskResult.TokenUsage?.TotalOutputTokens);
Console.WriteLine(taskResult.TokenUsage?.TotalTokens);
Console.WriteLine(taskResult.EstimatedCost);
```

This makes it possible to inspect per-task usage without querying the global tracker.

## Budget Enforcement

Budget enforcement depends on provider usage metadata. If the wrapped `IChatClient` returns usage in `ChatResponse` or `ChatResponseUpdate`, Nexus can stop an agent once its configured budget is exhausted.

```csharp
var agent = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Budgeted",
    Budget = new AgentBudget { MaxCostUsd = 0.25m },
});

var result = await orchestrator.ExecuteSequenceAsync([
    AgentTask.Create("Summarize this") with { AssignedAgent = agent.Id }
]);

var taskResult = result.TaskResults.Values.First();
Console.WriteLine(taskResult.Status); // Success or BudgetExceeded
```

If the budget is exceeded mid-stream, Nexus completes the task with `AgentResultStatus.BudgetExceeded` and preserves the last known token usage and estimated cost on the result.

Per-model totals are available through `snapshot.Models`:

```csharp
var gpt4o = snapshot.Models["gpt-4o"];
Console.WriteLine(gpt4o.Requests);
Console.WriteLine(gpt4o.TotalCost);
```

## Unknown Models

If a response contains usage but the model is missing from the pricing table, Nexus still records the token counts and sets `HasUnknownPricing = true`. In that case the estimated cost remains unchanged for the unknown model instead of guessing.

## Streaming Behavior

For streaming clients, usage is collected from `ChatResponseUpdate`. Many providers emit cumulative usage in the final update; Nexus keeps the largest observed values for each token counter to avoid double-counting those streams.

## Troubleshooting

### Budget is never exceeded

`MaxCostUsd` enforcement only works when the wrapped `IChatClient` emits usage metadata in `ChatResponse` or `ChatResponseUpdate`.

If a provider omits usage entirely, Nexus can still execute the request, but it cannot calculate per-call cost and therefore cannot enforce cost-based budgets.

Check these signals:

- `ICostTracker.GetSnapshotAsync()` shows token totals greater than zero
- completed `AgentResult` instances contain `TokenUsage` and `EstimatedCost`
- streaming runs emit `TokenUsageEvent`

If all three stay empty, the provider integration needs to expose usage fields such as `Usage`, `UsageDetails`, or `AdditionalProperties["Usage"]`.

### Cost totals stay at zero

Two common causes:

1. The provider returned usage, but no pricing entry exists for the resolved model ID.
2. The provider returned no usage metadata at all.

In the first case, `HasUnknownPricing` becomes `true` on the snapshot. In the second case, token totals remain zero.

### Streaming returns text but no per-task cost

Many providers send usage only in the final streaming update. If you wrap a custom `IChatClient`, make sure the final update includes usage metadata or that the final buffered `ChatResponse` does.