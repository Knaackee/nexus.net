# Example: Nexus CLI

The `Nexus.Cli` is a fully-featured interactive coding agent built with Spectre.Console on top of `Nexus.Defaults`, `IAgentLoop`, `Nexus.Commands`, and `Nexus.Skills`.

**Location:** `examples/Nexus.Cli/`

## What It Shows

- Real-world provider-backed `IChatClient` implementations
- Provider authentication and token caching for hosted providers
- Server-Sent Events (SSE) streaming parsing
- Multi-session agent-loop management
- Chat-driven file editing with tracked diffs and revert support
- A full-screen `--tui` mode with alternate-screen rendering, centralized state, multi-pane layout, and line-mode fallback
- Rich terminal rendering with Spectre.Console
- Slash-command dispatch with `Nexus.Commands`
- Reusable skill profiles with `Nexus.Skills`
- MCP server loading from project/user `.nexus/mcp.json`
- Runtime model discovery from the active provider

## Architecture

```
Program (Spectre) → CommandDispatcher / SkillCatalog → ChatManager → ChatSession
                                                                │
                                                                ▼
                                                          NexusDefaultHost
                                                                │
                                                                ▼
                                                          IAgentLoop + StandardTools
```

### Components

**CopilotAuth** — GitHub OAuth device flow with token caching in `~/.nexus/copilot-token.json` and automatic refresh.

**CopilotChatClient** — Implements `IChatClient` against the GitHub Copilot Chat Completions API with SSE streaming.

**CliChatProviders** — Resolves the active provider, authenticates when required, and discovers the available model IDs at runtime.

**ChatSession** — Owns a `NexusDefaultHost`, resumes the same loop-backed session over time, and forwards streamed loop events to the terminal.

**ChatManager** — Coordinates multiple sessions with create, switch, delete, and per-session skill operations.

**Nexus.Commands** — Parses slash commands and dispatches them to host-side command handlers.

**Nexus.Skills** — Stores reusable prompt/tool bundles that shape each session's `AgentDefinition`.

**Nexus.Protocols.Mcp** — Connects configured MCP servers and registers discovered tools into the session host before the loop runs.

## Commands

The CLI command surface is intentionally documented here as the canonical reference for the interactive host.

| Command | Description |
|---------|-------------|
| `/new <key> [model] [skill]` | Create a new chat session |
| `/list` | List all sessions |
| `/switch <id>` | Switch active session |
| `/remove <id>` | Delete a session |
| `/skill [name]` | List skills or switch the active session skill |
| `/models` | Show available models |
| `/model [name]` | Show or switch the active model |
| `/resume [key]` | Resume the latest persisted chat session |
| `/cost` | Show token and cost information |
| `/status` | Show current states and previews |
| `/changes` | List tracked file changes from tool-driven edits |
| `/diff [changeId]` | Show the unified diff for the latest or selected tracked change |
| `/revert [changeId]` | Revert the latest or selected tracked change |
| `/tools` | Show the recent tool activity stream for the active session |
| `/clear` | Clear the active session conversation |
| `/compact` | Compact the active session history |
| `/cancel` | Cancel the active request |
| `/help` | Show commands |
| `/quit` | Exit |

## Running

```bash
cd examples/Nexus.Cli
dotnet run
```

For the new fullscreen terminal experience:

```bash
dotnet run --project examples/Nexus.Cli -- --tui
```

## TUI Layout

The fullscreen host now uses a single render loop and a state store instead of writing directly to the terminal from session logic.

- header and status lines for provider, workspace, active session, and notices
- session sidebar that collapses away on narrower terminals
- transcript pane with scrollback and filter mode
- tool activity pane with grouped status lines
- multiline composer with cursor movement and history recall

## TUI Shortcuts

| Shortcut | Action |
|---------|--------|
| `Enter` | Send the current composer text |
| `Shift+Enter` | Insert a newline in the composer |
| `Tab` / `Shift+Tab` | Cycle active session |
| `Ctrl+P` | Open the session picker |
| `Ctrl+K` | Open the command palette |
| `Ctrl+F` | Open transcript search/filter |
| `Ctrl+G` | Clear the active transcript filter |
| `Ctrl+E` | Export the current transcript to `.nexus/exports/` |
| `Ctrl+L` | Clear the active chat |
| `Ctrl+C` | Cancel the active request, or exit if idle |
| `Ctrl+Q` | Exit the TUI |
| `PageUp` / `PageDown` | Scroll transcript viewport |

The TUI also falls back automatically to the existing line-oriented mode when stdin/stdout is redirected or the terminal is too small for the fullscreen layout.

On first run with GitHub Copilot, authenticate via GitHub device flow. The token is cached for subsequent runs.

Model selection is provider-driven. `/models` shows the currently discovered runtime list for the active provider, and `/new` without an explicit model uses that provider's current default.

If `.nexus/mcp.json` or `~/.nexus/mcp.json` exists, the CLI also loads configured MCP servers and makes their tools available to the active session.

When an agent edits a file through `file_edit` or `file_write`, the CLI now records that mutation in a shared file-change journal. The `/changes`, `/diff`, and `/revert` commands surface the same tracked state that other hosts can read through `IFileChangeJournal`.

## Budget Enforcement Note

This example now runs tasks through the default Nexus agent stack, so CLI behavior matches the core framework much more closely. CLI output can surface the same kinds of terminal results as other loop-based hosts, including:

- `AgentResultStatus.BudgetExceeded`
- per-task `TokenUsage`
- per-task `EstimatedCost`

Register `AddCostTracking(...)` and ensure the provider returns usage metadata if you want cost-aware enforcement in practice.

## Provider Troubleshooting

GitHub Copilot or other hosted providers may vary in how much usage metadata they surface through `Microsoft.Extensions.AI`.

If you add cost tracking and still see no budget enforcement:

1. Inspect the wrapped `IChatClient` responses for `Usage`, `UsageDetails`, or `AdditionalProperties["Usage"]`.
2. Confirm the resolved model name matches your configured pricing entry.
3. Fall back to token-count-only reporting if the provider does not expose billable usage data.
