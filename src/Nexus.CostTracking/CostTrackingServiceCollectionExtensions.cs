using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;

namespace Nexus.CostTracking;

public static class CostTrackingServiceCollectionExtensions
{
    public static CostTrackingBuilder Configure(this CostTrackingBuilder builder, Action<CostTrackingOptions> configure)
    {
        var options = GetOrCreateOptions(builder.Services);
        configure(options);
        RegisterCore(builder.Services);
        WrapChatClients(builder.Services);
        return builder;
    }

    public static CostTrackingBuilder AddModel(
        this CostTrackingBuilder builder,
        string modelId,
        decimal input,
        decimal output,
        decimal cacheRead = 0,
        decimal cacheWrite = 0)
        => builder.Configure(options => options.AddModel(modelId, input, output, cacheRead, cacheWrite));

    private static void RegisterCore(IServiceCollection services)
    {
        services.AddSingleton(GetOrCreateOptions(services));
        services.AddSingleton<IModelPricingProvider, DefaultModelPricingProvider>();
        services.AddSingleton<ICostTracker, DefaultCostTracker>();
        services.TryAddSingleton<IBudgetTracker, DefaultBudgetTracker>();
    }

    private static CostTrackingOptions GetOrCreateOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(s => s.ServiceType == typeof(CostTrackingOptions))?.ImplementationInstance as CostTrackingOptions;
        if (existing is not null)
            return existing;

        var options = new CostTrackingOptions();
        services.AddSingleton(options);
        return options;
    }

    private static void WrapChatClients(IServiceCollection services)
    {
        for (int i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(IChatClient))
                continue;

            services[i] = WrapDescriptor(descriptor);
        }
    }

    private static ServiceDescriptor WrapDescriptor(ServiceDescriptor descriptor)
    {
        if (descriptor.IsKeyedService)
        {
            return ServiceDescriptor.DescribeKeyed(
                descriptor.ServiceType,
                descriptor.ServiceKey,
                (sp, key) => new CostTrackingChatClient(
                    CreateInner(descriptor, sp, key),
                    sp.GetRequiredService<ICostTracker>(),
                    sp.GetRequiredService<IModelPricingProvider>(),
                    key?.ToString()),
                descriptor.Lifetime);
        }

        return ServiceDescriptor.Describe(
            descriptor.ServiceType,
            sp => new CostTrackingChatClient(
                CreateInner(descriptor, sp, null),
                sp.GetRequiredService<ICostTracker>(),
                sp.GetRequiredService<IModelPricingProvider>()),
            descriptor.Lifetime);
    }

    private static IChatClient CreateInner(ServiceDescriptor descriptor, IServiceProvider sp, object? key)
    {
        if (descriptor.ImplementationInstance is IChatClient instance)
            return instance;

        if (descriptor.ImplementationFactory is not null)
            return (IChatClient)descriptor.ImplementationFactory(sp)!;

        if (descriptor.KeyedImplementationFactory is not null)
            return (IChatClient)descriptor.KeyedImplementationFactory(sp, key)!;

        if (descriptor.ImplementationType is not null)
            return (IChatClient)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);

        if (descriptor.KeyedImplementationType is not null)
            return (IChatClient)ActivatorUtilities.CreateInstance(sp, descriptor.KeyedImplementationType);

        throw new InvalidOperationException("Unable to wrap IChatClient registration for cost tracking.");
    }
}