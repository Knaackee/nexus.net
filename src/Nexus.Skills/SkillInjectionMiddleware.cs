using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Events;
using Nexus.Core.Pipeline;

namespace Nexus.Skills;

public sealed class SkillInjectionMiddleware : IAgentMiddleware
{
    public const string ActiveSkillsMetadataKey = "nexus.activeSkills";

    private readonly ISkillCatalog _catalog;
    private readonly SkillInjectionOptions _options;

    public SkillInjectionMiddleware(ISkillCatalog catalog, SkillInjectionOptions options)
    {
        _catalog = catalog;
        _options = options;
    }

    public Task<AgentResult> InvokeAsync(
        AgentTask task,
        IAgentContext ctx,
        AgentExecutionDelegate next,
        CancellationToken ct)
        => next(ApplySkills(task, ctx), ctx, ct);

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        AgentTask task,
        IAgentContext ctx,
        StreamingAgentExecutionDelegate next,
        CancellationToken ct = default)
        => next(ApplySkills(task, ctx), ctx, ct);

    private AgentTask ApplySkills(AgentTask task, IAgentContext context)
    {
        if (!_options.Enabled)
            return task;

        var relevantSkills = _catalog.FindRelevant(task.Description, task.Messages, _options.MaxSkills);
        if (relevantSkills.Count == 0)
            return task;

        var baseDefinition = task.AgentDefinition ?? new AgentDefinition { Name = context.Agent.Name };
        var mergedDefinition = relevantSkills.Aggregate(baseDefinition, static (current, skill) => skill.ApplyTo(current));

        var metadata = new Dictionary<string, object>(task.Metadata)
        {
            [ActiveSkillsMetadataKey] = relevantSkills.Select(static skill => skill.Name).ToArray(),
        };

        return task with
        {
            AgentDefinition = mergedDefinition,
            Metadata = metadata,
        };
    }
}