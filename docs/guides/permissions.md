# Permissions

`Nexus.Permissions` provides rule-based tool authorization on top of Nexus tool metadata and `IApprovalGate`.

It covers two paths:

- `RuleBasedApprovalGate` for the current `ChatAgent` integration
- `PermissionToolMiddleware` for future tool-pipeline execution

## Package Setup

```csharp
using Nexus.Permissions;

services.AddNexus(nexus =>
{
    nexus.AddPermissions(p => p
        .UsePreset(PermissionPreset.Interactive)
        .UseConsolePrompt());
});
```

`Interactive` means:

- read-only tools are allowed automatically
- non-read-only tools fall through to approval

## Tool Metadata

Permissions work best when tools declare their intent:

```csharp
var writeTool = new LambdaTool(
    "write_file",
    "Writes text to disk",
    (input, ctx, ct) => Task.FromResult(ToolResult.Success("ok")))
{
    Annotations = new ToolAnnotations
    {
        IsReadOnly = false,
        RequiresApproval = true,
    }
};
```

For safe read-only tools:

```csharp
var readTool = new LambdaTool(
    "read_file",
    "Reads a file",
    (input, ctx, ct) => Task.FromResult(ToolResult.Success("content")))
{
    Annotations = new ToolAnnotations
    {
        IsReadOnly = true,
        IsIdempotent = true,
    }
};
```

## Presets

### AllowAll

```csharp
nexus.AddPermissions(p => p.UsePreset(PermissionPreset.AllowAll));
```

All tools are allowed.

### ReadOnly

```csharp
nexus.AddPermissions(p => p.UsePreset(PermissionPreset.ReadOnly));
```

Read-only tools are allowed. Everything else is denied.

### Interactive

```csharp
nexus.AddPermissions(p => p
    .UsePreset(PermissionPreset.Interactive)
    .UseConsolePrompt());
```

Read-only tools are allowed. Everything else asks a human.

## Custom Rules

Rules support wildcard matching and precedence by source:

```csharp
nexus.AddPermissions(p =>
{
    p.UsePreset(PermissionPreset.Interactive);
    p.AddRule(new ToolPermissionRule
    {
        Pattern = "shell",
        Action = PermissionAction.Deny,
        Source = PermissionRuleSource.Managed,
        Reason = "Shell access is disabled in production",
    });

    p.AddRule(new ToolPermissionRule
    {
        Pattern = "file_*",
        Action = PermissionAction.Allow,
        Source = PermissionRuleSource.Project,
    });
});
```

Rule precedence is:

1. `Managed`
2. `User`
3. `Project`
4. `Default`

Within the same source, higher `Priority` wins.

## Approval Flow

When a tool has `RequiresApproval = true`, `ChatAgent` asks the current `IApprovalGate` before execution.

With `Nexus.Permissions` registered, that gate becomes `RuleBasedApprovalGate`.

The flow is:

1. Agent emits `ApprovalRequestedEvent`
2. Permission rules decide `Allow`, `Deny`, or `Ask`
3. If `Ask`, the configured prompt is invoked
4. Decision is written to the audit log
5. Agent either executes the tool or returns `ToolResult.Denied(...)`

## Middleware Path

The package also exposes `PermissionToolMiddleware`:

```csharp
var middleware = serviceProvider.GetRequiredService<PermissionToolMiddleware>();
```

That is intended for the dedicated tool pipeline. It uses the same rules and prompt abstraction as the approval gate.

## Prompt Abstraction

Console is only one implementation. The key abstraction is `IPermissionPrompt`:

```csharp
public interface IPermissionPrompt
{
    Task<ApprovalResult> PromptAsync(
        ApprovalRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
```

For a web app, replace it with a frontend-backed prompt implementation.

## Testing

`Nexus.Permissions.Tests` covers:

- wildcard rule matching
- source precedence
- presets
- approval gate behavior
- middleware behavior
- DI registration