using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class GlobTool : ITool
{
    private readonly StandardToolOptions _options;

    public GlobTool(StandardToolOptions options)
    {
        _options = options;
    }

    public string Name => "glob";

    public string Description => "Finds files by glob pattern inside the configured sandbox.";

    public ToolAnnotations? Annotations => new()
    {
        IsReadOnly = true,
        IsIdempotent = true,
    };

    public Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var pattern = ToolJson.GetRequiredString(input, "pattern");
            var excludes = ToolJson.GetOptionalStringArray(input, "exclude");
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);
            foreach (var exclude in excludes)
                matcher.AddExclude(exclude);

            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(_options.BaseDirectory)));
            var matches = result.Files.Select(match => match.Path.Replace('\\', '/')).ToArray();
            return Task.FromResult(ToolResult.Success(matches));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure(ex.Message));
        }
    }
}