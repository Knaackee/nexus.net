# Workflows DSL

`Nexus.Workflows.Dsl` describes workflow structure and compiles it into orchestration graphs.

## Main Types

- `WorkflowDefinition`
- `NodeDefinition`
- `EdgeDefinition`
- `WorkflowOptions`
- `IWorkflowLoader`
- `IWorkflowValidator`
- `IWorkflowGraphCompiler`
- `IWorkflowExecutor`

## Execution Model

- nodes become `AgentTask` instances
- edges become graph dependencies or conditional edges
- workflow options become `OrchestrationOptions`
- execution is performed by `IOrchestrator`

## Concurrency

- `WorkflowOptions.MaxConcurrentNodes` limits graph-level parallelism
- conditional edges can prevent a downstream node from becoming runnable
- when all incoming conditional edges fail, Nexus skips the unreachable node explicitly

## Variable Resolution

- definition variables are merged with runtime variables
- runtime variables override definition variables on key collision
- description, system prompt, and conditions are resolved before execution