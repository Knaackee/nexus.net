using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Agents;
using Nexus.Core.Tools;
using Nexus.Orchestration;
using Nexus.Orchestration.Defaults;
using Nexus.Testing.Mocks;
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

public class DefaultWorkflowGraphCompilerTests
{
    [Fact]
    public void Compile_Maps_Workflow_To_Graph_And_Options()
    {
        var services = new ServiceCollection()
            .AddSingleton<IChatClient>(_ => new FakeChatClient("ok", "ok"))
            .AddSingleton<IToolRegistry, DefaultToolRegistry>()
            .AddSingleton<IAgentPool, DefaultAgentPool>()
            .AddSingleton<IOrchestrator, DefaultOrchestrator>()
            .AddWorkflowDsl()
            .BuildServiceProvider();

        using var scope = services;

        var compiler = scope.GetRequiredService<IWorkflowGraphCompiler>();
        var compiled = compiler.Compile(new WorkflowDefinition
        {
            Id = "parallel-review",
            Name = "Parallel Review",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "draft",
                    Name = "Draft",
                    Description = "Draft for ${topic}",
                    Agent = new AgentConfig
                    {
                        SystemPrompt = "Write about ${topic}",
                        Tools = ["search"],
                        Budget = new BudgetConfig { MaxCostUsd = 1.5m, MaxIterations = 2 }
                    }
                },
                new NodeDefinition
                {
                    Id = "review",
                    Name = "Review",
                    Description = "Review draft",
                    Agent = new AgentConfig { ContextWindow = new ContextWindowConfig { TrimStrategy = "KeepFirstAndLast", MaxTokens = 32000, TargetTokens = 24000 } }
                }
            ],
            Edges =
            [
                new EdgeDefinition
                {
                    From = "draft",
                    To = "review",
                    ContextPropagation = new ContextPropagationConfig { Strategy = "structured" }
                }
            ],
            Options = new WorkflowOptions
            {
                MaxConcurrentNodes = 4,
                GlobalTimeoutSeconds = 45,
                MaxTotalCostUsd = 5m,
                CheckpointStrategy = "manual"
            }
        }, new Dictionary<string, object> { ["topic"] = "Nexus" });

        compiled.Graph.Nodes.Should().HaveCount(2);
        compiled.Options.MaxConcurrentNodes.Should().Be(4);
        compiled.Options.GlobalTimeout.Should().Be(TimeSpan.FromSeconds(45));
        compiled.Options.MaxTotalCostUsd.Should().Be(5m);
        compiled.Options.CheckpointStrategy.Should().Be(CheckpointStrategy.Manual);

        var draft = compiled.Graph.Nodes.Single(node => node.Task.AgentDefinition!.Name == "Draft");
        draft.Task.Description.Should().Be("Draft for Nexus");
        draft.Task.AgentDefinition!.SystemPrompt.Should().Be("Write about Nexus");
        draft.Task.AgentDefinition!.ToolNames.Should().ContainSingle().Which.Should().Be("search");
        draft.Task.AgentDefinition!.Budget!.MaxCostUsd.Should().Be(1.5m);

        var review = compiled.Graph.Nodes.Single(node => node.Task.AgentDefinition!.Name == "Review");
        review.Dependencies.Should().ContainSingle().Which.Task.AgentDefinition!.Name.Should().Be("Draft");
        review.Task.AgentDefinition!.ContextWindow!.TrimStrategy.Should().Be(ContextTrimStrategy.KeepFirstAndLast);
    }

    [Theory]
    [InlineData("constant", BackoffType.Constant)]
    [InlineData("linear", BackoffType.Linear)]
    [InlineData("exponential", BackoffType.Exponential)]
    [InlineData("unknown", BackoffType.ExponentialWithJitter)]
    public void Compile_Maps_Backoff_Strategies(string backoffType, BackoffType expected)
    {
        using var services = CreateWorkflowServices();
        var compiler = services.GetRequiredService<IWorkflowGraphCompiler>();

        var compiled = compiler.Compile(new WorkflowDefinition
        {
            Id = "retry-map",
            Name = "Retry Map",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    Name = "Node",
                    Description = "Do work",
                    ErrorPolicy = new ErrorPolicyConfig
                    {
                        MaxRetries = 3,
                        BackoffType = backoffType,
                        FallbackChatClient = "fallback-client",
                        FallbackModelId = "fallback-model",
                        EscalateToHuman = true,
                        SendToDeadLetter = true,
                        MaxIterations = 7,
                        TimeoutSeconds = 12
                    }
                }
            ]
        });

        var task = compiled.Graph.Nodes.Single().Task;
        task.ErrorPolicy.Should().NotBeNull();
        task.ErrorPolicy!.Retry!.BackoffType.Should().Be(expected);
        task.ErrorPolicy.Fallback!.AlternateChatClientName.Should().Be("fallback-client");
        task.ErrorPolicy.Fallback.AlternateModelId.Should().Be("fallback-model");
        task.ErrorPolicy.EscalateToHuman.Should().BeTrue();
        task.ErrorPolicy.SendToDeadLetter.Should().BeTrue();
        task.ErrorPolicy.Timeout.Should().Be(TimeSpan.FromSeconds(12));
        task.AgentDefinition!.Timeout.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Theory]
    [InlineData("summarizeandtruncate", ContextTrimStrategy.SummarizeAndTruncate)]
    [InlineData("keep_first_and_last", ContextTrimStrategy.KeepFirstAndLast)]
    [InlineData("tokenbudget", ContextTrimStrategy.TokenBudget)]
    [InlineData("unknown", ContextTrimStrategy.SlidingWindow)]
    public void Compile_Maps_Context_Trim_Strategies(string trimStrategy, ContextTrimStrategy expected)
    {
        using var services = CreateWorkflowServices();
        var compiler = services.GetRequiredService<IWorkflowGraphCompiler>();

        var compiled = compiler.Compile(new WorkflowDefinition
        {
            Id = "trim-map",
            Name = "Trim Map",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    Name = "Node",
                    Description = "Do work",
                    Agent = new AgentConfig
                    {
                        ContextWindow = new ContextWindowConfig
                        {
                            TrimStrategy = trimStrategy,
                            MaxTokens = 1234,
                            TargetTokens = 1000,
                            ReservedForOutput = 111
                        }
                    }
                }
            ]
        });

        var contextWindow = compiled.Graph.Nodes.Single().Task.AgentDefinition!.ContextWindow!;
        contextWindow.TrimStrategy.Should().Be(expected);
        contextWindow.MaxTokens.Should().Be(1234);
        contextWindow.TargetTokens.Should().Be(1000);
        contextWindow.ReservedForOutput.Should().Be(111);
    }

    [Theory]
    [InlineData("none", CheckpointStrategy.None)]
    [InlineData("on_error", CheckpointStrategy.OnError)]
    [InlineData("manual", CheckpointStrategy.Manual)]
    [InlineData(null, CheckpointStrategy.AfterEachNode)]
    public void Compile_Maps_Checkpoint_Strategies(string? checkpointStrategy, CheckpointStrategy expected)
    {
        using var services = CreateWorkflowServices();
        var compiler = services.GetRequiredService<IWorkflowGraphCompiler>();

        var compiled = compiler.Compile(new WorkflowDefinition
        {
            Id = "checkpoint-map",
            Name = "Checkpoint Map",
            Nodes = [new NodeDefinition { Id = "n1", Name = "Node", Description = "Do work" }],
            Options = new WorkflowOptions { CheckpointStrategy = checkpointStrategy }
        });

        compiled.Options.CheckpointStrategy.Should().Be(expected);
    }

    [Fact]
    public void Compile_Runtime_Variables_Override_Definition_Variables()
    {
        using var services = CreateWorkflowServices();
        var compiler = services.GetRequiredService<IWorkflowGraphCompiler>();

        var compiled = compiler.Compile(new WorkflowDefinition
        {
            Id = "vars",
            Name = "Variables",
            Variables = new Dictionary<string, object> { ["topic"] = "default" },
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    Name = "Node",
                    Description = "Write about ${topic}",
                    Agent = new AgentConfig { SystemPrompt = "Topic=${topic}" }
                }
            ]
        }, new Dictionary<string, object> { ["topic"] = "override" });

        var task = compiled.Graph.Nodes.Single().Task;
        task.Description.Should().Be("Write about override");
        task.AgentDefinition!.SystemPrompt.Should().Be("Topic=override");
    }

    private static ServiceProvider CreateWorkflowServices()
        => new ServiceCollection()
            .AddSingleton<IChatClient>(_ => new FakeChatClient("ok", "ok"))
            .AddSingleton<IToolRegistry, DefaultToolRegistry>()
            .AddSingleton<IAgentPool, DefaultAgentPool>()
            .AddSingleton<IOrchestrator, DefaultOrchestrator>()
            .AddWorkflowDsl()
            .BuildServiceProvider();
}

