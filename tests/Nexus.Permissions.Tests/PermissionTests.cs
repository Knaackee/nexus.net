using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Contracts;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;
using Nexus.Testing.Mocks;

namespace Nexus.Permissions.Tests;

public class RuleBasedPermissionHandlerTests
{
    [Fact]
    public async Task WildcardRule_Matches_MultipleTools()
    {
        var options = new PermissionOptions();
        options.Rules.Add(new ToolPermissionRule
        {
            Pattern = "file_*",
            Action = PermissionAction.Deny,
            Reason = "File tools blocked",
        });

        var handler = new RuleBasedPermissionHandler(options);
        var context = CreateToolContext();

        var readDecision = await handler.EvaluateAsync(
            MockTool.AlwaysReturns("file_read", "ok"),
            EmptyJson(),
            context);
        var writeDecision = await handler.EvaluateAsync(
            MockTool.AlwaysReturns("file_write", "ok"),
            EmptyJson(),
            context);

        readDecision.Should().BeOfType<PermissionDenied>();
        writeDecision.Should().BeOfType<PermissionDenied>();
    }

    [Fact]
    public async Task SourcePrecedence_Is_Managed_User_Project_Default()
    {
        var options = new PermissionOptions();
        options.Rules.Add(new ToolPermissionRule
        {
            Pattern = "shell",
            Action = PermissionAction.Allow,
            Source = PermissionRuleSource.Default,
        });
        options.Rules.Add(new ToolPermissionRule
        {
            Pattern = "shell",
            Action = PermissionAction.Deny,
            Source = PermissionRuleSource.Project,
        });
        options.Rules.Add(new ToolPermissionRule
        {
            Pattern = "shell",
            Action = PermissionAction.Allow,
            Source = PermissionRuleSource.User,
        });
        options.Rules.Add(new ToolPermissionRule
        {
            Pattern = "shell",
            Action = PermissionAction.Deny,
            Source = PermissionRuleSource.Managed,
            Reason = "Managed policy wins",
        });

        var handler = new RuleBasedPermissionHandler(options);
        var decision = await handler.EvaluateAsync(
            MockTool.AlwaysReturns("shell", "ok"),
            EmptyJson(),
            CreateToolContext());

        decision.Should().BeOfType<PermissionDenied>()
            .Which.Reason.Should().Be("Managed policy wins");
    }

    [Fact]
    public async Task InteractivePreset_Allows_ReadOnly_And_Asks_For_Write()
    {
        var options = new PermissionOptions();
        PermissionPresets.Apply(PermissionPreset.Interactive, options);
        var handler = new RuleBasedPermissionHandler(options);

        var readOnlyTool = MockTool.AlwaysReturns("grep", "ok")
            .WithAnnotations(new ToolAnnotations { IsReadOnly = true });
        var writeTool = MockTool.AlwaysReturns("edit_file", "ok")
            .WithAnnotations(new ToolAnnotations { IsReadOnly = false });

        var readDecision = await handler.EvaluateAsync(readOnlyTool, EmptyJson(), CreateToolContext());
        var writeDecision = await handler.EvaluateAsync(writeTool, EmptyJson(), CreateToolContext());

        readDecision.Should().BeOfType<PermissionGranted>();
        writeDecision.Should().BeOfType<PermissionAsk>();
    }

    [Fact]
    public async Task ReadOnlyPreset_Denies_NonReadOnly_Tools()
    {
        var options = new PermissionOptions();
        PermissionPresets.Apply(PermissionPreset.ReadOnly, options);
        var handler = new RuleBasedPermissionHandler(options);

        var decision = await handler.EvaluateAsync(
            MockTool.AlwaysReturns("shell", "ok").WithAnnotations(new ToolAnnotations { IsReadOnly = false }),
            EmptyJson(),
            CreateToolContext());

        decision.Should().BeOfType<PermissionDenied>();
    }

    [Fact]
    public async Task AllowAllPreset_Allows_Any_Tool()
    {
        var options = new PermissionOptions();
        PermissionPresets.Apply(PermissionPreset.AllowAll, options);
        var handler = new RuleBasedPermissionHandler(options);

        var decision = await handler.EvaluateAsync(
            MockTool.AlwaysReturns("shell", "ok"),
            EmptyJson(),
            CreateToolContext());

        decision.Should().BeOfType<PermissionGranted>();
    }

    private static IToolContext CreateToolContext()
    {
        var context = Substitute.For<IToolContext>();
        context.AgentId.Returns(AgentId.New());
        context.Correlation.Returns(new CorrelationContext { TraceId = "trace", SpanId = "span" });
        context.Tools.Returns(new DefaultToolRegistry());
        return context;
    }

    private static JsonElement EmptyJson() => JsonDocument.Parse("{}").RootElement;
}

