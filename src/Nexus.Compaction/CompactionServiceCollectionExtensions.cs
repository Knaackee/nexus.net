using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Configuration;

namespace Nexus.Compaction;

public static class CompactionServiceCollectionExtensions
{
    public static CompactionBuilder Configure(this CompactionBuilder builder, Action<CompactionOptions> configure)
    {
        var options = GetOrCreateOptions(builder.Services);
        configure(options);
        RegisterCore(builder.Services, options);
        return builder;
    }

    public static CompactionBuilder UseDefaults(this CompactionBuilder builder)
    {
        RegisterCore(builder.Services, GetOrCreateOptions(builder.Services));
        return builder;
    }

    private static void RegisterCore(IServiceCollection services, CompactionOptions options)
    {
        services.TryAddSingleton(options);
        services.TryAddSingleton<ITokenCounter, DefaultTokenCounter>();
        services.TryAddSingleton<IContextWindowMonitor, DefaultContextWindowMonitor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICompactionStrategy, MicroCompactionStrategy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICompactionStrategy, SummaryCompactionStrategy>());
        services.TryAddSingleton<ICompactionService, DefaultCompactionService>();
        services.TryAddSingleton<ICompactionRecallService, DefaultCompactionRecallService>();
    }

    private static CompactionOptions GetOrCreateOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(CompactionOptions))?.ImplementationInstance as CompactionOptions;
        if (existing is not null)
            return existing;

        var created = new CompactionOptions();
        services.AddSingleton(created);
        return created;
    }
}