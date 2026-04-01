namespace Nexus.Cli;

internal sealed record CliWorkspaceOptions(
    string ProjectRoot,
    string NexusDirectory,
    string SessionDirectory,
    string ProjectMcpConfigPath,
    string UserMcpConfigPath,
    string ProjectSkillDirectory,
    string UserSkillDirectory,
    string ProjectCommandDirectory,
    string UserCommandDirectory,
    IReadOnlyList<string> ExtraSkillDirectories,
    IReadOnlyList<string> ExtraCommandDirectories)
{
    public static CliWorkspaceOptions Create(string projectRoot)
    {
        var normalizedProjectRoot = Path.GetFullPath(projectRoot);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var nexusDirectory = Path.Combine(normalizedProjectRoot, ".nexus");
        var userNexusDirectory = Path.Combine(userProfile, ".nexus");

        var extraSkillDirectories = ParsePathListEnvironmentVariable("NEXUS_CLI_EXTRA_SKILL_DIRS");
        if (IsEnabled("NEXUS_CLI_INCLUDE_CLAUDE_SKILLS"))
            extraSkillDirectories.Add(Path.Combine(userProfile, ".claude", "skills"));

        return new CliWorkspaceOptions(
            normalizedProjectRoot,
            nexusDirectory,
            Path.Combine(nexusDirectory, "sessions"),
            Path.Combine(nexusDirectory, "mcp.json"),
            Path.Combine(userNexusDirectory, "mcp.json"),
            Path.Combine(nexusDirectory, "skills"),
            Path.Combine(userNexusDirectory, "skills"),
            Path.Combine(nexusDirectory, "commands"),
            Path.Combine(userNexusDirectory, "commands"),
            extraSkillDirectories,
            ParsePathListEnvironmentVariable("NEXUS_CLI_EXTRA_COMMAND_DIRS"));
    }

    private static List<string> ParsePathListEnvironmentVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsEnabled(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}