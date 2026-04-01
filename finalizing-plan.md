# Finalizing Plan

This plan focuses on the last major push from a strong engineering repository to a polished, enterprise-grade runtime with better learnability, better examples, and much higher confidence through coverage.

## Objectives

1. Turn recipes into executable, source-backed assets.
2. Expand the recipe and guide catalog where real gaps still exist.
3. Add a short, precise LLM-oriented documentation surface.
4. Drive coverage from the current strong-but-not-final level toward near-complete coverage on core runtime paths.

## Workstream 1: Source-Backed Recipes

### Goal

Each important recipe should exist in three layers:

- documentation in `docs/recipes/{recipe-name}.md` or a migrated folder-based recipe README
- a minimal runnable sample app in `examples/{ExampleName}/`
- matching unit or focused integration tests in `tests/Recipes.{RecipeName}.Tests/` or the closest existing test project

### Target Structure

- `recipes/{recipe-name}/README.md`
- `examples/{ExampleName}/`
- `recipes/{recipe-name}/tests/` or mirrored tests under `tests/`

### Recommended First Wave

1. Single Agent With Tools
2. Chat Session With Memory
3. Human-Approved Workflow
4. Parallel Sub-Agents And Workflow Fan-Out

### Deliverables

- one runnable C# app per priority recipe
- one concise README per recipe focused on setup, shape, and extension points
- unit tests validating the central behavior of each recipe
- recipe index updated to point to docs plus source-backed example locations

### Success Criteria

- every top-tier recipe is runnable without guesswork
- every top-tier recipe has at least one automated test path
- docs and source stay aligned because the recipe stops being prose-only

## Workstream 2: More Recipes And More Guides

### Goal

Fill the remaining scenario gaps without producing low-value filler documentation.

### Candidate New Recipes

1. Multi-Model Routing
2. Budget-Limited Research Pipeline
3. MCP Tool Host With Approval Gate
4. Checkpointed Recovery Workflow
5. Tool-Only Worker Agent
6. Event-Streaming Web UI via AG-UI
7. A2A Delegation Between Services
8. Cost-Aware Batch Processing

### Candidate New Guides

1. Performance And Benchmarking
2. Production Hardening
3. Versioning And Upgrade Strategy
4. CI And Quality Gates
5. Operating Nexus In Team Environments
6. Designing Reliable Tool Contracts
7. Workflow Patterns And Anti-Patterns

### Prioritization Rule

Only add a new recipe or guide if it maps to an actual supported runtime capability and can be backed by either code, tests, or a real example.

### Success Criteria

- recipe catalog covers the main deployment and orchestration shapes
- guide catalog covers runtime design, operations, testing, and performance
- no guide is purely aspirational or disconnected from current implementation

## Workstream 3: LLM Documentation Surface

### Goal

Create a dedicated documentation surface optimized for LLM ingestion: short, factual, stable, and low-noise.

### Desired Characteristics

- concise and chunkable
- minimal marketing language
- strong terminology consistency
- one concept per section
- direct mapping to runtime components and public APIs

### Proposed Artifacts

1. `docs/llms/README.md` as the entry point
2. `docs/llms/runtime-map.md`
3. `docs/llms/agent-loop.md`
4. `docs/llms/workflows-dsl.md`
5. `docs/llms/tools-and-subagents.md`
6. `docs/llms/testing-and-benchmarks.md`
7. `docs/llms/glossary.md`

### Content Rules

- short paragraphs
- compact tables only when useful
- avoid long narrative intros
- avoid duplicate prose from human-facing docs
- prefer direct statements like “Nexus uses X for Y” over conceptual storytelling

### Success Criteria

- an LLM can infer package responsibilities quickly
- an LLM can distinguish orchestration, agent loop, tools, workflows DSL, and protocols without ambiguity
- the content is stable enough to be referenced in prompts and retrieval systems

## Workstream 4: Near-Full Coverage

### Goal

Push coverage much higher, with a focus on meaningful branch coverage in the core runtime rather than synthetic line inflation.

### Current Baseline

- full-solution line coverage is solid but not yet final-quality
- branch coverage is the main weakness
- the most important remaining gaps are likely in orchestration branches, middleware edges, protocol error paths, and CLI/runtime integration seams

### Coverage Strategy

1. Identify low-coverage core assemblies first.
2. Expand tests around real decision points, not trivial accessors.
3. Add regression tests for failure modes, timeouts, skipped nodes, validation failures, and fallback behavior.
4. Add scenario tests that cover multiple branches per run.
5. Introduce CI thresholds once the repo stabilizes above the target floor.

### Highest-Priority Coverage Areas

1. `Nexus.Orchestration`
2. `Nexus.AgentLoop`
3. `Nexus.Tools.Standard`
4. `Nexus.Workflows.Dsl`
5. `Nexus.Hosting.AspNetCore`
6. `Nexus.Protocols.Mcp`
7. `Nexus.Cli`

### Target Quality Bar

- near-full coverage on core runtime packages
- very high branch coverage for scheduling, approval, and fallback logic
- documented exceptions for hard-to-test external or live-only boundaries

### Success Criteria

- line coverage materially higher than the current baseline
- branch coverage improved enough that control-flow confidence is substantially better
- CI can enforce thresholds without becoming flaky

## Execution Order

### Phase 1

- establish recipe-to-source structure
- migrate first-wave recipes into runnable sample-backed assets
- add tests around those assets

### Phase 2

- add LLM-specific docs
- expand guides and recipes only where real runtime support exists
- keep docs indexes synchronized

### Phase 3

- push coverage aggressively across core runtime packages
- add coverage reporting and thresholds to CI
- close remaining high-value gaps before declaring enterprise readiness

## Risks

- recipe expansion can drift into duplicate docs if source-backed structure is not enforced
- coverage can become vanity-driven if line percentage is optimized without branch intent
- LLM docs can rot quickly if they mirror human docs instead of summarizing stable facts

## Final Readiness Gate

Nexus should only be called effectively enterprise-ready after these conditions are true:

- the main recipes are runnable and tested
- the guide and recipe catalog matches the implemented runtime surface
- the LLM docs exist and are kept intentionally concise
- core runtime coverage is high enough to trust control-flow-heavy components under change
- CI can verify tests, coverage, and benchmarks consistently