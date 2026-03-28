using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;
using Nexus.Core.Agents;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Testing.Mocks;

namespace Nexus.Testing.Helpers;

/// <summary>
/// Pre-configured test host that sets up a complete Nexus environment for testing.
/// </summary>
public sealed class NexusTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public FakeChatClient ChatClient { get; }
    public IServiceProvider Services => _serviceProvider;

    private NexusTestHost(ServiceProvider sp, FakeChatClient chatClient)
    {
        _serviceProvider = sp;
        ChatClient = chatClient;
    }

    public static NexusTestHost Create(Action<NexusBuilder>? configure = null, params string[] chatResponses)
    {
        var chatClient = new FakeChatClient(chatResponses);
        var services = new ServiceCollection();

        services.AddNexus(n =>
        {
            n.UseChatClient(_ => chatClient);
            configure?.Invoke(n);
        });

        var sp = services.BuildServiceProvider();
        return new NexusTestHost(sp, chatClient);
    }

    public T GetRequiredService<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();

    public ValueTask DisposeAsync()
    {
        return _serviceProvider.DisposeAsync();
    }
}
