namespace Nexus.Permissions;

public sealed class PermissionOptions
{
    public List<ToolPermissionRule> Rules { get; } = [];
    public PermissionAction DefaultAction { get; set; } = PermissionAction.Ask;
    public TimeSpan? AskTimeout { get; set; }
}