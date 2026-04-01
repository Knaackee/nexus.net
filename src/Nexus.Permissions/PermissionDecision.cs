namespace Nexus.Permissions;

public abstract record PermissionDecision;

public sealed record PermissionGranted(string? Reason = null) : PermissionDecision;

public sealed record PermissionDenied(string Reason) : PermissionDecision;

public sealed record PermissionAsk(string? Reason = null, TimeSpan? Timeout = null) : PermissionDecision;