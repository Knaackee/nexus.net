namespace Nexus.Core.Agents;

public record AgentBudget(
    int? MaxInputTokens = null,
    int? MaxOutputTokens = null,
    decimal? MaxCostUsd = null,
    int? MaxIterations = null,
    int? MaxToolCalls = null);
