# Before Production — Konsolidierter Schritt-für-Schritt-Plan

**Erstellt:** 02.04.2026
**Grundlage:** Zwei unabhängige Developer-Reviews + Gegenprüfung am Repo-Stand vom 02.04.2026
**Verifizierter Test-Stand:** 331 Tests, 0 Failed, 20 Test-Projekte

---

## Zusammenfassung der beiden Reviews

| Punkt | Review 1 (before-production-plan) | Review 2 (before-production-plan-opus) | Gegenprüfung |
|-------|-----------------------------------|----------------------------------------|---------------|
| Test-Anzahl | 331 ✅ | 294 ❌ (veraltet) | **331 bestätigt** |
| Release-Pipeline unvollständig | nicht erwähnt | 14/24 Packages ⛔ | **bestätigt — 10 fehlen** |
| README Badge Platzhalter | nicht erwähnt | `your-org` ⛔ | **bestätigt** |
| README Test-Zahl falsch | 265 statt 331 ⚠️ | erwähnt ⚠️ | **bestätigt — README sagt 265** |
| README Coverage nicht reproduzierbar | erwähnt ⚠️ | nicht explizit | **bestätigt — 76.29%/58.28% ohne Quelle** |
| README fehlt ChatEditing Example | erwähnt ⚠️ | erwähnt (L1) | **bestätigt** |
| installation.md unvollständig | nicht erwähnt | 14/24 Packages ⛔ | **bestätigt — 10 fehlen** |
| .gitignore fehlt `.nexus/` | erwähnt ⛔ | nicht erwähnt | **bestätigt — 4 Sessions + sessions.json eingecheckt** |
| CI schließt Cli.Tests aus | erwähnt ⚠️ | nicht erwähnt | **bestätigt** |
| CI installiert .NET 8.0.x unnötig | nicht erwähnt | erwähnt (M5) | **bestätigt** |
| 7 Projekte ohne Tests | erwähnt ⚠️ | erwähnt (H1) | **bestätigt (6 + Testing=N/A)** |
| csproj Descriptions fehlen | nicht erwähnt | 14 Projekte ⚠️ | **bestätigt** |
| CS1591 global unterdrückt | nicht erwähnt | erwähnt (M2) | **bestätigt** |
| Architektur-Diagramm unvollständig | nicht erwähnt | erwähnt (M1) | **bestätigt** |
| DI-Pattern inkonsistent | nicht erwähnt | erwähnt (M3) | nicht geprüft |
| Public-Klassen sollten internal sein | nicht erwähnt | erwähnt (M4) | nicht geprüft |
| CHANGELOG.md fehlt | nicht erwähnt | erwähnt (L4) | bestätigt |

**Fazit:** Beide Reviews sind inhaltlich korrekt und ergänzen sich gut. Review 1 fokussiert auf Repo-Hygiene und Consumer-Framing, Review 2 geht tiefer in Pipeline und Package-Vollständigkeit. Die Schnittmenge der Findings ist konsistent.

---

## Phase 1 — BLOCKER (vor Übergabe, heute)

> Diese Punkte verhindern, dass ein Consumer die Library sinnvoll nutzen kann.

---

### Schritt 1.1: `.nexus/` in .gitignore aufnehmen und Artefakte entfernen

**Problem:** Unter `examples/Nexus.Cli/.nexus/sessions/` liegen 4 Session-Verzeichnisse und eine `sessions.json` im Repo. Das sind lokale Laufzeitartefakte, die nicht ausgeliefert werden dürfen.

**Schritte:**

1. `.gitignore` öffnen und folgende Zeile ergänzen:
   ```
   .nexus/
   ```

2. Gecachte Dateien aus dem Git-Index entfernen:
   ```bash
   git rm -r --cached examples/Nexus.Cli/.nexus/
   ```

3. Commit:
   ```bash
   git commit -m "chore: ignore .nexus runtime artifacts and remove checked-in sessions"
   ```

4. Prüfen, dass keine weiteren `.nexus/`-Verzeichnisse im Repo liegen:
   ```bash
   git ls-files | grep .nexus
   ```
   → Erwartung: keine Treffer.

---

### Schritt 1.2: Release-Pipeline um 10 fehlende Packages erweitern

**Problem:** `release.yml` packt nur 14 von 24 Packages. Die fehlenden 10 werden von Examples und Docs referenziert — ein Consumer kann sie nicht via NuGet installieren.

