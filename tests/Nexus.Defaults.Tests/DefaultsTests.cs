using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexus.AgentLoop;
using Nexus.Commands;
using Nexus.Compaction;
using Nexus.Configuration;
using Nexus.Core.Agents;
using Nexus.CostTracking;
using Nexus.Defaults;
using Nexus.Orchestration;
using Nexus.Sessions;
using Nexus.Skills;
using Nexus.Testing.Mocks;
using Nexus.Core.Tools;

namespace Nexus.Defaults.Tests;

public sealed class NexusDefaultsTests
{
    [Fact]
    public async Task CreateDefault_RunAsync_Streams_Events()
    {
        await using var host = global::Nexus.Nexus.CreateDefault(new FakeChatClient("hello from defaults"));

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in host.RunAsync("Say hello"))
            events.Add(evt);

        events.Should().Contain(evt => evt is LoopStartedEvent);
        events.Should().Contain(evt => evt is TextChunkLoopEvent);
        events.OfType<LoopCompletedEvent>().Single().FinalResult.Status.Should().Be(AgentResultStatus.Success);
    }

    [Fact]
    public async Task CreateDefault_Registers_Core_Default_Services()
    {
        await using var host = global::Nexus.Nexus.CreateDefault(new FakeChatClient("ok"));

        host.Services.GetRequiredService<IAgentLoop>().Should().NotBeNull();
        host.Services.GetRequiredService<ICommandCatalog>().Should().NotBeNull();
        host.Services.GetRequiredService<ISkillCatalog>().Should().NotBeNull();
        host.Services.GetRequiredService<ISessionStore>().Should().NotBeNull();
        host.Services.GetRequiredService<ICompactionService>().Should().NotBeNull();
        host.Services.GetRequiredService<ICostTracker>().Should().NotBeNull();
        host.Services.GetRequiredService<INexusConfigurationProvider>().Should().NotBeNull();
        host.Services.GetRequiredService<IToolRegistry>().Resolve("file_read").Should().NotBeNull();
    }

    [Fact]
    public async Task CreateDefault_Allows_Custom_Default_Agent_Configuration()
    {
        await using var host = global::Nexus.Nexus.CreateDefault(new FakeChatClient("custom response"), options =>
        {
            options.DefaultAgentDefinition = new AgentDefinition
            {
                Name = "CustomAgent",
                SystemPrompt = "Use the custom path.",
            };
            options.SessionTitle = "custom-session";
        });

        host.DefaultAgentDefinition.Name.Should().Be("CustomAgent");

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in host.RunAsync("Run custom"))
            events.Add(evt);

        events.OfType<LoopCompletedEvent>().Single().FinalResult.Text.Should().Be("custom response");
    }

    [Fact]
    public async Task CreateDefault_Disposes_Without_Error()
    {
        var host = global::Nexus.Nexus.CreateDefault(new FakeChatClient("disposable"));
        var act = async () => await host.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateDefault_With_Factory_Overload_Works()
    {
        await using var host = global::Nexus.Nexus.CreateDefault(
            _ => new FakeChatClient("from factory"));

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in host.RunAsync("test"))
            events.Add(evt);

        events.OfType<LoopCompletedEvent>().Single().FinalResult.Text.Should().Be("from factory");
    }

    [Fact]
    public async Task CreateDefault_Registers_Memory_And_Orchestration_Services()
    {
        await using var host = global::Nexus.Nexus.CreateDefault(new FakeChatClient("ok"));

        host.Services.GetRequiredService<IOrchestrator>().Should().NotBeNull();
    }

    [Fact]
    public async Task CreateDefault_RunAsync_With_AgentLoopOptions_Overload()
    {
        await using var host = global::Nexus.Nexus.CreateDefault(new FakeChatClient("options-response"));

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in host.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "Custom", SystemPrompt = "test" },
            UserInput = "run",
        }))
            events.Add(evt);

        events.OfType<LoopCompletedEvent>().Single().FinalResult.Text.Should().Be("options-response");
    }

    [Fact]
    public void NexusDefaultsOptions_Has_Sensible_Defaults()
    {
        var options = new NexusDefaultsOptions();

        options.DefaultAgentDefinition.Name.Should().Be("DefaultAgent");
        options.SessionTitle.Should().Be("default");
        options.ConfigureConfiguration.Should().BeNull();
        options.ConfigureOrchestration.Should().BeNull();
    }
}