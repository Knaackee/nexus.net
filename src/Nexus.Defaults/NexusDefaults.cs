using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Commands;
using Nexus.Compaction;
using Nexus.Configuration;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.CostTracking;
using Nexus.Memory;
using Nexus.Orchestration;
using Nexus.Permissions;
using Nexus.Protocols.Mcp;
using Nexus.Sessions;
using Nexus.Skills;
using Nexus.Tools.Standard;

namespace Nexus.Defaults
{
    public sealed class NexusDefaultsOptions
    {
        public AgentDefinition DefaultAgentDefinition { get; set; } = new()
        {
            Name = "DefaultAgent",
            SystemPrompt = "You are a helpful assistant. Use tools when useful.",
        };

        public string? SessionTitle { get; set; } = "default";

        public Action<ConfigurationBuilder>? ConfigureConfiguration { get; set; }

        public Action<OrchestrationBuilder>? ConfigureOrchestration { get; set; }

        public Action<PermissionBuilder>? ConfigurePermissions { get; set; }

        public Action<CompactionBuilder>? ConfigureCompaction { get; set; }

        public Action<MemoryBuilder>? ConfigureMemory { get; set; }

        public Action<SessionBuilder>? ConfigureSessions { get; set; }

        public Action<AgentLoopBuilder>? ConfigureAgentLoop { get; set; }

        public Action<CostTrackingBuilder>? ConfigureCostTracking { get; set; }

        public Action<CommandBuilder>? ConfigureCommands { get; set; }

        public Action<McpBuilder>? ConfigureMcp { get; set; }

        public Action<SkillBuilder>? ConfigureSkills { get; set; }

        public Action<StandardToolBuilder>? ConfigureTools { get; set; }
    }

    public static class NexusDefaultsBuilderExtensions
    {
        public static NexusBuilder AddDefaults(this NexusBuilder builder, Action<NexusDefaultsOptions>? configure = null)
        {
            var options = GetOrCreateOptions(builder.Services);
            configure?.Invoke(options);

            builder.AddConfiguration(configuration =>
            {
                configuration.UseDefaults();
                options.ConfigureConfiguration?.Invoke(configuration);
            });

            builder.AddOrchestration(orchestration =>
            {
                orchestration.UseDefaults();
                options.ConfigureOrchestration?.Invoke(orchestration);
            });

            builder.AddCostTracking(costTracking =>
            {
                costTracking.Configure(_ => { });
                options.ConfigureCostTracking?.Invoke(costTracking);
            });

            builder.AddCommands(commands =>
            {
                commands.UseDefaults();
                options.ConfigureCommands?.Invoke(commands);
            });

            builder.AddMcp(mcp =>
            {
                mcp.UseDefaults();
                options.ConfigureMcp?.Invoke(mcp);
            });

            builder.AddSkills(skills =>
            {
                skills.UseDefaults();
                options.ConfigureSkills?.Invoke(skills);
            });

            builder.AddPermissions(permissions =>
            {
                permissions.UsePreset(PermissionPreset.Interactive);
                permissions.UseConsolePrompt();
                options.ConfigurePermissions?.Invoke(permissions);
            });

            builder.AddCompaction(compaction =>
            {
                compaction.UseDefaults();
                options.ConfigureCompaction?.Invoke(compaction);
            });

            builder.AddMemory(memory =>
            {
                memory.UseInMemory();
                options.ConfigureMemory?.Invoke(memory);
            });

            builder.AddSessions(sessions =>
            {
                sessions.UseInMemory();
                options.ConfigureSessions?.Invoke(sessions);
            });

            builder.AddAgentLoop(agentLoop =>
            {
                agentLoop.UseDefaults();
                options.ConfigureAgentLoop?.Invoke(agentLoop);
            });

            builder.AddStandardTools(tools =>
            {
                tools.UseConsoleInteraction();
                options.ConfigureTools?.Invoke(tools);
            });

            return builder;
        }

        private static NexusDefaultsOptions GetOrCreateOptions(IServiceCollection services)
        {
            var existing = services.FirstOrDefault(service => service.ServiceType == typeof(NexusDefaultsOptions))?.ImplementationInstance as NexusDefaultsOptions;
            if (existing is not null)
                return existing;

            var created = new NexusDefaultsOptions();
            services.AddSingleton(created);
            return created;
        }
    }

    public sealed class NexusDefaultHost : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly NexusDefaultsOptions _options;

        internal NexusDefaultHost(ServiceProvider serviceProvider, NexusDefaultsOptions options)
        {
            _serviceProvider = serviceProvider;
            _options = options;
        }

        public IServiceProvider Services => _serviceProvider;

        public AgentDefinition DefaultAgentDefinition => _options.DefaultAgentDefinition;

        public IAsyncEnumerable<AgentLoopEvent> RunAsync(string userInput, CancellationToken ct = default)
            => _serviceProvider.GetRequiredService<IAgentLoop>().RunAsync(new AgentLoopOptions
            {
                AgentDefinition = _options.DefaultAgentDefinition,
                UserInput = userInput,
                SessionTitle = _options.SessionTitle,
            }, ct);

        public IAsyncEnumerable<AgentLoopEvent> RunAsync(AgentLoopOptions options, CancellationToken ct = default)
            => _serviceProvider.GetRequiredService<IAgentLoop>().RunAsync(options, ct);

        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();
    }
}

namespace Nexus
{
    public static class Nexus
    {
        public static global::Nexus.Defaults.NexusDefaultHost CreateDefault(
            IChatClient chatClient,
            Action<global::Nexus.Defaults.NexusDefaultsOptions>? configure = null)
            => CreateDefault(_ => chatClient, configure);

        public static global::Nexus.Defaults.NexusDefaultHost CreateDefault(
            Func<IServiceProvider, IChatClient> chatClientFactory,
            Action<global::Nexus.Defaults.NexusDefaultsOptions>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddNexus(builder =>
            {
                builder.UseChatClient(chatClientFactory);
                global::Nexus.Defaults.NexusDefaultsBuilderExtensions.AddDefaults(builder, configure);
            });

            var serviceProvider = services.BuildServiceProvider();
            return new global::Nexus.Defaults.NexusDefaultHost(
                serviceProvider,
                serviceProvider.GetRequiredService<global::Nexus.Defaults.NexusDefaultsOptions>());
        }
    }
}