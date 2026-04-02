# Chat Editing With Diff And Revert

This example shows the coding-agent loop that most users expect from a CLI assistant: the model edits a real file through a tool call, the runtime records the change as part of the chat, exposes a diff, and can revert the edit later.

## What This Example Solves

Use this shape when:

- the agent should update workspace files directly
- file mutations need to remain inspectable after the turn finishes
- you want reversible edits instead of fire-and-forget file writes

## Project Files

- `Program.cs`: complete runnable example with a forced `file_edit` tool call
- `Nexus.Examples.ChatEditingWithDiffAndRevert.csproj`: project wiring
- tests: `tests/Nexus.Examples.Tests` using `ChatEditingWithDiffAndRevert_TracksDiffAndCanRevert`

## Run It

```powershell
dotnet run --project examples/Nexus.Examples.ChatEditingWithDiffAndRevert
```

## Validate It

```powershell
dotnet test tests/Nexus.Examples.Tests --filter ChatEditingWithDiffAndRevert_TracksDiffAndCanRevert
```

## Step By Step

### Step 1: Create a disposable workspace file

The example creates `notes.txt` in a temporary workspace and seeds it with `alpha` and `beta`.

Why this step exists:

- the file edit is real, not mocked away
- the diff and revert output become easy to inspect
- the example stays isolated from the repo working tree

### Step 2: Configure the default Nexus host for file editing

`Nexus.CreateDefault(...)` is configured with:

- `file_edit` as the only exposed tool
- a project root and tool base directory bound to the temp workspace
- permissive non-interactive permissions for the demo
- `AddFileChangeTracking(...)` so edits are journaled automatically

Why this step exists:

- the example demonstrates the same runtime surface the CLI now uses
- file tracking is attached as tool middleware, not by special-casing the example
- the journaling API can be reused by other hosts and UIs

### Step 3: Force one model tool call

The example chat client first emits a `FunctionCallContent` for `file_edit`, then summarizes the resulting tool output on the next pass.

Why this step exists:

- the example is deterministic and runnable without external credentials
- it proves the agent loop actually executes a write-capable tool
- the follow-up assistant turn shows that the edit is part of the conversation history

### Step 4: Inspect the tracked change

After the loop finishes, the example resolves `IFileChangeJournal`, prints the recorded diff, and shows the tracked change metadata.

Why this step exists:

- the diff is first-class runtime state, not console-only formatting
- external UIs such as `Nexus.Cli` can present the same journal data
- the example demonstrates how to build change review UX on top of the shared service

### Step 5: Revert the change

The example reverts the tracked change and prints the restored file contents.

Why this step exists:

- safe rollback is part of the workflow, not an afterthought
- the journal stores enough pre-change state to undo edits
- this is the concrete runtime behavior behind the CLI `/revert` command