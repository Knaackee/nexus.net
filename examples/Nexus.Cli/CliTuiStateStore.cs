namespace Nexus.Cli;

internal sealed class CliTuiStateStore
{
    private readonly object _gate = new();
    private CliTuiState _state;

    public CliTuiStateStore(CliTuiState initialState)
    {
        _state = initialState;
    }

    public CliTuiState Snapshot
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public CliTuiState Dispatch(CliTuiAction action)
    {
        lock (_gate)
        {
            _state = Reduce(_state, action);
            return _state;
        }
    }

    private static CliTuiState Reduce(CliTuiState state, CliTuiAction action)
        => action switch
        {
            SyncRuntimeAction sync => state with
            {
                RuntimeOutputText = sync.Snapshot.RuntimeOutputText,
                Sessions = sync.Snapshot.Sessions,
                ToolActivity = sync.Snapshot.ToolActivity,
                Commands = sync.Snapshot.Commands,
                ProviderName = sync.Snapshot.ProviderName,
                WorkspaceRoot = sync.Snapshot.WorkspaceRoot,
                LastUpdated = sync.Snapshot.Timestamp,
            },
            ResizeTerminalAction resize => state with
            {
                Width = resize.Width,
                Height = resize.Height,
                ForceFullRedraw = true,
            },
            InsertTextAction insert => UpdateEditor(state, editor =>
            {
                var text = editor.Text.Insert(editor.CursorIndex, insert.Text);
                return editor with { Text = text, CursorIndex = editor.CursorIndex + insert.Text.Length, HistoryOffset = -1 };
            }),
            BackspaceAction => UpdateEditor(state, editor =>
            {
                if (editor.CursorIndex == 0)
                    return editor;

                return editor with
                {
                    Text = editor.Text.Remove(editor.CursorIndex - 1, 1),
                    CursorIndex = editor.CursorIndex - 1,
                    HistoryOffset = -1,
                };
            }),
            DeleteAction => UpdateEditor(state, editor =>
            {
                if (editor.CursorIndex >= editor.Text.Length)
                    return editor;

                return editor with
                {
                    Text = editor.Text.Remove(editor.CursorIndex, 1),
                    HistoryOffset = -1,
                };
            }),
            MoveCursorHorizontalAction horizontal => UpdateEditor(state, editor =>
                editor with { CursorIndex = Math.Clamp(editor.CursorIndex + horizontal.Delta, 0, editor.Text.Length) }),
            MoveCursorVerticalAction vertical => UpdateEditor(state, editor => MoveCursorVertical(editor, vertical.Delta)),
            MoveCursorHomeAction => UpdateEditor(state, editor => editor with { CursorIndex = GetLineStart(editor.Text, editor.CursorIndex) }),
            MoveCursorEndAction => UpdateEditor(state, editor => editor with { CursorIndex = GetLineEnd(editor.Text, editor.CursorIndex) }),
            ReplaceEditorTextAction replace => state with
            {
                Editor = state.Editor with
                {
                    Text = replace.Text,
                    CursorIndex = Math.Clamp(replace.CursorIndex ?? replace.Text.Length, 0, replace.Text.Length),
                    HistoryOffset = -1,
                }
            },
            CommitSubmittedInputAction commit => state with
            {
                TranscriptLogText = AppendTranscript(state.TranscriptLogText, $"> {commit.Text}"),
                Editor = state.Editor with
                {
                    Text = string.Empty,
                    CursorIndex = 0,
                    History = AddToHistory(state.Editor.History, commit.Text),
                    HistoryOffset = -1,
                },
                Notice = string.Empty,
            },
            BrowseHistoryAction browse => BrowseHistory(state, browse.Delta),
            ScrollTranscriptAction scroll => state with { TranscriptScrollOffset = Math.Max(0, state.TranscriptScrollOffset + scroll.Delta) },
            SetNoticeAction notice => state with { Notice = notice.Notice },
            ToggleSessionPickerAction picker => state with
            {
                ShowSessionPicker = picker.IsOpen,
                SessionPickerQuery = picker.IsOpen ? state.SessionPickerQuery : string.Empty,
                ShowCommandPalette = picker.IsOpen ? false : state.ShowCommandPalette,
                ShowSearch = picker.IsOpen ? false : state.ShowSearch,
            },
            SetSessionPickerQueryAction query => state with { SessionPickerQuery = query.Query },
            ToggleCommandPaletteAction palette => state with
            {
                ShowCommandPalette = palette.IsOpen,
                CommandPaletteQuery = palette.IsOpen ? state.CommandPaletteQuery : string.Empty,
                ShowSessionPicker = palette.IsOpen ? false : state.ShowSessionPicker,
                ShowSearch = palette.IsOpen ? false : state.ShowSearch,
            },
            SetCommandPaletteQueryAction query => state with { CommandPaletteQuery = query.Query },
            ToggleSearchOverlayAction search => state with
            {
                ShowSearch = search.IsOpen,
                SearchDraft = search.IsOpen ? state.ActiveSearchQuery : string.Empty,
                ShowSessionPicker = search.IsOpen ? false : state.ShowSessionPicker,
                ShowCommandPalette = search.IsOpen ? false : state.ShowCommandPalette,
            },
            SetSearchDraftAction searchDraft => state with { SearchDraft = searchDraft.Query },
            ApplySearchAction search => state with
            {
                ActiveSearchQuery = search.Query,
                SearchDraft = search.Query,
                ShowSearch = false,
                TranscriptScrollOffset = 0,
            },
            RequestFullRedrawAction => state with { ForceFullRedraw = true },
            RenderCompletedAction => state with { ForceFullRedraw = false },
            _ => state,
        };

