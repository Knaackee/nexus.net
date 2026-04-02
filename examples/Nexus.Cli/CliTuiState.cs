using Nexus.Commands;

namespace Nexus.Cli;

internal sealed record CliTuiCommandItem(string Name, string Usage, string Description);

internal sealed record CliTuiSessionItem(
    string Key,
    string Model,
    string SkillName,
    ChatSessionState State,
    int MessageCount,
    bool IsActive);

internal sealed record CliRuntimeSnapshot(
    string RuntimeOutputText,
    IReadOnlyList<CliTuiSessionItem> Sessions,
    IReadOnlyList<CliToolActivity> ToolActivity,
    IReadOnlyList<CliTuiCommandItem> Commands,
    string ProviderName,
    string WorkspaceRoot,
    DateTimeOffset Timestamp);

internal sealed record CliTuiEditorState(
    string Text,
    int CursorIndex,
    IReadOnlyList<string> History,
    int HistoryOffset)
{
    public static CliTuiEditorState Empty { get; } = new(string.Empty, 0, [], -1);
}

internal sealed record CliTuiState(
    int Width,
    int Height,
    CliTerminalCapabilities Capabilities,
    string ProviderName,
    string WorkspaceRoot,
    string RuntimeOutputText,
    string TranscriptLogText,
    IReadOnlyList<CliTuiSessionItem> Sessions,
    IReadOnlyList<CliToolActivity> ToolActivity,
    IReadOnlyList<CliTuiCommandItem> Commands,
    CliTuiEditorState Editor,
    int TranscriptScrollOffset,
    string Notice,
    string ActiveSearchQuery,
    bool ShowSessionPicker,
    string SessionPickerQuery,
    bool ShowCommandPalette,
    string CommandPaletteQuery,
    bool ShowSearch,
    string SearchDraft,
    bool ForceFullRedraw,
    DateTimeOffset LastUpdated)
{
    public static CliTuiState Create(int width, int height, CliTerminalCapabilities capabilities)
        => new(
            width,
            height,
            capabilities,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            [],
            [],
            CliTuiEditorState.Empty,
            0,
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            false,
            string.Empty,
            false,
            string.Empty,
            true,
            DateTimeOffset.UtcNow);

    public string CombinedTranscript
        => string.IsNullOrWhiteSpace(TranscriptLogText)
            ? RuntimeOutputText
            : string.IsNullOrWhiteSpace(RuntimeOutputText)
                ? TranscriptLogText
                : TranscriptLogText + Environment.NewLine + RuntimeOutputText;
}

internal abstract record CliTuiAction;

internal sealed record SyncRuntimeAction(CliRuntimeSnapshot Snapshot) : CliTuiAction;

internal sealed record ResizeTerminalAction(int Width, int Height) : CliTuiAction;

internal sealed record InsertTextAction(string Text) : CliTuiAction;

internal sealed record BackspaceAction() : CliTuiAction;

internal sealed record DeleteAction() : CliTuiAction;

internal sealed record MoveCursorHorizontalAction(int Delta) : CliTuiAction;

internal sealed record MoveCursorVerticalAction(int Delta) : CliTuiAction;

internal sealed record MoveCursorHomeAction() : CliTuiAction;

internal sealed record MoveCursorEndAction() : CliTuiAction;

internal sealed record ReplaceEditorTextAction(string Text, int? CursorIndex = null) : CliTuiAction;

internal sealed record CommitSubmittedInputAction(string Text) : CliTuiAction;

internal sealed record BrowseHistoryAction(int Delta) : CliTuiAction;

internal sealed record ScrollTranscriptAction(int Delta) : CliTuiAction;

internal sealed record SetNoticeAction(string Notice) : CliTuiAction;

internal sealed record ToggleSessionPickerAction(bool IsOpen) : CliTuiAction;

internal sealed record SetSessionPickerQueryAction(string Query) : CliTuiAction;

internal sealed record ToggleCommandPaletteAction(bool IsOpen) : CliTuiAction;

internal sealed record SetCommandPaletteQueryAction(string Query) : CliTuiAction;

internal sealed record ToggleSearchOverlayAction(bool IsOpen) : CliTuiAction;

internal sealed record SetSearchDraftAction(string Query) : CliTuiAction;

internal sealed record ApplySearchAction(string Query) : CliTuiAction;

internal sealed record RequestFullRedrawAction() : CliTuiAction;

internal sealed record RenderCompletedAction() : CliTuiAction;