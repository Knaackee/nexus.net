# Nexus CLI

The Nexus CLI (`Nexus.Cli`) is an interactive multi-chat coding agent powered by GitHub Copilot. It demonstrates real-world usage of the Nexus framework with `Nexus.Defaults`, `IAgentLoop`, standard tools, slash commands, and reusable skills.

## Features

- **GitHub Copilot Authentication** — OAuth device flow with automatic token caching
- **Multi-Chat Support** — Create, switch, rename, and delete chat sessions
- **Streaming Responses** — Real-time token-by-token output through `AgentLoop` events
- **Rich Terminal UI** — Built with Spectre.Console for formatted markdown and status indicators
- **Slash Commands** — Powered by `Nexus.Commands`
- **Skill Profiles** — Powered by `Nexus.Skills`
- **MCP Tools** — Optional MCP server loading from project/user `.nexus/mcp.json`

## Running

```bash
cd examples/Nexus.Cli
dotnet run
```

On first run, you'll be prompted to authenticate with GitHub:

```
→ Open https://github.com/login/device
→ Enter code: XXXX-XXXX
Waiting for authorization...
✓ Authenticated as your-username
```

## Commands

| Command | Description |
|---------|-------------|
| `/new <key> [model] [skill]` | Create a new chat session |
| `/list` | List all chat sessions |
| `/switch <id>` | Switch to a different session |
| `/remove <id>` | Delete a chat session |
| `/skill [name]` | List skills or switch the active session skill |
| `/models` | Show available models |
| `/model [name]` | Show or switch the active model |
| `/resume [key]` | Resume the latest persisted session |
| `/cost` | Show token and cost information |
| `/status` | Show session status and output previews |
| `/clear` | Clear the active session conversation |
| `/compact` | Compact the active session history |
| `/cancel` | Cancel the active request |
| `/help` | Show available commands |
| `/quit` | Exit the CLI |

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