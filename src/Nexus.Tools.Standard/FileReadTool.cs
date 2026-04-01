using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class FileReadTool : ITool
{
    private readonly StandardToolOptions _options;

    public FileReadTool(StandardToolOptions options)
    {
        _options = options;
    }

    public string Name => "file_read";

    public string Description => "Reads a text file from the configured sandbox, optionally by line range.";

    public ToolAnnotations? Annotations => new()
    {
        IsReadOnly = true,
        IsIdempotent = true,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var path = ToolJson.GetRequiredString(input, "path");
            var absolutePath = PathSandbox.ResolvePath(_options.BaseDirectory, path);
            if (!File.Exists(absolutePath))
                return ToolResult.Failure($"File '{path}' does not exist.");

            var startLine = Math.Max(1, ToolJson.GetOptionalInt(input, "startLine") ?? 1);
            var endLine = ToolJson.GetOptionalInt(input, "endLine");

            var lines = await File.ReadAllLinesAsync(absolutePath, ct).ConfigureAwait(false);
            var effectiveEndLine = Math.Min(lines.Length, endLine ?? Math.Min(lines.Length, startLine + _options.MaxReadLines - 1));
            if (effectiveEndLine < startLine)
                return ToolResult.Failure("endLine must be greater than or equal to startLine.");

            var content = string.Join(Environment.NewLine, lines.Skip(startLine - 1).Take(effectiveEndLine - startLine + 1));
            return ToolResult.Success(content);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }
}