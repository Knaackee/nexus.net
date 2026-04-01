# Production Hardening

Production readiness in Nexus comes from explicit boundaries and verifiable runtime policy.

## Minimum Runtime Controls

- approval rules for state-changing tools
- budget limits on expensive agents and workflows
- guardrails for prompt injection, secrets, and PII
- checkpointing for long-running or failure-prone graphs
- telemetry and audit logging on critical execution paths

## Deployment Guidance

- keep provider credentials outside application code
- prefer deterministic tool contracts over free-form shell access
- use bounded concurrency for sub-agents and graph execution
- isolate file and shell tools to explicit working directories
- keep live-only boundaries documented when they are hard to unit test

## Failure Policy Guidance

- retries should be reserved for transient failures
- fallbacks should be explicit and test-covered
- approval rejections should stop or branch the workflow clearly
- skipped workflow nodes should be treated as expected outcomes, not silent failures

## Related Docs

- [Guardrails](guardrails.md)
- [Permissions](permissions.md)
- [Checkpointing](checkpointing.md)
- [Telemetry](telemetry.md)