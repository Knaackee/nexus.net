namespace Nexus.Cli;

internal static class CliTuiRenderer
{
    public static CliTuiFrame Render(CliTuiState state)
    {
        var width = Math.Max(40, state.Width);
        var height = Math.Max(12, state.Height);
        var lowWidth = width < state.Capabilities.MinimumWidth || height < state.Capabilities.MinimumHeight;

        var sidebarWidth = lowWidth ? 0 : width >= 120 ? 26 : width >= 90 ? 22 : 0;
        var toolsWidth = lowWidth ? 0 : width >= 130 ? 34 : 0;
        var transcriptWidth = Math.Max(20, width - sidebarWidth - toolsWidth - (sidebarWidth > 0 ? 1 : 0) - (toolsWidth > 0 ? 1 : 0));

        var headerLine = FormatWidth(BuildHeader(state, width, lowWidth), width);
        var statusLine = FormatWidth(BuildStatusLine(state, width), width);

        var composerBlock = BuildComposerBlock(state, width);
        var footerLine = FormatWidth(BuildFooterLine(state, width), width);
        var bodyHeight = Math.Max(1, height - 2 - composerBlock.Lines.Count - 1);

        var transcriptLines = BuildTranscriptLines(state, transcriptWidth, bodyHeight);
        var sidebarLines = sidebarWidth > 0 ? BuildSidebarLines(state, sidebarWidth, bodyHeight) : [];
        var toolLines = toolsWidth > 0 ? BuildToolLines(state, toolsWidth, bodyHeight) : [];

        var lines = new List<string>(height)
        {
            headerLine,
            statusLine,
        };

        for (var row = 0; row < bodyHeight; row++)
        {
            var parts = new List<string>();
            if (sidebarWidth > 0)
                parts.Add(GetLine(sidebarLines, row, sidebarWidth));
            if (sidebarWidth > 0)
                parts.Add("|");

            parts.Add(GetLine(transcriptLines, row, transcriptWidth));

            if (toolsWidth > 0)
            {
                parts.Add("|");
                parts.Add(GetLine(toolLines, row, toolsWidth));
            }

            lines.Add(FormatWidth(string.Concat(parts), width));
        }

        foreach (var line in composerBlock.Lines)
            lines.Add(FormatWidth(line, width));

        lines.Add(footerLine);

        while (lines.Count < height)
            lines.Add(new string(' ', width));

        if (state.ShowSessionPicker)
            DrawOverlay(lines, width, height, "Sessions", FilterSessions(state), "Enter select | Esc close");
        else if (state.ShowCommandPalette)
            DrawOverlay(lines, width, height, "Commands", FilterCommands(state), "Enter insert | Esc close");
        else if (state.ShowSearch)
            DrawOverlay(lines, width, height, "Search", BuildSearchOverlay(state), "Enter apply | Esc close");

        return new CliTuiFrame(lines, composerBlock.CursorRow, composerBlock.CursorColumn);
    }

    private static string BuildHeader(CliTuiState state, int width, bool lowWidth)
    {
        var session = state.Sessions.FirstOrDefault(item => item.IsActive);
        var sessionText = session is null ? "no active session" : $"{session.Key} ({session.Model}, {session.State})";
        var mode = lowWidth ? "compact" : "full";
        return $"Nexus CLI TUI [{mode}]  {state.ProviderName}  {TrimMiddle(state.WorkspaceRoot, Math.Max(10, width / 3))}  {sessionText}";
    }

    private static string BuildStatusLine(CliTuiState state, int width)
    {
        if (!string.IsNullOrWhiteSpace(state.Notice))
            return $"Notice: {state.Notice}";

        if (!state.Capabilities.IsTuiViable(state.Width, state.Height))
            return $"Terminal too small for full layout. Minimum {state.Capabilities.MinimumWidth}x{state.Capabilities.MinimumHeight}.";

        var search = string.IsNullOrWhiteSpace(state.ActiveSearchQuery) ? "search off" : $"search: {state.ActiveSearchQuery}";
        return $"Sessions: {state.Sessions.Count}  Tools: {state.ToolActivity.Count}  {search}  Updated: {state.LastUpdated.LocalDateTime:T}";
    }