public class DefaultWorkflowExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Invalid_Workflow_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(_ => new FakeChatClient("ok"));
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        services.AddSingleton<IAgentPool, DefaultAgentPool>();
        services.AddSingleton<IOrchestrator, DefaultOrchestrator>();
        services.AddWorkflowDsl();

        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IWorkflowExecutor>();

        var act = () => executor.ExecuteAsync(new WorkflowDefinition
        {
            Id = "broken",
            Name = "Broken",
            Nodes = []
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_Runs_Conditional_Workflow_And_Skips_Unmatched_Node()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(_ => new FakeChatClient("approved summary", "fallback summary"));
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();
        services.AddSingleton<IAgentPool, DefaultAgentPool>();
        services.AddSingleton<IOrchestrator, DefaultOrchestrator>();
        services.AddWorkflowDsl();

        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IWorkflowExecutor>();

        var result = await executor.ExecuteAsync(new WorkflowDefinition
        {
            Id = "conditional",
            Name = "Conditional Workflow",
            Nodes =
            [
                new NodeDefinition { Id = "start", Name = "Start", Description = "Start" },
                new NodeDefinition { Id = "approved", Name = "Approved", Description = "Approved path" },
                new NodeDefinition { Id = "rejected", Name = "Rejected", Description = "Rejected path" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "approved", Condition = "result.text.contains('approved')" },
                new EdgeDefinition { From = "start", To = "rejected", Condition = "result.status == 'Failed'" }
            ]
        });

        result.Status.Should().Be(OrchestrationStatus.Completed);
        result.TaskResults.Should().HaveCount(2);
        result.TaskResults.Values.Should().OnlyContain(item => item.Status == AgentResultStatus.Success);
        result.TaskResults.Values.Select(item => item.Text).Should().Contain("approved summary");
    }
}
