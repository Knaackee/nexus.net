# Nexus.Permissions API Reference

## Namespace: Nexus.Permissions

### PermissionAction

```csharp
public enum PermissionAction
{
    Allow,
    Deny,
    Ask,
}
```

### PermissionRuleSource

```csharp
public enum PermissionRuleSource
{
    Default,
    Project,
    User,
    Managed,
}
```

### PermissionPreset

```csharp
public enum PermissionPreset
{
    AllowAll,
    ReadOnly,
    Interactive,
}
```

### ToolPermissionRule

```csharp
public sealed record ToolPermissionRule
{
    public required string Pattern { get; init; }
    public PermissionAction Action { get; init; }
    public PermissionRuleSource Source { get; init; }
    public int Priority { get; init; }
    public string? Reason { get; init; }
    public Func<ToolPermissionContext, bool>? Condition { get; init; }
}
```

### PermissionOptions

```csharp
public sealed class PermissionOptions
{
    public List<ToolPermissionRule> Rules { get; }
    public PermissionAction DefaultAction { get; set; }
    public TimeSpan? AskTimeout { get; set; }
}
```

### IToolPermissionHandler

```csharp
public interface IToolPermissionHandler
{
    Task<PermissionDecision> EvaluateAsync(
        ITool tool,
        JsonElement input,
        IToolContext context,
        CancellationToken ct = default);
}
```

### IPermissionPrompt

```csharp
public interface IPermissionPrompt
{
    Task<ApprovalResult> PromptAsync(
        ApprovalRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
```

### RuleBasedApprovalGate

```csharp
public sealed class RuleBasedApprovalGate : IApprovalGate
```

Uses permission rules to convert approval requests into `allow`, `deny`, or `ask` outcomes.

### PermissionToolMiddleware

```csharp
public sealed class PermissionToolMiddleware : IToolMiddleware
```

Applies the same rules to a dedicated tool pipeline.

### Builder Extensions

```csharp
public static class PermissionServiceCollectionExtensions
{
    public static PermissionBuilder Configure(this PermissionBuilder builder, Action<PermissionOptions> configure);
    public static PermissionBuilder UsePreset(this PermissionBuilder builder, PermissionPreset preset);
    public static PermissionBuilder UseConsolePrompt(this PermissionBuilder builder);
    public static PermissionBuilder AddRule(this PermissionBuilder builder, ToolPermissionRule rule);
}
```