    private static CliTuiState UpdateEditor(CliTuiState state, Func<CliTuiEditorState, CliTuiEditorState> update)
        => state with { Editor = update(state.Editor) };

    private static CliTuiState BrowseHistory(CliTuiState state, int delta)
    {
        if (state.Editor.History.Count == 0)
            return state;

        var nextOffset = state.Editor.HistoryOffset < 0
            ? delta > 0 ? state.Editor.History.Count - 1 : -1
            : Math.Clamp(state.Editor.HistoryOffset - delta, -1, state.Editor.History.Count - 1);

        if (nextOffset < 0)
        {
            return state with
            {
                Editor = state.Editor with { Text = string.Empty, CursorIndex = 0, HistoryOffset = -1 }
            };
        }

        var entry = state.Editor.History[nextOffset];
        return state with
        {
            Editor = state.Editor with
            {
                Text = entry,
                CursorIndex = entry.Length,
                HistoryOffset = nextOffset,
            }
        };
    }

    private static string AppendTranscript(string transcript, string line)
        => string.IsNullOrWhiteSpace(transcript) ? line : transcript + Environment.NewLine + line;

    private static IReadOnlyList<string> AddToHistory(IReadOnlyList<string> history, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return history;

        var items = history.ToList();
        if (items.Count == 0 || !string.Equals(items[^1], text, StringComparison.Ordinal))
            items.Add(text);
        if (items.Count > 100)
            items.RemoveRange(0, items.Count - 100);
        return items;
    }

    private static CliTuiEditorState MoveCursorVertical(CliTuiEditorState editor, int delta)
    {
        var currentLineStart = GetLineStart(editor.Text, editor.CursorIndex);
        var currentColumn = editor.CursorIndex - currentLineStart;
        var targetAnchor = delta < 0 ? currentLineStart - 1 : GetLineEnd(editor.Text, editor.CursorIndex) + 1;
        if (targetAnchor < 0 || targetAnchor > editor.Text.Length)
            return editor;

        if (delta < 0)
        {
            var targetStart = GetLineStart(editor.Text, targetAnchor);
            var targetEnd = GetLineEnd(editor.Text, targetStart);
            return editor with { CursorIndex = Math.Min(targetStart + currentColumn, targetEnd) };
        }

        if (targetAnchor >= editor.Text.Length)
            return editor;

        var nextStart = GetLineStart(editor.Text, targetAnchor);
        var nextEnd = GetLineEnd(editor.Text, nextStart);
        return editor with { CursorIndex = Math.Min(nextStart + currentColumn, nextEnd) };
    }

    private static int GetLineStart(string text, int cursorIndex)
    {
        var index = Math.Clamp(cursorIndex, 0, text.Length);
        while (index > 0 && text[index - 1] != '\n')
            index--;
        return index;
    }

    private static int GetLineEnd(string text, int cursorIndex)
    {
        var index = Math.Clamp(cursorIndex, 0, text.Length);
        while (index < text.Length && text[index] != '\n')
            index++;
        return index;
    }
}