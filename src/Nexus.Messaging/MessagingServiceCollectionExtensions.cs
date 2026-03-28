using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;

namespace Nexus.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static MessagingBuilder UseInMemory(this MessagingBuilder builder)
    {
        builder.Services.AddSingleton<IMessageBus, InMemoryMessageBus>();
        builder.Services.AddSingleton<ISharedState, InMemorySharedState>();
        builder.Services.AddSingleton<IDeadLetterQueue, InMemoryDeadLetterQueue>();
        return builder;
    }
}
