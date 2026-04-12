namespace Nexus.Orchestration;

/// <summary>
/// Compact prompt policy injected into agent system prompts when the ask_user tool is available.
/// Kept minimal to preserve space in user-owned system prompts.
/// </summary>
internal static class AskUserPolicy
{
    public const string ToolName = "ask_user";

    public const string Text =
    "SYSTEM POLICY (ask_user): If user decision needed, call ask_user before acting; do not ask decision menus in plain text. " +
    "Mandatory when intent ambiguous, required parameter missing, multiple valid paths exist, or action is risky/expensive/irreversible/user-visible. " +
    "Decision changes outcome => ask_user; informational clarification with no outcome impact may be plain text. " +
    "Type rules: confirm=yes/no, select=one option, multiSelect=many, freeText only when options cannot be enumerated, secret for sensitive values; use type as canonical field. " +
    "Questions must be short, specific, action-oriented; for select/multiSelect include concrete options. " +
    "If ask_user unavailable/fails, state fallback and ask one concise plain-text question. " +
    "After answer, restate selected option briefly and continue immediately.";
}
