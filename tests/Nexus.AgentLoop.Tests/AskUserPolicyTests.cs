using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Orchestration.Defaults;
using Nexus.Sessions;
using Nexus.Testing.Mocks;
using Xunit;

namespace Nexus.AgentLoop.Tests;

/// <summary>
/// Tests for the ask_user prompt policy:
/// - Policy is injected into system prompt only when ask_user is in the tool list.
/// - Ambiguous-intent scenario: agent calls ask_user before acting.
/// - Risky-action scenario: agent calls ask_user confirm before executing destructive action.
/// </summary>
public sealed class AskUserPolicyTests
{
    private static readonly string[] FileOptions = ["Program.cs", "Startup.cs"];

    // -------------------------------------------------------------------------
    // Policy injection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Policy_Is_Injected_Into_SystemPrompt_When_AskUser_Tool_Is_Available()
    {
        var client = new FakeChatClient("done");
        using var services = BuildServices(client, toolNames: ["ask_user"]);
        var loop = services.GetRequiredService<IAgentLoop>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user"] },
            UserInput = "help",
        }));

        var systemMessage = client.ReceivedMessages.Single()
            .FirstOrDefault(m => m.Role == ChatRole.System);

        systemMessage.Should().NotBeNull();
        systemMessage!.Text.Should().Contain("ask_user");
        systemMessage.Text.Should().Contain("interpretations");
        systemMessage.Text.Should().Contain("irreversible");
    }

    [Fact]
    public async Task Policy_Is_NOT_Injected_When_AskUser_Tool_Is_Not_In_ToolNames()
    {
        var client = new FakeChatClient("done");
        using var services = BuildServices(client, toolNames: []);
        var loop = services.GetRequiredService<IAgentLoop>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition
            {
                Name = "assistant",
                SystemPrompt = "You are helpful.",
                ToolNames = [],          // no ask_user
            },
            UserInput = "help",
        }));

        var systemMessage = client.ReceivedMessages.Single()
            .FirstOrDefault(m => m.Role == ChatRole.System);

        systemMessage.Should().NotBeNull();
        systemMessage!.Text.Should().NotContain("interpretations");
        systemMessage.Text.Should().NotContain("irreversible");
    }

    [Fact]
    public async Task Policy_Appends_To_Existing_UserOwned_SystemPrompt()
    {
        const string userPrompt = "You are a coding assistant. Only write C#.";
        var client = new FakeChatClient("done");
        using var services = BuildServices(client, toolNames: ["ask_user"]);
        var loop = services.GetRequiredService<IAgentLoop>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition
            {
                Name = "assistant",
                ToolNames = ["ask_user"],
                SystemPrompt = userPrompt,
            },
            UserInput = "write a function",
        }));

        var systemMessage = client.ReceivedMessages.Single()
            .Single(m => m.Role == ChatRole.System);

        systemMessage.Text.Should().Contain(userPrompt);
        systemMessage.Text.Should().Contain("ask_user");
    }

    // -------------------------------------------------------------------------
    // Ambiguous intent: agent calls ask_user (select) before acting
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AmbiguousIntent_AskUser_SelectQuestion_Emitted_Before_Final_Answer()
    {
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("q-1", "ask_user", new Dictionary<string, object?>
            {
                ["type"] = "select",
                ["question"] = "Which file should I update?",
                ["options"] = (object)FileOptions,
            }))
            .WithResponse("Updated Program.cs as requested.");

        using var services = BuildServices(client, toolNames: ["ask_user"]);
        services.GetRequiredService<IToolRegistry>().Register(MockTool.AlwaysReturns("ask_user", "Program.cs"));
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user"] },
            UserInput = "Update the startup file",
        }))
        {
            events.Add(evt);
        }

        var inputRequested = events.OfType<UserInputRequestedLoopEvent>().Single();
        inputRequested.Request.InputType.Should().Be("select");
        inputRequested.Request.Question.Should().Be("Which file should I update?");
        inputRequested.Request.Options.Should().ContainInOrder("Program.cs", "Startup.cs");

        // ask_user must come before the final text output
        var askIndex = events.IndexOf(inputRequested);
        var textEvents = events.OfType<TextChunkLoopEvent>().ToList();
        textEvents.Should().NotBeEmpty();
        events.IndexOf(textEvents.First()).Should().BeGreaterThan(askIndex);
    }

    [Fact]
    public async Task AmbiguousIntent_AskUser_ConfirmQuestion_Emitted_Before_Final_Answer()
    {
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("q-2", "ask_user", new Dictionary<string, object?>
            {
                ["type"] = "confirm",
                ["question"] = "Do you want to overwrite the existing file?",
            }))
            .WithResponse("File overwritten.");

        using var services = BuildServices(client, toolNames: ["ask_user"]);
        services.GetRequiredService<IToolRegistry>().Register(MockTool.AlwaysReturns("ask_user", "yes"));
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user"] },
            UserInput = "Write the config",
        }))
        {
            events.Add(evt);
        }

        var inputRequested = events.OfType<UserInputRequestedLoopEvent>().Single();
        inputRequested.Request.InputType.Should().Be("confirm");
        inputRequested.Request.Question.Should().Be("Do you want to overwrite the existing file?");
    }

    [Fact]
    public async Task AmbiguousIntent_AskUser_InputTypeAlias_Select_Emitted_Before_Final_Answer()
    {
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("q-3", "ask_user", new Dictionary<string, object?>
            {
                ["inputType"] = "SELECT",
                ["question"] = "Pick target file",
                ["options"] = (object)FileOptions,
            }))
            .WithResponse("Updated file.");

        using var services = BuildServices(client, toolNames: ["ask_user"]);
        services.GetRequiredService<IToolRegistry>().Register(MockTool.AlwaysReturns("ask_user", "Program.cs"));
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user"] },
            UserInput = "Update config file",
        }))
        {
            events.Add(evt);
        }

        var inputRequested = events.OfType<UserInputRequestedLoopEvent>().Single();
        inputRequested.Request.InputType.Should().Be("select");
        inputRequested.Request.Options.Should().ContainInOrder("Program.cs", "Startup.cs");
    }

    [Fact]
    public async Task AmbiguousIntent_AskUser_Unknown_Type_Does_Not_Emit_UserInput_Request()
    {
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("q-4", "ask_user", new Dictionary<string, object?>
            {
                ["type"] = "dropdown",
                ["question"] = "Pick target file",
                ["options"] = (object)FileOptions,
            }))
            .WithResponse("I cannot ask that question type.");

        using var services = BuildServices(client, toolNames: ["ask_user"]);
        services.GetRequiredService<IToolRegistry>().Register(MockTool.AlwaysReturns("ask_user", "Program.cs"));
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user"] },
            UserInput = "Update config file",
        }))
        {
            events.Add(evt);
        }

        events.OfType<UserInputRequestedLoopEvent>().Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Risky action: agent confirms via ask_user before destructive step
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RiskyAction_AskUser_Confirm_Then_Execute_Emits_Events_In_Order()
    {
        // Turn 1: agent asks for confirmation
        // Turn 2: after user says "yes", agent proceeds
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("confirm-1", "ask_user", new Dictionary<string, object?>
            {
                ["type"] = "confirm",
                ["question"] = "This will delete all logs. Proceed?",
            }))
            .WithFunctionCallResponse(new FunctionCallContent("delete-1", "delete_logs", new Dictionary<string, object?> { }))
            .WithResponse("All logs deleted.");

        var deleteTool = MockTool.AlwaysReturns("delete_logs", "done");

        using var services = BuildServices(client, toolNames: ["ask_user", "delete_logs"]);
        var registry = services.GetRequiredService<IToolRegistry>();
        registry.Register(MockTool.AlwaysReturns("ask_user", "yes"));
        registry.Register(deleteTool);
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user", "delete_logs"] },
            UserInput = "Delete all application logs",
        }))
        {
            events.Add(evt);
        }

        // Confirm event must come before the delete tool call
        var confirmEvent = events.OfType<UserInputRequestedLoopEvent>().Single();
        var toolCallEvents = events.OfType<ToolCallStartedLoopEvent>().ToList();

        confirmEvent.Request.InputType.Should().Be("confirm");
        toolCallEvents.Should().Contain(e => e.ToolName == "delete_logs");

        var confirmIndex = events.IndexOf(confirmEvent);
        var deleteIndex = events.IndexOf(toolCallEvents.First(e => e.ToolName == "delete_logs"));
        deleteIndex.Should().BeGreaterThan(confirmIndex);

        // Loop must complete successfully
        events.OfType<LoopCompletedEvent>().Single().FinalResult.Status.Should().Be(AgentResultStatus.Success);
    }

    [Fact]
    public async Task RiskyAction_AskUser_Deny_Stops_Before_Destructive_Tool()
    {
        // Agent asks, user says "no", agent reports cancellation without calling destructive tool
        var client = new FakeChatClient()
            .WithFunctionCallResponse(new FunctionCallContent("confirm-2", "ask_user", new Dictionary<string, object?>
            {
                ["type"] = "confirm",
                ["question"] = "Drop the production database?",
            }))
            .WithResponse("Cancelled. The database was not dropped.");

        var dropTool = MockTool.AlwaysReturns("drop_db", "dropped");

        using var services = BuildServices(client, toolNames: ["ask_user", "drop_db"]);
        var registry = services.GetRequiredService<IToolRegistry>();
        registry.Register(MockTool.AlwaysReturns("ask_user", "no"));
        registry.Register(dropTool);
        var loop = services.GetRequiredService<IAgentLoop>();

        var events = new List<AgentLoopEvent>();
        await foreach (var evt in loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition { Name = "assistant", ToolNames = ["ask_user", "drop_db"] },
            UserInput = "Drop the production database",
        }))
        {
            events.Add(evt);
        }

        events.OfType<UserInputRequestedLoopEvent>().Should().ContainSingle();
        events.OfType<ToolCallStartedLoopEvent>()
            .Should().NotContain(e => e.ToolName == "drop_db");
        events.OfType<LoopCompletedEvent>().Single().FinalResult.Status.Should().Be(AgentResultStatus.Success);
        dropTool.ReceivedInputs.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // E2E: policy in system prompt is visible to the LLM
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_SystemPrompt_Sent_To_LLM_Contains_Policy_When_AskUser_Available()
    {
        var client = new FakeChatClient("ok");
        using var services = BuildServices(client, toolNames: ["ask_user"]);
        services.GetRequiredService<IToolRegistry>().Register(MockTool.AlwaysReturns("ask_user", "yes"));
        var loop = services.GetRequiredService<IAgentLoop>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition
            {
                Name = "assistant",
                ToolNames = ["ask_user"],
                SystemPrompt = "You are a deployment assistant.",
            },
            UserInput = "Deploy to prod",
        }));

        // The actual ChatMessage array sent to the LLM must contain the policy
        var receivedTurn = client.ReceivedMessages.Single();
        var systemText = receivedTurn.Single(m => m.Role == ChatRole.System).Text ?? "";

        systemText.Should().Contain("You are a deployment assistant.");
        systemText.Should().Contain("ask_user");
        systemText.Should().Contain("irreversible");
        systemText.Should().Contain("confirm");
    }

    [Fact]
    public async Task E2E_SystemPrompt_Sent_To_LLM_Has_No_Policy_When_AskUser_Not_Available()
    {
        var client = new FakeChatClient("ok");
        using var services = BuildServices(client, toolNames: []);
        var loop = services.GetRequiredService<IAgentLoop>();

        await DrainAsync(loop.RunAsync(new AgentLoopOptions
        {
            AgentDefinition = new AgentDefinition
            {
                Name = "assistant",
                ToolNames = [],
                SystemPrompt = "You are a deployment assistant.",
            },
            UserInput = "Deploy to prod",
        }));

        var receivedTurn = client.ReceivedMessages.Single();
        var systemText = receivedTurn.Single(m => m.Role == ChatRole.System).Text ?? "";

        // Policy markers must be absent
        systemText.Should().NotContain("interpretations");
        systemText.Should().NotContain("irreversible", because: "policy must not be injected without ask_user tool");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ServiceProvider BuildServices(FakeChatClient client, IReadOnlyList<string>? toolNames = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(client);
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        services.AddSingleton<IApprovalGate, AutoApproveGate>();
        services.AddSingleton<IAgentPool, DefaultAgentPool>();
        services.AddSingleton<InMemorySessionStore>();
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<InMemorySessionStore>());
        services.AddSingleton<ISessionTranscript>(sp => sp.GetRequiredService<InMemorySessionStore>());
        services.AddSingleton<IAgentLoop, DefaultAgentLoop>();
        return services.BuildServiceProvider();
    }

    private static async Task DrainAsync(IAsyncEnumerable<AgentLoopEvent> stream)
    {
        await foreach (var _ in stream) { }
    }
}
