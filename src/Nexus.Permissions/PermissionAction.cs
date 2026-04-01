namespace Nexus.Permissions;

public enum PermissionAction
{
    Allow,
    Deny,
    Ask,
}

public enum PermissionRuleSource
{
    Default,
    Project,
    User,
    Managed,
}

public enum PermissionPreset
{
    AllowAll,
    ReadOnly,
    Interactive,
}