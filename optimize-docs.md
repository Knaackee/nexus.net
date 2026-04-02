# Optimize Docs

## Goal

Raise the quality of `docs`, `recipes`, `guides`, and `llms` to a level where:

- a new user can choose the right Nexus package or document quickly
- an LLM can route itself to the right file with low token cost
- the root README stays curated instead of turning into a package dump
- every `src` project has at least one discoverable documentation home

## Executive Summary

The documentation is already strong in structure and intent, but it is not yet complete enough for first-contact navigation.

The biggest issue is not writing quality. The biggest issue is coverage and routing:

- `docs/llms/README.md` is efficient, but too thin as a first entry point for a new LLM
- the root `README.md` links only part of the actual package surface
- multiple `src` projects do not have a dedicated documentation page
- some pages exist, but their discoverability from the root is inconsistent

The right fix is not to make every top-level page longer. The right fix is to improve routing quality while keeping index pages compact.

## Assessment Of docs/llms/README.md

## What already works well

- Very low token cost
- Clear purpose: retrieval, prompting, package-to-capability mapping
- Strong document titles for second-hop navigation
- Good separation from full human-oriented guides

## What is missing

As a first-contact file for a new LLM, it is currently too sparse.

It does not yet answer these questions fast enough:

- If I only need one package, which one is it?
- Where do I go for session chat versus orchestration versus workflow DSL?
- Which docs are conceptual, which are package maps, and which are evidence or terminology?
- What is the minimum reading path for a new integration?

## Verdict

`docs/llms/README.md` is good as a compact index, but not yet good enough as the primary landing page for a new LLM dropped into the repo cold.

It should stay compact, but it needs roughly 80 to 180 more words of routing help.

## Recommended shape for docs/llms/README.md

Keep it short. Add only these compact sections:

1. `Use this section when...`
2. `Fast path by problem shape`
3. `Package shortcut map`
4. `Read next`

Example content shape:

- one-shot assistant with tools -> `runtime-map.md`, then `tools-and-subagents.md`
- multi-turn chat with memory -> `agent-loop.md`, then `runtime-map.md`
- structured workflow or DAG -> `workflows-dsl.md`, then `runtime-map.md`
- evaluating reliability or runtime evidence -> `testing-and-benchmarks.md`
- unfamiliar term -> `glossary.md`

That keeps token use low while making first-hop routing much stronger.

## Root README Issues

The root `README.md` is strong as a product page, but weaker as a complete package map.

### Problem 1

The API docs list looks authoritative, but it covers only a subset of the `src` packages.

This creates a false impression that undocumented packages are less important or internal-only, even when they are user-relevant.

### Problem 2

The README mixes multiple navigation styles:

- some topics link to guides
- some packages link to API pages
- some examples link to `examples/.../README.md`
- some example docs under `docs/examples/` are not surfaced consistently

### Problem 3

The `Project Structure` section is too coarse to substitute for package-level docs.

## Recommended root strategy

Do not turn the root README into a 24-package directory.

Instead:

1. Keep the root README curated.
2. Add one canonical package index page.
3. Link that package index prominently from the root README and `docs/README.md`.

Recommended new index page:

- `docs/api/README.md` or `docs/packages/README.md`

That page should list every `src` package with:

- one-line purpose
- when to use it
- link to API doc or package page
- link to related guide/example if available

## Coverage Review By Source Project

### Well covered

- `Nexus.Core`
- `Nexus.Orchestration`
- `Nexus.Memory`
- `Nexus.Guardrails`
- `Nexus.Permissions`
- `Nexus.CostTracking`
- `Nexus.Messaging`
- `Nexus.Workflows.Dsl`
- `Nexus.Testing`

These already have clear API entries and related guide coverage.

### Partially covered

- `Nexus.AgentLoop`: covered in LLM docs, but missing dedicated API page
- `Nexus.Auth.OAuth2`: covered by auth guide, but missing package page or API page
- `Nexus.Orchestration.Checkpointing`: covered by guide language, but not discoverable as its own project surface
- `Nexus.Protocols.A2A`: partially covered inside combined protocols page
- `Nexus.Protocols.AgUi`: partially covered inside combined protocols page
- `Nexus.Protocols.Mcp`: partially covered inside combined protocols page
- `Nexus.Hosting.AspNetCore`: mentioned inside protocols page, but not documented as a first-class hosting package
- `Nexus.Telemetry`: guide exists, but there is no dedicated API page
- `Nexus.Tools.Standard`: lightly covered in LLM docs, but missing dedicated package/API page

