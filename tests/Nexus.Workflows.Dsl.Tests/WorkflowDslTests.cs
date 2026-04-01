using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Workflows.Dsl;

namespace Nexus.Workflows.Dsl.Tests;

public class DefaultWorkflowLoaderTests
{
    private readonly DefaultWorkflowLoader _loader = new();

    [Fact]
    public void LoadFromString_Json_Parses_Correctly()
    {
        var json = """
        {
            "id": "test-wf",
            "name": "Test Workflow",
            "nodes": [
                {
                    "id": "n1",
                    "name": "Node 1",
                    "description": "First node"
                }
            ],
            "edges": []
        }
        """;

        var definition = _loader.LoadFromString(json, "json");

        definition.Id.Should().Be("test-wf");
        definition.Name.Should().Be("Test Workflow");
        definition.Nodes.Should().HaveCount(1);
        definition.Nodes[0].Id.Should().Be("n1");
    }

    [Fact]
    public void LoadFromString_Json_With_RequiresApproval()
    {
        var json = """
        {
            "id": "wf-approval",
            "name": "Approval Workflow",
            "nodes": [
                {
                    "id": "n1",
                    "name": "Safe Node",
                    "description": "No approval needed"
                },
                {
                    "id": "n2",
                    "name": "Dangerous Node",
                    "description": "Needs approval",
                    "requiresApproval": true
                }
            ]
        }
        """;

        var definition = _loader.LoadFromString(json, "json");

        definition.Nodes[0].RequiresApproval.Should().BeFalse();
        definition.Nodes[1].RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public void RequiresApproval_DefaultsToFalse()
    {
        var node = new NodeDefinition
        {
            Id = "n1",
            Name = "Test",
            Description = "Test node"
        };

        node.RequiresApproval.Should().BeFalse();
    }

    [Fact]
    public void LoadFromString_Json_With_Edges()
    {
        var json = """
        {
            "id": "wf-2",
            "name": "Two Node",
            "nodes": [
                { "id": "a", "name": "A", "description": "First" },
                { "id": "b", "name": "B", "description": "Second" }
            ],
            "edges": [
                { "from": "a", "to": "b" }
            ]
        }
        """;

        var definition = _loader.LoadFromString(json, "json");

        definition.Edges.Should().HaveCount(1);
        definition.Edges[0].From.Should().Be("a");
        definition.Edges[0].To.Should().Be("b");
    }

    [Fact]
    public void LoadFromString_Unsupported_Format_Throws()
    {
        var act = () => _loader.LoadFromString("{}", "xml");
        act.Should().Throw<ArgumentException>().WithMessage("*Unsupported format*");
    }
}

public class DefaultWorkflowValidatorTests
{
    private readonly DefaultWorkflowValidator _validator = new();

    [Fact]
    public void Valid_Workflow_Returns_Ok()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf-1",
            Name = "Test",
            Nodes =
            [
                new NodeDefinition { Id = "n1", Name = "Node 1", Description = "First" },
                new NodeDefinition { Id = "n2", Name = "Node 2", Description = "Second" }
            ],
            Edges = [new EdgeDefinition { From = "n1", To = "n2" }]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Empty_Id_Fails()
    {
        var definition = new WorkflowDefinition
        {
            Id = "",
            Name = "Test",
            Nodes = [new NodeDefinition { Id = "n1", Name = "N", Description = "D" }]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ID"));
    }

    [Fact]
    public void No_Nodes_Fails()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf",
            Name = "Test",
            Nodes = []
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one node"));
    }

    [Fact]
    public void Duplicate_Node_Id_Fails()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf",
            Name = "Test",
            Nodes =
            [
                new NodeDefinition { Id = "dup", Name = "A", Description = "D" },
                new NodeDefinition { Id = "dup", Name = "B", Description = "D" }
            ]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate"));
    }