    private static (IReadOnlyList<string> Lines, int CursorRow, int CursorColumn) BuildComposerBlock(CliTuiState state, int width)
    {
        var contentWidth = Math.Max(10, width - 4);
        var header = new string('-', width);
        var wrapped = WrapText(string.IsNullOrEmpty(state.Editor.Text) ? string.Empty : state.Editor.Text, contentWidth);
        if (wrapped.Count == 0)
            wrapped.Add(string.Empty);

        var cursorWrapped = WrapText(state.Editor.Text[..Math.Clamp(state.Editor.CursorIndex, 0, state.Editor.Text.Length)], contentWidth);
        if (cursorWrapped.Count == 0)
            cursorWrapped.Add(string.Empty);

        var visible = wrapped.TakeLast(3).ToList();
        var visibleCursorLine = Math.Clamp(cursorWrapped.Count - 1, 0, 2);
        var cursorLineOffset = Math.Max(0, cursorWrapped.Count - visible.Count);
        var cursorRow = 2 + visibleCursorLine + 1 + Math.Max(0, state.Height - 2 - visible.Count - 1 - (state.Height - 2 - visible.Count - 1));
        _ = cursorLineOffset;

        var lines = new List<string> { header, "Composer:" };
        foreach (var line in visible)
            lines.Add($"> {line}");

        var cursorColumn = Math.Min(width, 3 + cursorWrapped[^1].Length);
        var baseRow = state.Height - lines.Count;
        return (lines, Math.Max(1, baseRow + 2 + Math.Min(visible.Count - 1, visibleCursorLine)), Math.Max(1, cursorColumn));
    }

