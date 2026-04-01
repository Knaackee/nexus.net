# Testing And Benchmarks

Nexus is designed to be tested at the contract, runtime, and scenario level.

## Testing Layers

- unit tests for packages such as orchestration, sessions, tools, and workflow DSL
- loop and orchestration scenario tests for multi-step behavior
- CLI tests for user-facing command behavior
- live integration tests for provider-backed paths

## Useful Test Helpers

- `Nexus.Testing.Mocks.FakeChatClient`
- `Nexus.Testing.Mocks.MockTool`
- `Nexus.Testing.Mocks.MockApprovalGate`
- `Nexus.Testing.Mocks.MockAgent`

## Benchmark Surface

`benchmarks/Nexus.Benchmarks` measures runtime hot paths such as:

- workflow compilation
- workflow execution
- parallel sub-agent delegation

## Why This Matters

- orchestration code is control-flow heavy
- approval and fallback behavior are branch heavy
- bounded concurrency behavior should be measured, not guessed