using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class GrepTool : ITool
{
    private readonly StandardToolOptions _options;

    public GrepTool(StandardToolOptions options)
    {
        _options = options;
    }

    public string Name => "grep";

    public string Description => "Searches text in files under the configured sandbox using plain text or regex.";

    public ToolAnnotations? Annotations => new()
    {
        IsReadOnly = true,
        IsIdempotent = true,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var pattern = ToolJson.GetRequiredString(input, "pattern");
            var include = ToolJson.GetOptionalString(input, "include") ?? "**/*";
            var isRegex = ToolJson.GetOptionalBool(input, "isRegex");
            var caseSensitive = ToolJson.GetOptionalBool(input, "caseSensitive");
            var maxResults = ToolJson.GetOptionalInt(input, "maxResults") ?? _options.MaxSearchResults;

            var matcher = new Matcher(caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(include);
            foreach (var exclude in ToolJson.GetOptionalStringArray(input, "exclude"))
                matcher.AddExclude(exclude);

            var files = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(_options.BaseDirectory))).Files;
            var regex = isRegex
                ? new Regex(pattern, (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant)
                : null;

            var matches = new List<GrepMatch>();
            foreach (var file in files)
            {
                var absolutePath = Path.Combine(_options.BaseDirectory, file.Path);
                var lines = await File.ReadAllLinesAsync(absolutePath, ct).ConfigureAwait(false);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var hit = regex is not null
                        ? regex.IsMatch(line)
                        : line.Contains(pattern, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

                    if (!hit)
                        continue;

                    matches.Add(new GrepMatch(file.Path.Replace('\\', '/'), i + 1, line));
                    if (matches.Count >= maxResults)
                        return ToolResult.Success(matches.ToArray());
                }
            }

            return ToolResult.Success(matches.ToArray());
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }
}