### Missing dedicated documentation home

- `Nexus.Commands`
- `Nexus.Compaction`
- `Nexus.Configuration`
- `Nexus.Defaults`
- `Nexus.Sessions`
- `Nexus.Skills`

These are important enough to deserve first-class discoverability.

## Missing Or Weakly Surfaced Docs

### Missing dedicated package/API pages to add

- `docs/api/nexus-agent-loop.md`
- `docs/api/nexus-auth-oauth2.md`
- `docs/api/nexus-commands.md`
- `docs/api/nexus-compaction.md`
- `docs/api/nexus-configuration.md`
- `docs/api/nexus-defaults.md`
- `docs/api/nexus-hosting-aspnetcore.md`
- `docs/api/nexus-orchestration-checkpointing.md`
- `docs/api/nexus-sessions.md`
- `docs/api/nexus-skills.md`
- `docs/api/nexus-telemetry.md`
- `docs/api/nexus-tools-standard.md`

### Existing docs that should be surfaced better from the root

- `docs/recipes/existing-provider-ui.md`
- `docs/examples/chat-editing-with-diff-and-revert.md`

### Existing docs that need a clearer canonical entry point

- combined protocols documentation versus separate protocol package discoverability
- example docs under `docs/examples/` versus runnable example READMEs under `examples/`

## Recommended Documentation Model Per Package

Every `src` project should have one canonical doc page answering the same questions in the same order:

1. What is this package?
2. When should I use it?
3. When should I not use it?
4. Key types or entry points
5. Minimal registration or setup example
6. Related guides
7. Related examples
8. Related neighboring packages

That gives both humans and LLMs a stable retrieval pattern.

## Quality Bar By Doc Type

### Root and index pages

- short
- routing-heavy
- no long code blocks
- clear next-hop links
- no duplication of deep content

### Guides

- explain tradeoffs and design choices
- show when to choose one subsystem over another
- include failure modes and production constraints
- cross-link to recipes and API pages

### Recipes

- stay scenario-first
- show smallest viable wiring
- explicitly say what remains outside Nexus
- point to runnable example and guide

### LLM docs

- optimize for package selection and terminology disambiguation
- keep paragraphs short and dense
- avoid marketing copy
- prefer routing tables and capability maps over prose

### API and package docs

- identify key public types, not every internal symbol
- include concrete setup snippets
- state neighboring packages and boundaries clearly

## Priority Plan

### P0

- strengthen `docs/llms/README.md` as a first-hop LLM landing page
- add a canonical package index page for all `src` projects
- update root `README.md` and `docs/README.md` to link that package index

### P1

- add package/API pages for missing first-class projects
- surface `existing-provider-ui` from the root README
- surface `docs/examples/chat-editing-with-diff-and-revert.md` from the root README

### P2

- decide whether protocols stay combined or become one page per package
- decide whether examples docs and runnable example READMEs need a stricter canonical hierarchy
- add consistent "Related guide / recipe / example / API" footers to all package pages

## Concrete Edits To Make Next

1. Rewrite `docs/llms/README.md` to add a compact routing section without turning it into a guide.
2. Create `docs/api/README.md` as the canonical package index.
3. Add missing package docs for `Commands`, `Compaction`, `Configuration`, `Defaults`, `Sessions`, `Skills`, `AgentLoop`, `Tools.Standard`, `Hosting.AspNetCore`, `Telemetry`, `Auth.OAuth2`, and `Orchestration.Checkpointing`.
4. Update the root `README.md` so it links to the package index instead of implying the current API list is complete.
5. Add the missing high-value links for `Existing Provider UI` and `Chat Editing With Diff And Revert` doc pages.

## Definition Of Done

This optimization work is complete when:

- every `src` project has a discoverable documentation home
- the root README stays concise but no longer hides package coverage gaps
- `docs/llms/README.md` can route a new LLM correctly in one hop most of the time
- guides, recipes, package docs, and examples form one consistent navigation system
- a new user can answer "which package should I start with?" in under one minute