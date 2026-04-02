# Nexus For LLMs

This section is optimized for retrieval, prompting, and fast package-to-capability mapping.

Start here when an LLM needs to answer one of these questions quickly:

- which package owns a runtime concern
- when to use agent loop versus orchestration versus workflows DSL
- how tools, approvals, sessions, and protocols fit together
- where testing and benchmark evidence lives

## Use This Section When

Use these pages as the low-token routing layer for Nexus.

Read them before the full guides when the main goal is to identify the right package, runtime surface, or next document with minimal context cost.

## Fast Path By Problem Shape

- one-shot assistant with tools: start with [Runtime Map](runtime-map.md), then [Tools And Sub-Agents](tools-and-subagents.md)
- multi-turn chat with resume, approvals, or compaction: start with [Agent Loop](agent-loop.md)
- structured workflow or DAG execution: start with [Workflows DSL](workflows-dsl.md), then [Runtime Map](runtime-map.md)
- evidence, test surfaces, or benchmark locations: use [Testing And Benchmarks](testing-and-benchmarks.md)
- unfamiliar runtime term: use [Glossary](glossary.md)

## Package Shortcut Map

- `Nexus.AgentLoop`: multi-turn execution with sessions, tool calls, approvals, and compaction
- `Nexus.Orchestration`: graph, sequence, parallel, and hierarchical execution
- `Nexus.Workflows.Dsl`: load and compile JSON or YAML workflows
- `Nexus.Tools.Standard`: filesystem, search, shell, web, interaction, and sub-agent tools
- `Nexus.Sessions` plus `Nexus.Compaction`: continuity and context-window control
- `Nexus.Protocols.*` plus `Nexus.Hosting.AspNetCore`: runtime exposure over MCP, A2A, AG-UI, and HTTP

## Read Next

- if you know the package name already, jump to [Package Index](../api/README.md)
- if you need implementation detail, jump from here into `docs/api`
- if you need integration guidance, jump from here into `docs/guides` or `docs/recipes`

## Documents

- [Runtime Map](runtime-map.md)
- [Agent Loop](agent-loop.md)
- [Workflows DSL](workflows-dsl.md)
- [Tools And Sub-Agents](tools-and-subagents.md)
- [Testing And Benchmarks](testing-and-benchmarks.md)
- [Glossary](glossary.md)