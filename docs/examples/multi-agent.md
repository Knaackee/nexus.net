# Example: Multi-Agent Graph

The `Nexus.Examples.MultiAgent` project demonstrates graph orchestration with multiple agents, checkpointing, and workflow DSL.

**Location:** `examples/Nexus.Examples.MultiAgent/`

## What It Shows

- Multi-agent graph orchestration with DAG dependencies
- Conditional edges based on agent results
- Checkpointing and resume-from-failure
- Loading workflows from JSON/YAML definitions
- Streaming events from graph execution

## Graph Orchestration

```csharp
var services = new ServiceCollection();
services.AddNexus(nexus =>
{
    nexus.UseChatClient("gpt4", _ => gpt4Client);
    nexus.UseChatClient("mini", _ => miniClient);
    nexus.AddOrchestration(o => o.UseDefaults());
    nexus.AddMemory(m => m.UseInMemory());
    nexus.AddCheckpointing();
    nexus.AddMessaging();
});

var sp = services.BuildServiceProvider();
var pool = sp.GetRequiredService<IAgentPool>();

// Spawn specialized agents
var researcher = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Researcher",
    ChatClientName = "gpt4",
    SystemPrompt = "You research topics thoroughly.",
    ToolNames = ["web_search"],
});

var writer = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Writer",
    ChatClientName = "gpt4",
    SystemPrompt = "You write engaging content based on research.",
});

var reviewer = await pool.SpawnAsync(new AgentDefinition
{
    Name = "Reviewer",
    ChatClientName = "mini",
    SystemPrompt = "You review content for accuracy and clarity. Say 'APPROVED' or 'NEEDS REVISION'.",
});

// Build the graph
var orchestrator = sp.GetRequiredService<IOrchestrator>();
var graph = orchestrator.CreateGraph();

var researchNode = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Research the latest AI agent frameworks",
    AssignedAgent = researcher.Id,
});

var writeNode = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Write a blog post about the research findings",
    AssignedAgent = writer.Id,
});

var reviewNode = graph.AddTask(new AgentTask
{
    Id = TaskId.New(),
    Description = "Review the blog post for accuracy",
    AssignedAgent = reviewer.Id,
});

graph.AddDependency(researchNode, writeNode);
graph.AddDependency(writeNode, reviewNode);

// Stream execution events
await foreach (var evt in orchestrator.ExecuteGraphStreamingAsync(graph))
{
    switch (evt)
    {
        case NodeStartedEvent ns:
            Console.WriteLine($"\n--- {ns.NodeId} started ---");
            break;
        case AgentEventInGraph { InnerEvent: TextChunkEvent chunk }:
            Console.Write(chunk.Text);
            break;
        case NodeCompletedEvent nc:
            Console.WriteLine($"\n--- {nc.NodeId} completed: {nc.Result.Status} ---");
            break;
    }
}
```

## Checkpointing

```csharp
var store = sp.GetRequiredService<ICheckpointStore>();

// Execute with checkpointing
try
{
    var result = await orchestrator.ExecuteGraphAsync(graph);
}
catch (Exception)
{
    // Load latest checkpoint and resume
    var snapshot = await store.LoadLatestAsync(graph.Id);
    if (snapshot is not null)
    {
        var result = await orchestrator.ResumeFromCheckpointAsync(snapshot, graph);
        Console.WriteLine($"Resumed: {result.Status}");
    }
}
```

## Workflow DSL

Load the same pipeline from a JSON file:

```json
{
    "id": "blog-pipeline",
    "name": "Blog Pipeline",
    "nodes": [
        {
            "id": "research",
            "name": "Researcher",
            "description": "Research AI agent frameworks",
            "agent": { "tools": ["web_search"], "chatClient": "gpt4" }
        },
        {
            "id": "write",
            "name": "Writer",
            "description": "Write blog post",
            "agent": { "chatClient": "gpt4" }
        },
        {
            "id": "review",
            "name": "Reviewer",
            "description": "Review for accuracy",
            "agent": { "modelId": "gpt-4o-mini" }
        }
    ],
    "edges": [
        { "from": "research", "to": "write" },
        { "from": "write", "to": "review" }
    ]
}
```

```csharp
var loader = sp.GetRequiredService<IWorkflowLoader>();
var workflow = await loader.LoadFromFileAsync("blog-pipeline.json");
var validator = sp.GetRequiredService<IWorkflowValidator>();
var validation = validator.Validate(workflow);
Console.WriteLine($"Valid: {validation.IsValid}");
```

## Running

```bash
cd examples/Nexus.Examples.MultiAgent
dotnet run
```
