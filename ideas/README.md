# Nexus Ideas & Feature Gap Analysis

## Kontext

Diese Analyse vergleicht Nexus (v0.1.0) mit dem Claude Code CLI Source Code, um fehlende Building Blocks zu identifizieren, die die Developer Experience drastisch verbessern würden.

**Kernproblem:** Nexus bietet gute Low-Level-Bausteine (Agents, Tools, Memory, Orchestration), aber es fehlt der **"Leim" dazwischen** — die Komponenten, die eine agentic Anwendung erst wirklich nutzbar machen. Entwickler müssen aktuell zu viel selbst bauen, um von den Bausteinen zu einer funktionierenden Anwendung zu kommen.

## Übersicht: Wo steht Nexus?

### Was Nexus gut macht
- Saubere Architektur mit klarer Schichtentrennung
- Composable Middleware (ASP.NET Core Pattern)
- Streaming-first Design (`IAsyncEnumerable<T>`)
- Protocol-Adapter (MCP, A2A, AG-UI)
- Orchestrierungsmuster (Graph, Sequence, Parallel, Hierarchical)
- Stark typisierte Abstraktionen (`AgentId`, `TaskId`, etc.)
- `Microsoft.Extensions.AI` Integration

### Was fehlt (Priorisiert)

Status-Stand: 2026-04-01

| # | Idea | Priorität | Status | Datei |
|---|------|-----------|--------|-------|
| 1 | **Agent Execution Loop** — Der fehlende Kern | 🔴 Kritisch | Teilweise erledigt | [001-agent-execution-loop.md](001-agent-execution-loop.md) |
| 2 | **Context Window Management & Auto-Compaction** | 🔴 Kritisch | Teilweise erledigt | [002-context-compaction.md](002-context-compaction.md) |
| 3 | **Tool Concurrency & Streaming Execution** | 🟠 Hoch | Teilweise erledigt | [003-tool-concurrency.md](003-tool-concurrency.md) |
| 4 | **Permission System** — Tool-Zugriffskontrolle | 🟠 Hoch | Weitgehend erledigt | [004-permission-system.md](004-permission-system.md) |
| 5 | **Cost Tracking & Budget Enforcement** | 🟠 Hoch | Weitgehend erledigt | [005-cost-tracking.md](005-cost-tracking.md) |
| 6 | **Session Persistence & Resume** | 🟠 Hoch | Weitgehend erledigt | [006-session-persistence.md](006-session-persistence.md) |
| 7 | **Configuration Hierarchy** | 🟡 Mittel | Erledigt | [007-configuration-hierarchy.md](007-configuration-hierarchy.md) |
| 8 | **Command Framework** — Erweiterbare Slash-Commands | 🟡 Mittel | Erledigt | [008-command-framework.md](008-command-framework.md) |
| 9 | **Plugin Architecture** | 🟡 Mittel | Offen | [009-plugin-architecture.md](009-plugin-architecture.md) |
| 10 | **Skills System** — Custom Prompt Templates | 🟡 Mittel | Erledigt | [010-skills-system.md](010-skills-system.md) |
| 11 | **Überflüssiges & Vereinfachungen** | — | Teilweise aktualisiert | [011-simplification.md](011-simplification.md) |
| 12 | **CLI Gap Analysis** — Was die Example CLI braucht | — | Weitgehend erledigt | [012-cli-gap-analysis.md](012-cli-gap-analysis.md) |

## Kernerkenntnisse aus Claude Code

1. **Der Agent Loop IST das Produkt** — Claude Code's `query()` Generator ist das Herzstück. Er orchestriert Model-Calls, Tool-Ausführung, Compaction, Budget-Enforcement und Recovery in einem einzigen, robusten Loop. Nexus hat keinen äquivalenten Building Block.

2. **Context Management ist kein Nice-to-Have** — Ohne automatische Compaction bricht jede längere Agent-Session zusammen. Claude Code hat 4 Compaction-Stufen (auto, reactive, micro, session memory).

3. **Tool-Concurrency ist ein Multiplikator** — Lesende Tools parallel, schreibende seriell — das macht Agents 3-5x schneller bei komplexen Aufgaben.

4. **Permission Control ermöglicht Trust** — Ohne Permission-System kann kein Entwickler einen Agent in Produktion deployen, der mit Filesystem/Shell arbeitet.

5. **DX schlägt Features** — Claude Code hat weniger Orchestrierungs-Patterns als Nexus, aber die DX ist dramatisch besser, weil der "happy path" vorkonfiguriert ist.

6. **Step-Level Human-in-the-Loop** — Nicht nur bei Tools, sondern zwischen Workflow-Steps. User kann nach einem Design-Entwurf approven, eingreifen oder modifizieren bevor der nächste Step startet. Nexus nutzt dafür das bestehende `IApprovalGate` + `AgentState.WaitingForApproval`.

7. **Eigenes Format statt Kompatibilität** — Nexus nutzt `.nexus/` als eigenes Konfigurationsformat. Keine Kompatibilität mit `.claude/`, GitHub Copilot oder OpenCode Configs — Nexus ist bewusst flexibler und mächtiger als der kleinste gemeinsame Nenner dieser Tools.
