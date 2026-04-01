using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Compaction;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;

namespace Nexus.Memory;

public static class MemoryServiceCollectionExtensions
{
    public static MemoryBuilder UseInMemory(this MemoryBuilder builder)
    {
        builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        builder.Services.AddSingleton<IWorkingMemory, InMemoryWorkingMemory>();
        builder.Services.AddSingleton<ILongTermMemory, InMemoryLongTermMemory>();
        builder.Services.AddSingleton<IContextWindowManager, DefaultContextWindowManager>();
        return builder;
    }

    public static MemoryBuilder UseLongTermMemoryRecall(this MemoryBuilder builder, Action<LongTermMemoryRecallOptions>? configure = null)
    {
        var options = GetOrCreateLongTermRecallOptions(builder.Services);
        configure?.Invoke(options);
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICompactionRecallProvider, LongTermMemoryRecallProvider>());
        return builder;
    }

    private static LongTermMemoryRecallOptions GetOrCreateLongTermRecallOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(LongTermMemoryRecallOptions))?.ImplementationInstance as LongTermMemoryRecallOptions;
        if (existing is not null)
            return existing;

        var created = new LongTermMemoryRecallOptions();
        services.AddSingleton(created);
        return created;
    }
}