**Schritte:**

1. `.github/workflows/release.yml` öffnen.

2. Folgende 10 Projekte zur Pack-Liste hinzufügen:
   ```yaml
   - src/Nexus.AgentLoop/Nexus.AgentLoop.csproj
   - src/Nexus.Sessions/Nexus.Sessions.csproj
   - src/Nexus.Compaction/Nexus.Compaction.csproj
   - src/Nexus.Configuration/Nexus.Configuration.csproj
   - src/Nexus.CostTracking/Nexus.CostTracking.csproj
   - src/Nexus.Permissions/Nexus.Permissions.csproj
   - src/Nexus.Commands/Nexus.Commands.csproj
   - src/Nexus.Skills/Nexus.Skills.csproj
   - src/Nexus.Tools.Standard/Nexus.Tools.Standard.csproj
   - src/Nexus.Defaults/Nexus.Defaults.csproj
   ```

3. Lokal verifizieren, dass alle 24 Projekte packbar sind:
   ```bash
   dotnet pack Nexus.sln -c Release --no-build -o ./artifacts/packages
   ls ./artifacts/packages/*.nupkg | Measure-Object
   ```
   → Erwartung: 24 .nupkg Dateien.

4. Commit:
   ```bash
   git commit -m "ci: add 10 missing packages to release pipeline"
   ```

---

### Schritt 1.3: `docs/getting-started/installation.md` um 10 fehlende Packages erweitern

**Problem:** Die Installationsanleitung listet nur 14 von 24 Packages. Consumer finden die restlichen 10 nicht.

**Schritte:**

1. `docs/getting-started/installation.md` öffnen.

2. Folgende 10 Packages mit Beschreibung in die Tabelle aufnehmen:

   | Package | Beschreibung |
   |---------|-------------|
   | `Nexus.AgentLoop` | Session-aware execution loop for agents |
   | `Nexus.Sessions` | Session persistence and transcripts |
   | `Nexus.Compaction` | Context window compaction strategies |
   | `Nexus.Configuration` | Hierarchical configuration system |
   | `Nexus.CostTracking` | Token and USD cost tracking |
   | `Nexus.Permissions` | Tool approval and permission rules |
   | `Nexus.Commands` | Slash command framework |
   | `Nexus.Skills` | Skill injection middleware |
   | `Nexus.Tools.Standard` | Built-in tools (file, shell, grep, web) |
   | `Nexus.Defaults` | Batteries-included convenience wiring |

3. Sicherstellen, dass die Reihenfolge logisch ist (Core → Runtime → Convenience).

4. Commit:
   ```bash
   git commit -m "docs: add 10 missing packages to installation guide"
   ```

---

### Schritt 1.4: README.md Badge-URL korrigieren

**Problem:** Der CI-Badge zeigt auf `https://github.com/your-org/nexus/...` — ein Platzhalter.

**Schritte:**

1. `README.md` öffnen.
2. `your-org` durch den echten GitHub-Org/Repo-Namen ersetzen.
3. Prüfen, ob es weitere Stellen mit `your-org` gibt:
   ```bash
   grep -r "your-org" .
   ```
4. Alle Treffer korrigieren.

---

### Schritt 1.5: README.md Test-Zahl und Coverage aktualisieren

**Problem:** README sagt „265 tests passed". Aktuell: **331 Tests**. Coverage-Werte (76.29% / 58.28%) sind nicht aus einer reproduzierbaren Quelle belegt.

**Schritte:**

