namespace Nexus.Orchestration;

/// <summary>
/// Compact prompt policy injected into agent system prompts when the ask_user tool is available.
/// Kept minimal to preserve space in user-owned system prompts.
/// </summary>
internal static class AskUserPolicy
{
    public const string ToolName = "ask_user";

    public const string Text =
        "When using ask_user: " +
        "(1) ≥2 interpretations of user intent → ask before acting (prefer type=select or type=confirm); " +
        "(2) destructive, irreversible, or costly action → confirm first unless already gated; " +
        "(3) max 1 unverified assumption per turn — if more needed, ask; " +
        "(4) never silently override stated user preferences; " +
        "(5) keep questions short and decision-oriented — use type=confirm or type=select over freeText when choices are enumerable.";
}
