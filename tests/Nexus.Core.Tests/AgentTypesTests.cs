using System.Text.Json;
using FluentAssertions;
using Nexus.Core.Agents;
using Nexus.Core.Tools;

namespace Nexus.Core.Tests;

public class AgentIdTests
{
    [Fact]
    public void New_Creates_Unique_Ids()
    {
        var id1 = AgentId.New();
        var id2 = AgentId.New();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void Parse_Roundtrips()
    {
        var original = AgentId.New();
        var parsed = AgentId.Parse(original.Value.ToString());
        parsed.Should().Be(original);
    }

    [Fact]
    public void ToString_Returns_Short_Hex()
    {
        var id = AgentId.New();
        id.ToString().Should().HaveLength(8);
    }

    [Fact]
    public void JsonSerialization_Roundtrips()
    {
        var id = AgentId.New();
        var json = JsonSerializer.Serialize(id);
        var deserialized = JsonSerializer.Deserialize<AgentId>(json);
        deserialized.Should().Be(id);
    }

    [Fact]
    public void Implicit_String_Conversion()
    {
        var id = AgentId.New();
        string str = id;
        str.Should().HaveLength(8);
    }
}

public class TaskIdTests
{
    [Fact]
    public void New_Creates_Unique_Ids()
    {
        var id1 = TaskId.New();
        var id2 = TaskId.New();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void JsonSerialization_Roundtrips()
    {
        var id = TaskId.New();
        var json = JsonSerializer.Serialize(id);
        var deserialized = JsonSerializer.Deserialize<TaskId>(json);
        deserialized.Should().Be(id);
    }
}

public class AgentResultTests
{
    [Fact]
    public void Success_Creates_Correct_Result()
    {
        var result = AgentResult.Success("hello");
        result.Status.Should().Be(AgentResultStatus.Success);
        result.Text.Should().Be("hello");
    }

    [Fact]
    public void Failed_Creates_Correct_Result()
    {
        var result = AgentResult.Failed("oops");
        result.Status.Should().Be(AgentResultStatus.Failed);
        result.Text.Should().Be("oops");
    }

    [Fact]
    public void Cancelled_Creates_Correct_Result()
    {
        var result = AgentResult.Cancelled();
        result.Status.Should().Be(AgentResultStatus.Cancelled);
    }

    [Fact]
    public void Timeout_Creates_Correct_Result()
    {
        var result = AgentResult.Timeout("too slow");
        result.Status.Should().Be(AgentResultStatus.Timeout);
        result.Text.Should().Be("too slow");
    }
}

public class AgentTaskTests
{
    [Fact]
    public void Create_Sets_Id_And_Description()
    {
        var task = AgentTask.Create("Do something");
        task.Id.Value.Should().NotBeEmpty();
        task.Description.Should().Be("Do something");
    }

    [Fact]
    public void Metadata_Defaults_To_Empty()
    {
        var task = AgentTask.Create("test");
        task.Metadata.Should().BeEmpty();
    }
}

public class AgentBudgetTests
{
    [Fact]
    public void Default_Values_Are_Null()
    {
        var budget = new AgentBudget();
        budget.MaxInputTokens.Should().BeNull();
        budget.MaxOutputTokens.Should().BeNull();
        budget.MaxCostUsd.Should().BeNull();
        budget.MaxIterations.Should().BeNull();
        budget.MaxToolCalls.Should().BeNull();
    }

    [Fact]
    public void Can_Set_All_Properties()
    {
        var budget = new AgentBudget(
            MaxInputTokens: 1000,
            MaxOutputTokens: 2000,
            MaxCostUsd: 5.0m,
            MaxIterations: 10,
            MaxToolCalls: 20);

        budget.MaxInputTokens.Should().Be(1000);
        budget.MaxOutputTokens.Should().Be(2000);
        budget.MaxCostUsd.Should().Be(5.0m);
        budget.MaxIterations.Should().Be(10);
        budget.MaxToolCalls.Should().Be(20);
    }
}
