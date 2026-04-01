# Nexus Benchmarks

The benchmark suite measures the hot paths that matter for multi-agent runtime design:

- workflow compilation from DSL to executable task graph
- workflow execution with parallel-ready nodes
- batched sub-agent delegation through the standard `agent` tool

## Run Everything

```bash
dotnet run -c Release --project benchmarks/Nexus.Benchmarks
```

## Run One Family

```bash
dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter *Workflow*
dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter *SubAgent*
```

## Benchmarks Included

- `WorkflowRuntimeBenchmarks.CompileWorkflow`
- `WorkflowRuntimeBenchmarks.ExecuteWorkflow`
- `SubAgentBenchmarks.RunParallelSubAgents`

## Results

BenchmarkDotNet writes CSV, GitHub-flavored Markdown, and HTML reports into `BenchmarkDotNet.Artifacts/results`.

## Why These Benchmarks Exist

- They make parallel workflow execution measurable instead of anecdotal.
- They show the overhead of delegating to multiple child agents in one tool call.
- They give a stable place to compare future scheduler, compiler, or pooling changes.