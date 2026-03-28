using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;

namespace Nexus.Orchestration.Checkpointing;

public static class CheckpointingServiceCollectionExtensions
{
    public static CheckpointBuilder UseInMemory(this CheckpointBuilder builder)
    {
        builder.Services.AddSingleton<ICheckpointStore, InMemoryCheckpointStore>();
        builder.Services.AddSingleton<ISnapshotSerializer, JsonSnapshotSerializer>();
        return builder;
    }
}
