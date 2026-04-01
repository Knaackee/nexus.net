using Nexus.Skills;

namespace Nexus.Cli;

internal static class CliSkillCatalog
{
    public const string DefaultSkillName = "coding";

    public static SkillCatalog CreateDefaultCatalog(CliWorkspaceOptions? workspace = null)
    {
        var catalog = new SkillCatalog();
        catalog.Register(new SkillDefinition
        {
            Name = "chat",
            Description = "General chat assistant.",
            SystemPrompt = "Focus on direct answers. Use tools only if the user asks for actions or repo-specific investigation.",
        });
        catalog.Register(new SkillDefinition
        {
            Name = DefaultSkillName,
            Description = "Coding assistant with the standard Nexus tools.",
            SystemPrompt = "Act like a pragmatic software engineer. Inspect existing code before changing it, prefer minimal patches, and verify important changes.",
            ToolNames = ["file_read", "file_write", "file_edit", "glob", "grep", "shell", "web_fetch", "ask_user", "agent"],
        });

        if (workspace is null)
            return catalog;

        var loader = new MarkdownSkillLoader();
        foreach (var skill in loader.LoadFromDirectory(workspace.UserSkillDirectory, SkillSource.User, optional: true))
            catalog.Register(skill);

        foreach (var skill in loader.LoadFromDirectory(workspace.ProjectSkillDirectory, SkillSource.Project, optional: true))
            catalog.Register(skill);

        foreach (var directory in workspace.ExtraSkillDirectories)
        {
            foreach (var skill in loader.LoadFromDirectory(directory, SkillSource.Custom, optional: true))
                catalog.Register(skill);
        }

        return catalog;
    }
}