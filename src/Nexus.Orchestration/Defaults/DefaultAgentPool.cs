using System.Collections.Concurrent;
using System.Reactive.Subjects;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;

namespace Nexus.Orchestration.Defaults;

public sealed class DefaultAgentPool : IAgentPool, IDisposable
{
    private readonly ConcurrentDictionary<AgentId, IAgent> _agents = new();
    private readonly Subject<AgentLifecycleEvent> _lifecycle = new();
    private readonly IServiceProvider _serviceProvider;

    public DefaultAgentPool(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IReadOnlyList<IAgent> ActiveAgents => _agents.Values.ToList();
    public IObservable<AgentLifecycleEvent> Lifecycle => _lifecycle;

    public Task<IAgent> SpawnAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var client = definition.ChatClientName is not null
            ? _serviceProvider.GetRequiredKeyedService<IChatClient>(definition.ChatClientName)
            : _serviceProvider.GetRequiredService<IChatClient>();

        var agent = new ChatAgent(definition.Name, client, new ChatAgentOptions
        {
            SystemPrompt = definition.SystemPrompt,
            ToolNames = definition.ToolNames,
            MaxIterations = definition.Budget?.MaxIterations ?? 25,
        });

        _agents[agent.Id] = agent;
        _lifecycle.OnNext(new AgentLifecycleEvent(agent.Id, AgentState.Created, AgentState.Idle, DateTimeOffset.UtcNow));
        return Task.FromResult<IAgent>(agent);
    }

    public Task PauseAsync(AgentId id, CancellationToken ct = default)
    {
        // Pausing would require cooperative cancellation in the running agent
        _lifecycle.OnNext(new AgentLifecycleEvent(id, AgentState.Running, AgentState.Paused, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task ResumeAsync(AgentId id, CancellationToken ct = default)
    {
        _lifecycle.OnNext(new AgentLifecycleEvent(id, AgentState.Paused, AgentState.Running, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task KillAsync(AgentId id, CancellationToken ct = default)
    {
        if (_agents.TryRemove(id, out _))
        {
            _lifecycle.OnNext(new AgentLifecycleEvent(id, AgentState.Running, AgentState.Disposed, DateTimeOffset.UtcNow));
        }

        return Task.CompletedTask;
    }

    public async Task DrainAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (_agents.Values.Any(a => a.State == AgentState.Running) && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(100, cts.Token).ConfigureAwait(false);
        }
    }

    public async Task CheckpointAndStopAllAsync(ICheckpointStore store, CancellationToken ct = default)
    {
        foreach (var agent in _agents.Values.ToList())
        {
            await KillAsync(agent.Id, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _lifecycle.OnCompleted();
        _lifecycle.Dispose();
        _agents.Clear();
    }
}
