# Example: Nexus CLI

The `Nexus.Cli` is a fully-featured interactive coding agent powered by GitHub Copilot, built with Spectre.Console.

**Location:** `examples/Nexus.Cli/`

## What It Shows

- Real-world `IChatClient` implementation (GitHub Copilot API)
- OAuth device flow authentication with token caching
- Server-Sent Events (SSE) streaming parsing
- Multi-session chat management
- Rich terminal rendering with Spectre.Console

## Architecture

```
Program (Spectre) → ChatManager (multi-session) → ChatSession (conversation)
                         │
                    ┌────┴────┐
                    ▼         ▼
              CopilotAuth   CopilotChatClient
             (device flow)   (IChatClient)
```

### Components

**CopilotAuth** — GitHub OAuth device flow with token caching in `~/.nexus/copilot-token.json` and automatic refresh.

**CopilotChatClient** — Implements `IChatClient` against the GitHub Copilot Chat Completions API with SSE streaming.

**ChatSession** — Manages conversation history for one chat thread.

**ChatManager** — Coordinates multiple sessions with create, switch, rename, and delete operations.

## Commands

| Command | Description |
|---------|-------------|
| `/new` | Create a new chat session |
| `/list` | List all sessions |
| `/switch <id>` | Switch active session |
| `/rename <name>` | Rename current session |
| `/delete <id>` | Delete a session |
| `/model <name>` | Change model (default: gpt-4o) |
| `/clear` | Clear conversation |
| `/help` | Show commands |
| `/quit` | Exit |

## Running

```bash
cd examples/Nexus.Cli
dotnet run
```

On first run, authenticate via GitHub device flow. The token is cached for subsequent runs.
