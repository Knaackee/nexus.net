namespace Nexus.Tools.Standard;

public sealed class StandardToolOptions
{
    public string BaseDirectory { get; set; } = Directory.GetCurrentDirectory();

    public string? WorkingDirectory { get; set; }

    public int MaxReadLines { get; set; } = 400;

    public int MaxSearchResults { get; set; } = 200;

    public int MaxFetchCharacters { get; set; } = 20_000;

    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ShellTimeout { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed record ShellCommandResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record GrepMatch(string Path, int LineNumber, string Line);

public sealed record WebFetchResult(string Url, int StatusCode, string Content);

public sealed record AgentToolResult(string? Text, string Status, decimal? EstimatedCost);

public sealed record EditResult(string Path, int Replacements);

public sealed record FileWriteResult(string Path, int CharacterCount);