public class PermissionToolMiddlewareTests
{
    [Fact]
    public async Task Denied_Does_Not_Invoke_Next()
    {
        var handler = Substitute.For<IToolPermissionHandler>();
        handler.EvaluateAsync(Arg.Any<ITool>(), Arg.Any<JsonElement>(), Arg.Any<IToolContext>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionDenied("blocked"));
        var prompt = Substitute.For<IPermissionPrompt>();
        var auditLog = Substitute.For<IAuditLog>();
        var middleware = new PermissionToolMiddleware(handler, prompt, auditLog);
        var tool = MockTool.AlwaysReturns("shell", "ok");
        var nextCalled = false;

        var result = await middleware.InvokeAsync(
            tool,
            JsonDocument.Parse("{}").RootElement,
            CreateToolContext(),
            (_, _, _, _) =>
            {
                nextCalled = true;
                return Task.FromResult(ToolResult.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("DENIED");
    }

    [Fact]
    public async Task Ask_Approved_Invokes_Next()
    {
        var handler = Substitute.For<IToolPermissionHandler>();
        handler.EvaluateAsync(Arg.Any<ITool>(), Arg.Any<JsonElement>(), Arg.Any<IToolContext>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionAsk("needs approval"));
        var prompt = Substitute.For<IPermissionPrompt>();
        prompt.PromptAsync(Arg.Any<ApprovalRequest>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalResult(true, "tester"));
        var auditLog = Substitute.For<IAuditLog>();
        var middleware = new PermissionToolMiddleware(handler, prompt, auditLog);
        var tool = MockTool.AlwaysReturns("shell", "ok");
        var nextCalled = false;

        var result = await middleware.InvokeAsync(
            tool,
            JsonDocument.Parse("{}").RootElement,
            CreateToolContext(),
            (_, _, _, _) =>
            {
                nextCalled = true;
                return Task.FromResult(ToolResult.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Ask_Denied_Returns_Denied_Result()
    {
        var handler = Substitute.For<IToolPermissionHandler>();
        handler.EvaluateAsync(Arg.Any<ITool>(), Arg.Any<JsonElement>(), Arg.Any<IToolContext>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionAsk("needs approval"));
        var prompt = Substitute.For<IPermissionPrompt>();
        prompt.PromptAsync(Arg.Any<ApprovalRequest>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalResult(false, Comment: "user denied"));
        var middleware = new PermissionToolMiddleware(handler, prompt, Substitute.For<IAuditLog>());

        var result = await middleware.InvokeAsync(
            MockTool.AlwaysReturns("shell", "ok"),
            JsonDocument.Parse("{}").RootElement,
            CreateToolContext(),
            (_, _, _, _) => Task.FromResult(ToolResult.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("user denied");
    }

    private static IToolContext CreateToolContext()
    {
        var context = Substitute.For<IToolContext>();
        context.AgentId.Returns(AgentId.New());
        context.Correlation.Returns(new CorrelationContext { TraceId = "trace", SpanId = "span" });
        context.Tools.Returns(new DefaultToolRegistry());
        return context;
    }
}

public class RuleBasedApprovalGateTests
{
    [Fact]
    public async Task Allow_Rule_Returns_Approved_Without_Prompt()
    {
        var options = new PermissionOptions { DefaultAction = PermissionAction.Deny };
        options.Rules.Add(new ToolPermissionRule
        {
            Pattern = "shell",
            Action = PermissionAction.Allow,
        });

        var prompt = Substitute.For<IPermissionPrompt>();
        var gate = new RuleBasedApprovalGate(options, prompt, Substitute.For<IAuditLog>());

        var result = await gate.RequestApprovalAsync(new ApprovalRequest("Execute shell", AgentId.New(), "shell"));

        result.IsApproved.Should().BeTrue();
        await prompt.DidNotReceiveWithAnyArgs().PromptAsync(default!, default, default);
    }

    [Fact]
    public async Task Deny_Rule_Returns_Denied_Without_Prompt()
    {
        var options = new PermissionOptions { DefaultAction = PermissionAction.Allow };
        options.Rules.Add(new ToolPermissionRule
        {
            Pattern = "shell",
            Action = PermissionAction.Deny,
            Reason = "policy",
        });

        var prompt = Substitute.For<IPermissionPrompt>();
        var gate = new RuleBasedApprovalGate(options, prompt, Substitute.For<IAuditLog>());

        var result = await gate.RequestApprovalAsync(new ApprovalRequest("Execute shell", AgentId.New(), "shell"));

        result.IsApproved.Should().BeFalse();
        result.Comment.Should().Be("policy");
        await prompt.DidNotReceiveWithAnyArgs().PromptAsync(default!, default, default);
    }

    [Fact]
    public async Task Ask_Rule_Delegates_To_Prompt()
    {
        var options = new PermissionOptions { DefaultAction = PermissionAction.Ask };
        var prompt = Substitute.For<IPermissionPrompt>();
        prompt.PromptAsync(Arg.Any<ApprovalRequest>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalResult(true, "human"));

        var gate = new RuleBasedApprovalGate(options, prompt, Substitute.For<IAuditLog>());

        var result = await gate.RequestApprovalAsync(new ApprovalRequest("Execute shell", AgentId.New(), "shell"));

        result.IsApproved.Should().BeTrue();
        await prompt.Received(1).PromptAsync(Arg.Any<ApprovalRequest>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }
}

public class PermissionBuilderTests
{
    [Fact]
    public void AddPermissions_Registers_Core_Services_And_Replaces_ApprovalGate()
    {
        var services = new ServiceCollection();

        services.AddNexus(nexus =>
        {
            nexus.AddPermissions(p => p.UsePreset(PermissionPreset.Interactive));
        });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolPermissionHandler>().Should().BeOfType<RuleBasedPermissionHandler>();
        provider.GetRequiredService<PermissionToolMiddleware>().Should().NotBeNull();
        provider.GetRequiredService<IApprovalGate>().Should().BeOfType<RuleBasedApprovalGate>();
        provider.GetRequiredService<IPermissionPrompt>().Should().BeOfType<NullPermissionPrompt>();
    }
}