1. `README.md` öffnen.
2. `265` durch `331` ersetzen (oder Formulierung verwenden, die nicht so schnell veraltet, z.B. „300+ tests").
3. **Coverage-Werte:** Entweder
   - **(a)** entfernen, bis Coverage automatisiert erhoben wird, oder
   - **(b)** Coverage jetzt einmal erheben und aktualisieren:
     ```bash
     dotnet test Nexus.sln -c Release --collect:"XPlat Code Coverage"
     ```
   Empfehlung: **(a)** — harte Zahlen ohne CI-Enforcement sind ein Vertrauensrisiko.
4. Commit.

---

### Schritt 1.6: README.md — ChatEditingWithDiffAndRevert in Examples-Liste aufnehmen

**Problem:** Das Example existiert, ist in `docs/` und `examples/README.md` verlinkt, fehlt aber in der Root-README unter „Runnable Scenario Examples".

**Schritte:**

1. `README.md` öffnen, Abschnitt „Runnable Scenario Examples" finden.
2. Eintrag ergänzen:
   ```markdown
   | Chat Editing With Diff And Revert | File editing with change tracking, diff display, and one-click revert |
   ```
3. Commit.

---

## Phase 2 — HOCH (vor Übergabe empfohlen, spätestens Tag 1)

> Keine harten Blocker, aber diese Punkte beeinflussen das Vertrauen des Consumers.

---

### Schritt 2.1: CI — `Nexus.Cli.Tests` aufnehmen oder Ausschluss dokumentieren

**Problem:** `ci.yml` schließt `Nexus.Cli.Tests` (52 Tests) explizit aus. Diese Tests laufen lokal, aber nicht in der Pipeline.

**Schritte:**

Option A — Aufnehmen (empfohlen):
1. `.github/workflows/ci.yml` öffnen.
2. Aus dem `--filter`-String den Part `FullyQualifiedName!~Nexus.Cli.Tests` entfernen.
3. Prüfen, ob die Tests auch ohne lokale Abhängigkeiten (Ollama etc.) in CI bestehen.
4. Wenn einzelne Tests einen laufenden Provider brauchen, diese mit `[Trait("Category", "Live")]` taggen und den CI-Filter auf diesen Trait umstellen.

Option B — Dokumentieren:
1. In `README.md` oder `docs/guides/ci-and-quality-gates.md` als bekannte Einschränkung aufnehmen.

---

### Schritt 2.2: CI — Unnötiges .NET 8.0.x SDK entfernen

**Problem:** `ci.yml` und `release.yml` installieren `8.0.x`, obwohl alle Projekte `net10.0` targeten.

**Schritte:**

1. Beide Workflow-Dateien öffnen.
2. Unter `setup-dotnet` die Zeile `8.0.x` entfernen.
3. Nur `10.0.x` behalten.
4. Pipeline-Lauf triggern und prüfen, dass alles grün bleibt.

---

### Schritt 2.3: Smoke-Tests für 3 Consumer-kritische Projekte anlegen

**Problem:** 6 Source-Projekte haben keine Tests (+ Testing = N/A). Die drei Consumer-kritischsten sind:

| Projekt | Risiko | Begründung |
|---------|--------|------------|
| `Nexus.Hosting.AspNetCore` | Hoch | Consumer-facing HTTP-Endpoints |
| `Nexus.Auth.OAuth2` | Mittel | Token-Logik, Security-relevant |
| `Nexus.Protocols.A2A` | Mittel | HTTP-Client + JSON-RPC |

**Schritte (je Projekt):**

1. Test-Projekt anlegen:
   ```bash
   dotnet new xunit -n Nexus.<Name>.Tests -o tests/Nexus.<Name>.Tests
   ```
2. Referenzen hinzufügen (Quellprojekt, FluentAssertions, NSubstitute).
3. Mindestens 3-5 Smoke-Tests schreiben:
   - Hosting: Endpoint-Registration, Health-Check-Response
   - Auth: Token-Cache-Verhalten, Client-Credentials-Flow mit Mock-HTTP
   - A2A: Task-Serialisierung, JSON-RPC-Request-Format
4. In Solution aufnehmen und CI-Lauf verifizieren.

---

### Schritt 2.4: Package-Descriptions in csproj-Dateien ergänzen

**Problem:** 14 Projekte erben die generische Description „Multi-Agent Orchestration Engine for .NET". Auf NuGet.org sieht der Consumer keinen Unterschied.

**Schritte:**

Folgende Projekte brauchen eine spezifische `<Description>` im `.csproj`:

| Projekt | Vorgeschlagene Description |
|---------|---------------------------|
| `Nexus.Orchestration` | Graph-based multi-agent orchestration with sequence, parallel, and hierarchical execution |
| `Nexus.Orchestration.Checkpointing` | Snapshot serialization and restore for orchestration state |
| `Nexus.Memory` | Conversation history and working memory for Nexus agents |
| `Nexus.Messaging` | Inter-agent pub/sub messaging, shared state, and dead letter queue |
| `Nexus.Guardrails` | PII redaction, prompt injection detection, and secrets scanning |
| `Nexus.CostTracking` | Token counting and USD cost tracking per agent and session |
| `Nexus.Permissions` | Tool approval rules and permission policies |
| `Nexus.Commands` | Slash command framework for interactive agent sessions |
| `Nexus.Skills` | Skill injection middleware for agent capabilities |
| `Nexus.Telemetry` | OpenTelemetry traces and metrics middleware for Nexus |
| `Nexus.Testing` | Mock agents, fake LLM clients, and event recording for tests |
| `Nexus.Hosting.AspNetCore` | ASP.NET Core endpoints for A2A, AG-UI SSE, and health checks |
| `Nexus.Protocols.A2A` | Agent-to-Agent protocol client with JSON-RPC transport |
| `Nexus.Protocols.AgUi` | AG-UI event bridge for frontend streaming |

**Schritte pro Projekt:**

1. `src/Nexus.<Name>/Nexus.<Name>.csproj` öffnen.
2. Im `<PropertyGroup>` ergänzen:
   ```xml
   <Description>...</Description>
   ```
3. Sammel-Commit:
   ```bash
   git commit -m "chore: add specific NuGet descriptions to all packages"
   ```

---

## Phase 3 — MITTEL (erste Woche nach Übergabe)

> Kann mit Consumer-Feedback priorisiert werden.

---

### Schritt 3.1: Coverage automatisieren

1. In `ci.yml` Coverage-Erhebung ergänzen:
   ```yaml
   - name: Test with coverage
     run: dotnet test --collect:"XPlat Code Coverage" --results-directory ./artifacts/coverage
   ```
2. Report-Generator einbinden (z.B. `reportgenerator`).
3. Schwellenwerte definieren (z.B. 70% Line-Coverage für Core-Packages).
4. Coverage-Badge in README aus CI-Artefakt generieren.

---

### Schritt 3.2: Architektur-Diagramm vervollständigen

Das Mermaid-Diagramm in der README zeigt nur die Haupt-Layer. Fehlende Packages ergänzen:

- `Nexus.AgentLoop` (Runtime-Layer)
- `Nexus.Sessions` (Runtime-Layer)
- `Nexus.Compaction` (Runtime-Layer)
- `Nexus.Configuration` (Infrastructure-Layer)
- `Nexus.Tools.Standard` (Runtime-Layer)
- `Nexus.Defaults` (Convenience-Layer, ganz oben)

---

### Schritt 3.3: Schwach getestete Module ausbauen

| Projekt | Aktuelle Tests | Empfohlenes Minimum | Fokus |
|---------|---------------|---------------------|-------|
| `Nexus.Compaction.Tests` | 4 | 8-10 | Strategie-Auswahl, Token-Counting-Edge-Cases |
| `Nexus.Configuration.Tests` | 3 | 8-10 | Settings-Merge, Provider-Fallback, Override-Reihenfolge |
| `Nexus.Defaults.Tests` | 3 | 6-8 | Alle Services registriert, Builder-Extensions korrekt |
| `Nexus.Protocols.Mcp.Tests` | 3 | 8-10 | Tool-Discovery, Transport-Fehler, Timeout |

---

### Schritt 3.4: DI-Pattern-Konsistenz herstellen

**Problem:** Die meisten Packages nutzen `NexusBuilder.Add*()`, aber:
- `Nexus.Workflows.Dsl` registriert direkt auf `IServiceCollection`
- `Nexus.CostTracking` nutzt `Configure()` statt `Add*()`

**Schritte:**

1. Builder-Extensions für beide Packages anlegen:
   ```csharp
   public static NexusBuilder AddWorkflowsDsl(this NexusBuilder builder) { ... }
   public static NexusBuilder AddCostTracking(this NexusBuilder builder) { ... }
   ```
2. Alte Registrierungsmethoden als `[Obsolete]` markieren oder entfernen.
3. Docs/Examples aktualisieren.

---

### Schritt 3.5: Public-API-Surface bereinigen

Kandidaten für `internal`:
- `SlashCommandDispatcher` (Commands)
- `CommandRegistry` (Commands)
- `NexusDefaultHost` (Defaults)
- `AgentIdJsonConverter`, `TaskIdJsonConverter` (Core)

**Vorgehen:**
1. Sicherstellen, dass kein Consumer-Code diese Typen direkt referenziert.
2. Auf `internal` setzen.
3. `[InternalsVisibleTo]` für zugehörige Test-Projekte ergänzen, falls Tests betroffen sind.

---

## Phase 4 — NICE-TO-HAVE (laufend)

---

### Schritt 4.1: CHANGELOG.md anlegen

Initial-Eintrag für 0.1.0 mit den Kern-Features:
- 24 Packages, modulare Architektur
- Agent-Loop mit Session-Management
- Multi-Agent-Orchestration (Graph, Sequence, Parallel, Hierarchical)
- MCP/A2A/AG-UI Protokoll-Support
- Guardrails, Permissions, Cost-Tracking
- Workflow-DSL (JSON/YAML)
- ASP.NET Core Hosting

---

### Schritt 4.2: XML-Docs schrittweise ergänzen

Priorität auf die 10 wichtigsten Consumer-facing Interfaces:
1. `IAgent`
2. `IChatAgent`
3. `ITool`
4. `IOrchestrator`
5. `IAgentLoop`
6. `IMemoryStore`
7. `IGuardrail`
8. `IPermissionPolicy`
9. `ICostTracker`
10. `INexusBuilder`

CS1591-Suppression in `Directory.Build.props` danach schrittweise pro Projekt aufheben.

---

### Schritt 4.3: `docs/getting-started/quickstart.md` mit Inhalt füllen

Aktuell nur ein Redirect auf `guides/quick-start.md`. Entweder Inline-Inhalt oder sauberen Redirect (HTTP-style, nicht Markdown-Verweis).

---

### Schritt 4.4: Benchmarks aktualisieren

Falls Performance-relevante Änderungen seit dem letzten Lauf stattfanden:
```bash
dotnet run --project benchmarks/Nexus.Benchmarks -c Release
```
Ergebnisse in `BenchmarkDotNet.Artifacts/results/` committen.

---

## Pilot-Übergabe-Framing

Empfohlener Kommunikationsrahmen gegenüber dem Consumer:

> **„Robuste erste Integrationsbasis — pilot-ready."**
>
> - Getestet: 331 Tests, alle grün, 20 Test-Projekte
> - Dokumentiert: 57 Docs, 20 Guides, 10 Recipes, 8 Examples
> - Empfohlener Einstieg: `Nexus.Examples.Minimal` oder `Nexus.Examples.SingleAgentWithTools`
> - Validierte End-to-End-Beispiele: ChatSessionWithMemory, ChatEditingWithDiffAndRevert
> - Bekannte Einschränkung: XML-Docs auf Core-APIs noch nicht vollständig, Test-Coverage bei Leaf-Packages (Auth, Hosting, Telemetry) noch dünn
> - Erwartung: Gezieltes Feedback zur Developer Experience, wir schärfen iterativ nach

---

## Checkliste (zum Abhaken)

### Phase 1 — BLOCKER (heute)
- [ ] 1.1 `.nexus/` in .gitignore + Artefakte entfernen
- [ ] 1.2 `release.yml` um 10 Packages erweitern
- [ ] 1.3 `installation.md` um 10 Packages erweitern
- [ ] 1.4 README Badge-URL korrigieren
- [ ] 1.5 README Test-Zahl + Coverage aktualisieren
- [ ] 1.6 README ChatEditing-Example aufnehmen

### Phase 2 — HOCH (vor/bei Übergabe)
- [ ] 2.1 CI: Cli.Tests aufnehmen oder dokumentieren
- [ ] 2.2 CI: .NET 8.0.x entfernen
- [ ] 2.3 Smoke-Tests: Hosting, Auth, A2A
- [ ] 2.4 Package-Descriptions in 14 csproj-Dateien

### Phase 3 — MITTEL (erste Woche)
- [ ] 3.1 Coverage automatisieren
- [ ] 3.2 Architektur-Diagramm vervollständigen
- [ ] 3.3 Schwach getestete Module ausbauen
- [ ] 3.4 DI-Pattern-Konsistenz
- [ ] 3.5 Public-API-Surface bereinigen

### Phase 4 — NICE-TO-HAVE (laufend)
- [ ] 4.1 CHANGELOG.md
- [ ] 4.2 XML-Docs auf Core-Interfaces
- [ ] 4.3 Quickstart.md mit Inhalt füllen
- [ ] 4.4 Benchmarks aktualisieren
