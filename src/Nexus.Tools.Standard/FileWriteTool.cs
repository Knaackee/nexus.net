using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class FileWriteTool : ITool
{
    private readonly StandardToolOptions _options;

    public FileWriteTool(StandardToolOptions options)
    {
        _options = options;
    }

    public string Name => "file_write";

    public string Description => "Writes text to a file inside the configured sandbox.";

    public ToolAnnotations? Annotations => new()
    {
        RequiresApproval = true,
        IsReadOnly = false,
        IsIdempotent = false,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var path = ToolJson.GetRequiredString(input, "path");
            var content = ToolJson.GetRequiredString(input, "content");
            var overwrite = ToolJson.GetOptionalBool(input, "overwrite", true);
            var absolutePath = PathSandbox.ResolvePath(_options.BaseDirectory, path);

            if (!overwrite && File.Exists(absolutePath))
                return ToolResult.Failure($"File '{path}' already exists and overwrite is false.");

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            await File.WriteAllTextAsync(absolutePath, content, ct).ConfigureAwait(false);
            return ToolResult.Success(new FileWriteResult(path.Replace('\\', '/'), content.Length));
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }
}