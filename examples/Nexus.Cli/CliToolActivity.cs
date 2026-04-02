namespace Nexus.Cli;

internal sealed record CliToolActivity(
    DateTimeOffset Timestamp,
    string ToolName,
    string Status,
    string Message,
    int? ChangeId = null,
    string? Path = null);