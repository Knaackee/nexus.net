using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;

namespace Nexus.Workflows.Dsl;

public static class WorkflowDslBuilderExtensions
{
    public static IServiceCollection AddWorkflowDsl(
        this IServiceCollection services,
        Action<WorkflowDslBuilder>? configure = null)
    {
        services.AddSingleton<IWorkflowLoader, DefaultWorkflowLoader>();
        services.AddSingleton<IWorkflowValidator, DefaultWorkflowValidator>();
        services.AddSingleton<IVariableResolver, DefaultVariableResolver>();
        services.AddSingleton<IConditionEvaluator, SimpleConditionEvaluator>();

        var registry = new DefaultAgentTypeRegistry();
        services.AddSingleton<IAgentTypeRegistry>(registry);

        var builder = new WorkflowDslBuilder(services, registry);
        configure?.Invoke(builder);

        return services;
    }
}

public sealed class WorkflowDslBuilder
{
    private readonly IServiceCollection _services;
    private readonly DefaultAgentTypeRegistry _registry;

    internal WorkflowDslBuilder(IServiceCollection services, DefaultAgentTypeRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    public WorkflowDslBuilder RegisterAgentType(
        string typeName,
        Func<AgentConfig, IServiceProvider, Nexus.Core.Agents.IAgent> factory)
    {
        _registry.Register(typeName, factory);
        return this;
    }

    public WorkflowDslBuilder UseConditionEvaluator<T>() where T : class, IConditionEvaluator
    {
        _services.AddSingleton<IConditionEvaluator, T>();
        return this;
    }

    public WorkflowDslBuilder UseVariableResolver<T>() where T : class, IVariableResolver
    {
        _services.AddSingleton<IVariableResolver, T>();
        return this;
    }
}
