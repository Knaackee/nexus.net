using FluentAssertions;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Events;
using Nexus.Core.Tools;
using Xunit;

namespace Nexus.Skills.Tests;

public sealed class SkillDefinitionTests
{
    [Fact]
    public void ApplyTo_Appends_Prompt_And_Merges_Tools()
    {
        var skill = new SkillDefinition
        {
            Name = "coding",
            SystemPrompt = "Use tools when useful.",
            ToolNames = ["file_read", "shell"],
        };

        var agent = skill.ApplyTo(new AgentDefinition
        {
            Name = "Assistant",
            SystemPrompt = "You are helpful.",
            ToolNames = ["shell", "grep"],
        });

        agent.SystemPrompt.Should().Be("You are helpful." + Environment.NewLine + Environment.NewLine + "Use tools when useful.");
        agent.ToolNames.Should().Equal(["shell", "grep", "file_read"]);
    }

    [Fact]
    public void Catalog_Resolves_By_Name()
    {
        var catalog = new SkillCatalog();
        catalog.Register(new SkillDefinition { Name = "coding", Description = "Coding assistant." });

        var skill = catalog.Resolve("coding");

        skill.Should().NotBeNull();
        skill!.Description.Should().Be("Coding assistant.");
    }

    [Fact]
    public void Catalog_Finds_Relevant_Skills_For_User_Request()
    {
        var catalog = new SkillCatalog();
        catalog.Register(new SkillDefinition
        {
            Name = "csharp",
            Description = "C# coding conventions",
            WhenToUse = "When writing or reviewing C# code and tests",
        });
        catalog.Register(new SkillDefinition
        {
            Name = "docs",
            Description = "Documentation writing",
            WhenToUse = "When updating markdown docs",
        });

        var relevant = catalog.FindRelevant("Please review this C# implementation and its tests");

        relevant.Should().NotBeEmpty();
        relevant[0].Name.Should().Be("csharp");
    }

    [Fact]
    public async Task MarkdownSkillLoader_Loads_Skill_From_Frontmatter()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "coding.md"), """
---
name: coding
description: Coding conventions
whenToUse: When changing C# code
model: gpt-4.1
tools:
  - file_read
  - shell
---
Use minimal patches and verify important changes.
""");

        var loader = new MarkdownSkillLoader();

        var skills = loader.LoadFromDirectory(root, SkillSource.Project, optional: false);

        skills.Should().ContainSingle();
        var skill = skills[0];
        skill.Name.Should().Be("coding");
        skill.ToolNames.Should().Equal(["file_read", "shell"]);
        skill.ModelId.Should().Be("gpt-4.1");
        skill.WhenToUse.Should().Be("When changing C# code");
        skill.SystemPrompt.Should().Contain("Use minimal patches");
        skill.Source.Should().Be(SkillSource.Project);
    }

    [Fact]
    public void MarkdownSkillLoader_MissingOptionalDirectory_ReturnsEmpty()
    {
        var loader = new MarkdownSkillLoader();

        var skills = loader.LoadFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), optional: true);

        skills.Should().BeEmpty();
    }

    [Fact]
    public async Task SkillInjectionMiddleware_Applies_Relevant_Skill_To_Task()
    {
        var catalog = new SkillCatalog();
        catalog.Register(new SkillDefinition
        {
            Name = "csharp",
            SystemPrompt = "Use project C# conventions.",
            ToolNames = ["file_read", "shell"],
            WhenToUse = "When changing or reviewing C# code",
        });

        var middleware = new SkillInjectionMiddleware(catalog, new SkillInjectionOptions());
        var task = AgentTask.Create("Review this C# code") with
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", SystemPrompt = "Base prompt.", ToolNames = ["grep"] },
        };

        AgentTask? capturedTask = null;
        var result = await middleware.InvokeAsync(task, new TestAgentContext(), (innerTask, _, _) =>
        {
            capturedTask = innerTask;
            return Task.FromResult(AgentResult.Success("ok"));
        }, CancellationToken.None);

        result.Status.Should().Be(AgentResultStatus.Success);
        capturedTask.Should().NotBeNull();
        capturedTask!.AgentDefinition.Should().NotBeNull();
        capturedTask.AgentDefinition!.SystemPrompt.Should().Contain("Base prompt.");
        capturedTask.AgentDefinition.SystemPrompt.Should().Contain("Use project C# conventions.");
        capturedTask.AgentDefinition.ToolNames.Should().Equal(["grep", "file_read", "shell"]);
        capturedTask.Metadata.Should().ContainKey(SkillInjectionMiddleware.ActiveSkillsMetadataKey);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexus-skill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestAgentContext : IAgentContext
    {
        public IAgent Agent { get; } = new TestAgent();
        public IToolRegistry Tools { get; } = new DefaultToolRegistry();
        public IConversationStore? Conversations => null;
        public IWorkingMemory? WorkingMemory => null;
        public IMessageBus? MessageBus => null;
        public IApprovalGate? ApprovalGate => null;
        public IBudgetTracker? Budget => null;
        public ISecretProvider? Secrets => null;
        public CorrelationContext Correlation { get; } = CorrelationContext.New();

        public IChatClient GetChatClient(string? name = null)
            => throw new NotSupportedException();

        public Task<IAgent> SpawnChildAsync(AgentDefinition definition, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestAgent : IAgent
    {
        public AgentId Id { get; } = AgentId.New();
        public string Name => "assistant";
        public AgentState State => AgentState.Idle;

        public Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
            => Task.FromResult(AgentResult.Success("ok"));

        public async IAsyncEnumerable<AgentEvent> ExecuteStreamingAsync(AgentTask task, IAgentContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new AgentCompletedEvent(Id, AgentResult.Success("ok"));
            await Task.CompletedTask;
        }
    }
}