namespace Nexus.Cli;

internal sealed record CliTuiFrame(IReadOnlyList<string> Lines, int CursorRow, int CursorColumn);

internal sealed record CliFrameLineUpdate(int Row, string Text);

internal static class CliFrameDiff
{
    public static IReadOnlyList<CliFrameLineUpdate> Compute(CliTuiFrame? previous, CliTuiFrame current)
    {
        if (previous is null)
        {
            return current.Lines
                .Select((line, index) => new CliFrameLineUpdate(index, line))
                .ToArray();
        }

        var maxLines = Math.Max(previous.Lines.Count, current.Lines.Count);
        var updates = new List<CliFrameLineUpdate>();
        for (var index = 0; index < maxLines; index++)
        {
            var oldLine = index < previous.Lines.Count ? previous.Lines[index] : string.Empty;
            var newLine = index < current.Lines.Count ? current.Lines[index] : string.Empty;
            if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
                updates.Add(new CliFrameLineUpdate(index, newLine));
        }

        return updates;
    }
}