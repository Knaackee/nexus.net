# Example: Chat Editing With Diff And Revert

The `Nexus.Examples.ChatEditingWithDiffAndRevert` project demonstrates a coding-agent pattern where the model edits a file through `file_edit`, the runtime records the mutation, and the host can render a diff or revert the change later.

**Location:** `examples/Nexus.Examples.ChatEditingWithDiffAndRevert/`

## What It Shows

- file-system tool execution inside the normal agent loop
- file-change journaling through `IFileChangeJournal`
- unified diff generation for applied edits
- clean rollback through `RevertAsync(...)`
- a deterministic example that does not require an external provider

## Run It

```bash
dotnet run --project examples/Nexus.Examples.ChatEditingWithDiffAndRevert
```

## Validate It

```bash
dotnet test tests/Nexus.Examples.Tests --filter ChatEditingWithDiffAndRevert_TracksDiffAndCanRevert
```

## Related Assets

- runnable walkthrough: [../../examples/Nexus.Examples.ChatEditingWithDiffAndRevert/README.md](../../examples/Nexus.Examples.ChatEditingWithDiffAndRevert/README.md)
- interactive host using the same runtime surface: [nexus-cli.md](nexus-cli.md)