using System.Text.Json;
using FluentAssertions;
using Nexus.Core.Agents;
using Nexus.Core.Tools;

namespace Nexus.Core.Tests;

public class ToolResultTests
{
    [Fact]
    public void Success_Creates_Correct_Result()
    {
        var result = ToolResult.Success("data");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("data");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_Creates_Correct_Result()
    {
        var result = ToolResult.Failure("error msg");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("error msg");
    }

    [Fact]
    public void Denied_Creates_Correct_Result()
    {
        var result = ToolResult.Denied("not allowed");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("DENIED");
        result.Error.Should().Contain("not allowed");
    }
}

public class LambdaToolTests
{
    [Fact]
    public void Constructor_Sets_Name_And_Description()
    {
        var tool = new LambdaTool("test_tool", "A test tool",
            (_, _, _) => Task.FromResult(ToolResult.Success("ok")));

        tool.Name.Should().Be("test_tool");
        tool.Description.Should().Be("A test tool");
    }

    [Fact]
    public void Constructor_Throws_On_Null_Name()
    {
        var act = () => new LambdaTool(null!, "desc",
            (_, _, _) => Task.FromResult(ToolResult.Success("ok")));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_Invokes_Delegate()
    {
        var called = false;
        var tool = new LambdaTool("t", "d", (input, ctx, ct) =>
        {
            called = true;
            return Task.FromResult(ToolResult.Success("result"));
        });

        var result = await tool.ExecuteAsync(default, null!, CancellationToken.None);
        called.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("result");
    }
}

public class DefaultToolRegistryTests
{
    [Fact]
    public void Register_And_Resolve()
    {
        var registry = new DefaultToolRegistry();
        var tool = new LambdaTool("my_tool", "desc",
            (_, _, _) => Task.FromResult(ToolResult.Success("ok")));

        registry.Register(tool);
        var resolved = registry.Resolve("my_tool");

        resolved.Should().NotBeNull();
        resolved!.Name.Should().Be("my_tool");
    }

    [Fact]
    public void Resolve_Returns_Null_For_Unknown()
    {
        var registry = new DefaultToolRegistry();
        registry.Resolve("nonexistent").Should().BeNull();
    }

    [Fact]
    public void ListAll_Returns_All_Registered_Tools()
    {
        var registry = new DefaultToolRegistry();
        registry.Register(new LambdaTool("a", "d", (_, _, _) => Task.FromResult(ToolResult.Success("1"))));
        registry.Register(new LambdaTool("b", "d", (_, _, _) => Task.FromResult(ToolResult.Success("2"))));

        registry.ListAll().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_Is_Case_Insensitive()
    {
        var registry = new DefaultToolRegistry();
        registry.Register(new LambdaTool("MyTool", "desc",
            (_, _, _) => Task.FromResult(ToolResult.Success("ok"))));

        registry.Resolve("mytool").Should().NotBeNull();
        registry.Resolve("MYTOOL").Should().NotBeNull();
    }

    [Fact]
    public void BindToolsToAgent_Limits_Tools()
    {
        var registry = new DefaultToolRegistry();
        registry.Register(new LambdaTool("a", "d", (_, _, _) => Task.FromResult(ToolResult.Success("1"))));
        registry.Register(new LambdaTool("b", "d", (_, _, _) => Task.FromResult(ToolResult.Success("2"))));
        registry.Register(new LambdaTool("c", "d", (_, _, _) => Task.FromResult(ToolResult.Success("3"))));

        var agentId = AgentId.New();
        registry.BindToolsToAgent(agentId, ["a", "c"]);

        var tools = registry.ListForAgent(agentId);
        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().BeEquivalentTo(["a", "c"]);
    }

    [Fact]
    public void ListForAgent_Returns_All_When_No_Binding()
    {
        var registry = new DefaultToolRegistry();
        registry.Register(new LambdaTool("a", "d", (_, _, _) => Task.FromResult(ToolResult.Success("1"))));
        registry.Register(new LambdaTool("b", "d", (_, _, _) => Task.FromResult(ToolResult.Success("2"))));

        var tools = registry.ListForAgent(AgentId.New());
        tools.Should().HaveCount(2);
    }
}

public class ToolAnnotationsTests
{
    [Fact]
    public void Defaults_Are_False_And_Free()
    {
        var annotations = new ToolAnnotations();
        annotations.IsReadOnly.Should().BeFalse();
        annotations.IsIdempotent.Should().BeFalse();
        annotations.IsDestructive.Should().BeFalse();
        annotations.RequiresApproval.Should().BeFalse();
        annotations.CostCategory.Should().Be(ToolCostCategory.Free);
    }
}