    private static List<string> BuildTranscriptLines(CliTuiState state, int width, int height)
    {
        var lines = WrapText(state.CombinedTranscript, width);
        if (!string.IsNullOrWhiteSpace(state.ActiveSearchQuery))
        {
            lines = lines
                .Where(line => line.Contains(state.ActiveSearchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (lines.Count == 0)
            lines.Add("No transcript yet. Type a message or use /new to create a chat.");

        var offset = Math.Min(state.TranscriptScrollOffset, Math.Max(0, lines.Count - height));
        var start = Math.Max(0, lines.Count - height - offset);
        return lines.Skip(start).Take(height).Select(line => FormatWidth(line, width)).ToList();
    }

    private static List<string> BuildSidebarLines(CliTuiState state, int width, int height)
    {
        var lines = new List<string> { FormatWidth("Sessions", width), new string('-', width) };
        foreach (var session in state.Sessions.Take(height - 2))
        {
            var marker = session.IsActive ? '*' : ' ';
            lines.Add(FormatWidth($"{marker} {session.Key} [{session.State}]", width));
            if (lines.Count < height)
                lines.Add(FormatWidth($"  {session.Model} | {session.SkillName} | {session.MessageCount} msgs", width));
            if (lines.Count >= height)
                break;
        }

        while (lines.Count < height)
            lines.Add(string.Empty);
        return lines;
    }

    private static List<string> BuildToolLines(CliTuiState state, int width, int height)
    {
        var lines = new List<string> { FormatWidth("Tools", width), new string('-', width) };
        foreach (var item in GroupToolActivity(state.ToolActivity).Take(height - 2))
            lines.Add(FormatWidth(item, width));

        while (lines.Count < height)
            lines.Add(string.Empty);
        return lines;
    }

    private static List<string> GroupToolActivity(IReadOnlyList<CliToolActivity> toolActivity)
    {
        var grouped = new List<string>();
        foreach (var group in toolActivity.TakeLast(20).GroupBy(item => (item.ToolName, item.Status)))
        {
            var first = group.First();
            var count = group.Count();
            var suffix = count > 1 ? $" x{count}" : string.Empty;
            grouped.Add($"{first.Timestamp.LocalDateTime:HH:mm:ss} {first.ToolName} [{first.Status}] {first.Message}{suffix}");
        }

        return grouped;
    }

    private static string BuildFooterLine(CliTuiState state, int width)
    {
        var shortcuts = "Enter send | Shift+Enter newline | Tab cycle | Ctrl+P sessions | Ctrl+K commands | Ctrl+F search | PgUp/PgDn scroll | Ctrl+E export";
        if (state.Capabilities.ReducedMotion)
            shortcuts += " | reduced motion";
        return TrimRight(shortcuts, width);
    }

    private static string[] FilterSessions(CliTuiState state)
        => state.Sessions
            .Where(session => string.IsNullOrWhiteSpace(state.SessionPickerQuery)
                || session.Key.Contains(state.SessionPickerQuery, StringComparison.OrdinalIgnoreCase)
                || session.Model.Contains(state.SessionPickerQuery, StringComparison.OrdinalIgnoreCase))
            .Select(session => $"{session.Key}  {session.Model}  {session.SkillName}  {session.State}")
            .ToArray();

    private static string[] FilterCommands(CliTuiState state)
        => state.Commands
            .Where(command => string.IsNullOrWhiteSpace(state.CommandPaletteQuery)
                || command.Name.Contains(state.CommandPaletteQuery, StringComparison.OrdinalIgnoreCase)
                || command.Description.Contains(state.CommandPaletteQuery, StringComparison.OrdinalIgnoreCase))
            .Select(command => $"/{command.Name}  {command.Description}")
            .ToArray();

    private static IReadOnlyList<string> BuildSearchOverlay(CliTuiState state)
        =>
        [
            string.IsNullOrWhiteSpace(state.SearchDraft) ? "Type a query to filter the transcript." : $"Query: {state.SearchDraft}",
            string.IsNullOrWhiteSpace(state.ActiveSearchQuery) ? "Active filter: none" : $"Active filter: {state.ActiveSearchQuery}",
        ];

    private static void DrawOverlay(List<string> lines, int width, int height, string title, IReadOnlyList<string> content, string footer)
    {
        var overlayWidth = Math.Min(width - 4, Math.Max(30, width * 2 / 3));
        var overlayHeight = Math.Min(height - 4, Math.Max(6, Math.Min(content.Count + 4, height - 4)));
        var left = Math.Max(0, (width - overlayWidth) / 2);
        var top = Math.Max(1, (height - overlayHeight) / 2);

        var overlayLines = new List<string>
        {
            "+" + new string('-', overlayWidth - 2) + "+",
            "|" + TrimRight($" {title}", overlayWidth - 2).PadRight(overlayWidth - 2) + "|",
        };

        foreach (var line in content.Take(overlayHeight - 4))
            overlayLines.Add("|" + TrimRight($" {line}", overlayWidth - 2).PadRight(overlayWidth - 2) + "|");

        while (overlayLines.Count < overlayHeight - 1)
            overlayLines.Add("|" + new string(' ', overlayWidth - 2) + "|");

        overlayLines.Add("+" + new string('-', overlayWidth - 2) + "+");

        for (var index = 0; index < overlayLines.Count && top + index < lines.Count; index++)
        {
            var target = lines[top + index].ToCharArray();
            var source = overlayLines[index];
            for (var column = 0; column < source.Length && left + column < target.Length; column++)
                target[left + column] = source[column];

            lines[top + index] = new string(target);
        }

        var footerRow = Math.Min(lines.Count - 1, top + overlayHeight - 2);
        var footerText = TrimRight(footer, overlayWidth - 4);
        var footerChars = lines[footerRow].ToCharArray();
        for (var index = 0; index < footerText.Length && left + 2 + index < footerChars.Length; index++)
            footerChars[left + 2 + index] = footerText[index];
        lines[footerRow] = new string(footerChars);
    }

    private static List<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var remaining = rawLine;
            while (remaining.Length > width)
            {
                lines.Add(remaining[..width]);
                remaining = remaining[width..];
            }

            lines.Add(remaining);
        }

        return lines;
    }

    private static string GetLine(List<string> lines, int index, int width)
        => index < lines.Count ? FormatWidth(lines[index], width) : new string(' ', width);

    private static string FormatWidth(string text, int width)
        => TrimRight(text, width).PadRight(width);

    private static string TrimRight(string text, int width)
        => text.Length <= width ? text : text[..Math.Max(0, width)];

    private static string TrimMiddle(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= width)
            return text;

        if (width <= 3)
            return text[..width];

        var prefix = (width - 1) / 2;
        var suffix = width - prefix - 1;
        return text[..prefix] + "~" + text[^suffix..];
    }
}