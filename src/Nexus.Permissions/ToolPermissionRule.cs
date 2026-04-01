using System.Text.RegularExpressions;

namespace Nexus.Permissions;

public sealed record ToolPermissionRule
{
    public required string Pattern { get; init; }
    public PermissionAction Action { get; init; }
    public PermissionRuleSource Source { get; init; } = PermissionRuleSource.Default;
    public int Priority { get; init; }
    public string? Reason { get; init; }
    public Func<ToolPermissionContext, bool>? Condition { get; init; }

    public bool Matches(ToolPermissionContext context)
    {
        if (!WildcardMatcher.IsMatch(context.ToolName, Pattern))
            return false;

        return Condition?.Invoke(context) ?? true;
    }

    private static class WildcardMatcher
    {
        public static bool IsMatch(string input, string pattern)
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}