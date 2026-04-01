using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class FileEditTool : ITool
{
    private readonly StandardToolOptions _options;

    public FileEditTool(StandardToolOptions options)
    {
        _options = options;
    }

    public string Name => "file_edit";

    public string Description => "Applies an exact text replacement within a sandboxed file.";

    public ToolAnnotations? Annotations => new()
    {
        RequiresApproval = true,
        IsReadOnly = false,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var path = ToolJson.GetRequiredString(input, "path");
            var oldText = ToolJson.GetRequiredString(input, "oldText");
            var newText = ToolJson.GetRequiredString(input, "newText");
            var replaceAll = ToolJson.GetOptionalBool(input, "replaceAll");
            var absolutePath = PathSandbox.ResolvePath(_options.BaseDirectory, path);
            if (!File.Exists(absolutePath))
                return ToolResult.Failure($"File '{path}' does not exist.");

            var content = await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false);
            if (!content.Contains(oldText, StringComparison.Ordinal))
                return ToolResult.Failure("oldText was not found in the file.");

            int replacements;
            string updated;
            if (replaceAll)
            {
                replacements = content.Split(oldText, StringSplitOptions.None).Length - 1;
                updated = content.Replace(oldText, newText, StringComparison.Ordinal);
            }
            else
            {
                var index = content.IndexOf(oldText, StringComparison.Ordinal);
                updated = string.Concat(content.AsSpan(0, index), newText, content.AsSpan(index + oldText.Length));
                replacements = 1;
            }

            await File.WriteAllTextAsync(absolutePath, updated, ct).ConfigureAwait(false);
            return ToolResult.Success(new EditResult(path.Replace('\\', '/'), replacements));
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }
}