using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Contracts;
using Nexus.Core.Routing;
using Nexus.Core.Tools;

namespace Nexus.Core.Configuration;

public class NexusBuilder
{
    internal IServiceCollection Services { get; }

    public NexusBuilder(IServiceCollection services)
    {
        Services = services;
        Services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        Services.AddSingleton<IApprovalGate, AutoApproveGate>();
        Services.AddSingleton<IAuditLog, NullAuditLog>();
    }

    public NexusBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    public NexusBuilder UseChatClient(string name, Func<IServiceProvider, IChatClient> factory)
    {
        Services.AddKeyedSingleton<IChatClient>(name, (sp, _) => factory(sp));
        return this;
    }

    public NexusBuilder UseRouter<TRouter>() where TRouter : class, IChatClientRouter
    {
        Services.AddSingleton<IChatClientRouter, TRouter>();
        Services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<IChatClientRouter>());
        return this;
    }

    public NexusBuilder AddOrchestration(Action<OrchestrationBuilder>? configure = null)
    {
        var builder = new OrchestrationBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddMessaging(Action<MessagingBuilder>? configure = null)
    {
        var builder = new MessagingBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddGuardrails(Action<GuardrailBuilder>? configure = null)
    {
        var builder = new GuardrailBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddMemory(Action<MemoryBuilder>? configure = null)
    {
        var builder = new MemoryBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddCheckpointing(Action<CheckpointBuilder>? configure = null)
    {
        var builder = new CheckpointBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddMcp(Action<McpBuilder>? configure = null)
    {
        var builder = new McpBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddA2A(Action<A2ABuilder>? configure = null)
    {
        var builder = new A2ABuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddAgUi(Action<AgUiBuilder>? configure = null)
    {
        var builder = new AgUiBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddTools(Action<ToolBuilder>? configure = null)
    {
        var builder = new ToolBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddSecrets(Action<SecretBuilder>? configure = null)
    {
        var builder = new SecretBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddRateLimiting(Action<RateLimitBuilder>? configure = null)
    {
        var builder = new RateLimitBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddTelemetry(Action<TelemetryBuilder>? configure = null)
    {
        var builder = new TelemetryBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    public NexusBuilder AddAuditLog<TAuditLog>() where TAuditLog : class, IAuditLog
    {
        Services.AddSingleton<IAuditLog, TAuditLog>();
        return this;
    }

    public NexusBuilder AddApprovalGate<TGate>() where TGate : class, IApprovalGate
    {
        Services.AddSingleton<IApprovalGate, TGate>();
        return this;
    }
}

// Sub-builders — each package extends these with actual configuration methods.
public class OrchestrationBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class MessagingBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class GuardrailBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class MemoryBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class CheckpointBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class McpBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class A2ABuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class AgUiBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class ToolBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class SecretBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class RateLimitBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }
public class TelemetryBuilder(IServiceCollection services) { public IServiceCollection Services { get; } = services; }

public static class NexusServiceCollectionExtensions
{
    public static IServiceCollection AddNexus(
        this IServiceCollection services, Action<NexusBuilder> configure)
    {
        var builder = new NexusBuilder(services);
        configure(builder);
        return services;
    }
}
