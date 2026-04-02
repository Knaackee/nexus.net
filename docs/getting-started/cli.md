# Nexus CLI

The Nexus CLI (`Nexus.Cli`) is the interactive Nexus host for real multi-chat sessions, slash commands, reusable skills, and MCP-backed tools.

Use this page as the shortest entry path. The full CLI architecture and reference live in [../examples/nexus-cli.md](../examples/nexus-cli.md).

## Running

```bash
cd examples/Nexus.Cli
dotnet run
```

Fullscreen mode is available via:

```bash
dotnet run --project examples/Nexus.Cli -- --tui
```

The TUI now runs with a dedicated state store and multi-pane layout instead of line-by-line console writes. Use `Ctrl+P` for the session picker, `Ctrl+K` for the command palette, `Ctrl+F` for transcript filtering, and `Ctrl+E` to export the current transcript.

On first run with GitHub Copilot, you'll be prompted to authenticate with GitHub:

```
→ Open https://github.com/login/device
→ Enter code: XXXX-XXXX
Waiting for authorization...
✓ Authenticated as your-username
```

If you switch the provider via environment configuration, the CLI discovers the available models from that provider at runtime instead of using a baked-in model list.

## Core Flow

- Run `/models` to inspect the active provider's discovered model list.
- Run `/new <key> [model] [skill]` to create a chat.
- Send a normal message to the active chat.
- Use `/status`, `/cost`, `/list`, and `/resume` to inspect or continue work.
- Use `/changes`, `/diff`, `/revert`, and `/tools` after tool-driven edits to inspect or undo file mutations.

## Where To Go Next

- Full CLI reference and architecture: [../examples/nexus-cli.md](../examples/nexus-cli.md)
- Runnable examples index: [../../examples/README.md](../../examples/README.md)
- Framework quick start: [quickstart.md](quickstart.md)

## Architecture

The CLI is built from six main components:

- **`CopilotAuth`** — Handles GitHub device flow OAuth, token persistence in `~/.nexus/copilot-token.json`, and automatic refresh
- **`CopilotChatClient`** — Implements `IChatClient` by talking to the GitHub Copilot Chat Completions API with SSE streaming
- **`ChatSession`** — Hosts one `NexusDefaultHost`, persists session state through `IAgentLoop`, and streams loop events back to the UI
- **`ChatManager`** — Coordinates multiple sessions, active-session switching, and per-session skills
- **`Nexus.Commands`** — Provides the slash-command dispatcher used by the CLI
- **`Nexus.Skills`** — Provides reusable prompt/tool profiles such as the default coding skill
- **`Nexus.Protocols.Mcp`** — Loads configured MCP servers and exposes their discovered tools to the runtime registry

## MCP Configuration

The CLI automatically checks two optional config files:

- `.nexus/mcp.json` inside the current project
- `~/.nexus/mcp.json` inside the current user profile

Project config overrides user config for servers with the same name.

```json
{
	"servers": {
		"filesystem": {
			"command": "npx",
			"args": ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"]
		}
	}
}
```

## Budget And Cost Notes

The current `Nexus.Cli` example now runs through `Nexus.Defaults` and `IAgentLoop`, so it shares the same agent execution stack as the rest of the framework. Budget enforcement therefore works the same way as in the other examples:

```csharp
nexus.AddCostTracking(c => c.AddModel("gpt-4o", input: 2.50m, output: 10.00m));

var agent = await pool.SpawnAsync(new AgentDefinition
{
	Name = "Assistant",
	Budget = new AgentBudget { MaxCostUsd = 0.50m },
});
```

The crucial requirement is still provider usage metadata. Without usage in the underlying chat responses, the CLI can stream text but cannot enforce `MaxCostUsd`.

## Troubleshooting

- If cost totals stay at zero, verify that the provider returns usage metadata at all.
- If token totals increase but cost stays zero, verify that the resolved model ID matches a configured pricing entry.
- If budget totals stay at zero, inspect the underlying provider response for usage metadata before assuming the loop is misconfigured.

## Example Session

```
Nexus CLI v1.0 — Type /help for commands

You: Explain the builder pattern in C#