    [Fact]
    public void Edge_References_Unknown_Node_Fails()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf",
            Name = "Test",
            Nodes = [new NodeDefinition { Id = "n1", Name = "N", Description = "D" }],
            Edges = [new EdgeDefinition { From = "n1", To = "unknown" }]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("non-existing"));
    }

    [Fact]
    public void Self_Referencing_Edge_Fails()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf",
            Name = "Test",
            Nodes = [new NodeDefinition { Id = "n1", Name = "N", Description = "D" }],
            Edges = [new EdgeDefinition { From = "n1", To = "n1" }]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Self-referencing"));
    }

    [Fact]
    public void Cycle_Detection()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf",
            Name = "Test",
            Nodes =
            [
                new NodeDefinition { Id = "a", Name = "A", Description = "D" },
                new NodeDefinition { Id = "b", Name = "B", Description = "D" },
                new NodeDefinition { Id = "c", Name = "C", Description = "D" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "a", To = "b" },
                new EdgeDefinition { From = "b", To = "c" },
                new EdgeDefinition { From = "c", To = "a" }
            ]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cycle"));
    }

    [Fact]
    public void Linear_Graph_Has_No_Cycle()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf",
            Name = "Test",
            Nodes =
            [
                new NodeDefinition { Id = "a", Name = "A", Description = "D" },
                new NodeDefinition { Id = "b", Name = "B", Description = "D" },
                new NodeDefinition { Id = "c", Name = "C", Description = "D" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "a", To = "b" },
                new EdgeDefinition { From = "b", To = "c" }
            ]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Negative_Budget_Fails()
    {
        var definition = new WorkflowDefinition
        {
            Id = "wf",
            Name = "Test",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1", Name = "N", Description = "D",
                    Agent = new AgentConfig { Budget = new BudgetConfig { MaxCostUsd = -1.0m } }
                }
            ]
        };

        var result = _validator.Validate(definition);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("negative budget"));
    }
}

public class DefaultVariableResolverTests
{
    private readonly DefaultVariableResolver _resolver = new();

    [Fact]
    public void Resolves_Variables()
    {
        var vars = new Dictionary<string, object> { ["name"] = "World" };
        var result = _resolver.Resolve("Hello ${name}!", vars);
        result.Should().Be("Hello World!");
    }

    [Fact]
    public void Returns_Empty_String_Unchanged()
    {
        var result = _resolver.Resolve("", new Dictionary<string, object>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_Variables()
    {
        var vars = new Dictionary<string, object> { ["a"] = "1", ["b"] = "2" };
        var result = _resolver.Resolve("${a} + ${b}", vars);
        result.Should().Be("1 + 2");
    }
}

public class SimpleConditionEvaluatorTests
{
    private readonly SimpleConditionEvaluator _evaluator = new();

    [Fact]
    public void Empty_Expression_Returns_True()
    {
        var result = _evaluator.Evaluate("", AgentResult.Success("x"));
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluates_Success_Status()
    {
        var result = _evaluator.Evaluate("result.status == 'Success'", AgentResult.Success("ok"));
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluates_Failed_Status()
    {
        var result = _evaluator.Evaluate("result.status == 'Failed'", AgentResult.Failed("err"));
        result.Should().BeTrue();
    }

    [Fact]
    public void Status_Mismatch_Returns_False()
    {
        var result = _evaluator.Evaluate("result.status == 'Failed'", AgentResult.Success("ok"));
        result.Should().BeFalse();
    }
}

public class DefaultAgentTypeRegistryTests
{
    [Fact]
    public void Register_And_Create()
    {
        var registry = new DefaultAgentTypeRegistry();
        registry.Register("custom", (config, sp) => new FakeAgent(config));

        var config = new AgentConfig { Type = "custom" };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var sp = services.BuildServiceProvider();

        var agent = registry.Create(config, sp);
        agent.Should().NotBeNull();
        agent.Name.Should().Be("custom-agent");
    }

    [Fact]
    public void Create_Unknown_Type_Throws()
    {
        var registry = new DefaultAgentTypeRegistry();
        var config = new AgentConfig { Type = "nonexistent" };
        var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();

        var act = () => registry.Create(config, sp);
        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class FakeAgent : IAgent
    {
        public AgentId Id { get; } = AgentId.New();
        public string Name => "custom-agent";
        public AgentState State => AgentState.Idle;

        public FakeAgent(AgentConfig _) { }

        public Task<AgentResult> ExecuteAsync(AgentTask task, IAgentContext context, CancellationToken ct = default)
            => Task.FromResult(AgentResult.Success("fake"));

        public IAsyncEnumerable<Nexus.Core.Events.AgentEvent> ExecuteStreamingAsync(
            AgentTask task, IAgentContext context, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
