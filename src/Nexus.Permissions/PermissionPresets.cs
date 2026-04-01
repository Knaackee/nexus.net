namespace Nexus.Permissions;

public static class PermissionPresets
{
    public static void Apply(PermissionPreset preset, PermissionOptions options)
    {
        options.Rules.Clear();

        switch (preset)
        {
            case PermissionPreset.AllowAll:
                options.DefaultAction = PermissionAction.Allow;
                break;

            case PermissionPreset.ReadOnly:
                options.DefaultAction = PermissionAction.Deny;
                options.Rules.Add(new ToolPermissionRule
                {
                    Pattern = "*",
                    Action = PermissionAction.Allow,
                    Source = PermissionRuleSource.Default,
                    Condition = ctx => ctx.Annotations?.IsReadOnly == true,
                    Reason = "Read-only tools are allowed",
                });
                break;

            case PermissionPreset.Interactive:
                options.DefaultAction = PermissionAction.Ask;
                options.Rules.Add(new ToolPermissionRule
                {
                    Pattern = "*",
                    Action = PermissionAction.Allow,
                    Source = PermissionRuleSource.Default,
                    Condition = ctx => ctx.Annotations?.IsReadOnly == true,
                    Reason = "Read-only tools are allowed",
                });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
        }
    }
}