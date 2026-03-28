# Nexus CLI

The Nexus CLI (`Nexus.Cli`) is an interactive multi-chat coding agent powered by GitHub Copilot. It demonstrates real-world usage of the Nexus framework with streaming, tool integration, and conversation management.

## Features

- **GitHub Copilot Authentication** — OAuth device flow with automatic token caching
- **Multi-Chat Support** — Create, switch, rename, and delete chat sessions
- **Streaming Responses** — Real-time token-by-token output via SSE
- **Rich Terminal UI** — Built with Spectre.Console for formatted markdown and status indicators

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
| `/new` | Create a new chat session |
| `/list` | List all chat sessions |
| `/switch <id>` | Switch to a different session |
| `/rename <name>` | Rename the current session |
| `/delete <id>` | Delete a chat session |
| `/model <name>` | Change the model (default: gpt-4o) |
| `/clear` | Clear the current conversation |
| `/help` | Show available commands |
| `/quit` | Exit the CLI |

## Architecture

The CLI is built from four main components:

- **`CopilotAuth`** — Handles GitHub device flow OAuth, token persistence in `~/.nexus/copilot-token.json`, and automatic refresh
- **`CopilotChatClient`** — Implements `IChatClient` by talking to the GitHub Copilot Chat Completions API with SSE streaming
- **`ChatSession`** — Manages conversation history for a single chat thread
- **`ChatManager`** — Coordinates multiple sessions, handles routing and lifecycle

## Example Session

```
Nexus CLI v1.0 — Type /help for commands

You: Explain the builder pattern in C#