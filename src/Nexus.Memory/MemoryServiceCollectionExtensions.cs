using Microsoft.Extensions.DependencyInjection;
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
}
