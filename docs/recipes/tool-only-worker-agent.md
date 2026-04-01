# Recipe: Tool-Only Worker Agent

Use this when the agent should act as a narrow worker with a tightly bounded tool surface.

## Good Fit

- the worker does not need workflow routing
- tools are the main value path
- prompts should stay minimal and deterministic

## Core Pieces

- `IAgentPool`
- `IToolRegistry`
- tool annotations and approval metadata
- optional `IApprovalGate` for sensitive tools

## Design Rule

Give the worker only the few tools required for its job. Avoid turning a specialized worker into a general-purpose assistant.

## Related Guides

- [Permissions](../guides/permissions.md)
- [Testing](../guides/testing.md)