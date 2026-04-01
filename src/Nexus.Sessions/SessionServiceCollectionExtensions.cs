using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;

namespace Nexus.Sessions;

public static class SessionServiceCollectionExtensions
{
    public static SessionBuilder UseInMemory(this SessionBuilder builder)
    {
        builder.Services.AddSingleton<InMemorySessionStore>();
        builder.Services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<InMemorySessionStore>());
        builder.Services.AddSingleton<ISessionTranscript>(sp => sp.GetRequiredService<InMemorySessionStore>());
        return builder;
    }

    public static SessionBuilder UseFileSystem(this SessionBuilder builder, string baseDirectory)
    {
        builder.Services.AddSingleton<FileSessionStore>(_ => new FileSessionStore(baseDirectory));
        builder.Services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<FileSessionStore>());
        builder.Services.AddSingleton<ISessionTranscript>(sp => sp.GetRequiredService<FileSessionStore>());
        return builder;
    }
}