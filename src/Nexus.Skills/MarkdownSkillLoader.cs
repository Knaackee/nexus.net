using System.Text;

namespace Nexus.Skills;

public sealed class MarkdownSkillLoader : ISkillLoader
{
    public IReadOnlyList<SkillDefinition> LoadFromDirectory(string path, SkillSource source = SkillSource.Custom, bool optional = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            if (optional)
                return [];

            throw new DirectoryNotFoundException($"Skill directory '{fullPath}' does not exist.");
        }

        return Directory.EnumerateFiles(fullPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => ParseFile(file, source))
            .ToArray();
    }

    private static SkillDefinition ParseFile(string filePath, SkillSource source)
    {
        var text = File.ReadAllText(filePath);
        var document = FrontMatterDocument.Parse(text);

        return new SkillDefinition
        {
            Name = document.GetScalar("name") ?? Path.GetFileNameWithoutExtension(filePath),
            Description = document.GetScalar("description"),
            SystemPrompt = document.Body,
            ToolNames = document.GetList("tools").Count > 0
                ? document.GetList("tools")
                : document.GetList("allowedTools"),
            ModelId = document.GetScalar("model") ?? document.GetScalar("modelId") ?? document.GetScalar("modelOverride"),
            WhenToUse = document.GetScalar("whenToUse"),
            Source = source,
            SourcePath = filePath,
        };
    }

    private sealed class FrontMatterDocument
    {
        private readonly Dictionary<string, string> _scalars;
        private readonly Dictionary<string, List<string>> _lists;

        private FrontMatterDocument(Dictionary<string, string> scalars, Dictionary<string, List<string>> lists, string body)
        {
            _scalars = scalars;
            _lists = lists;
            Body = body;
        }

        public string Body { get; }

        public string? GetScalar(string key)
            => _scalars.GetValueOrDefault(key);

        public List<string> GetList(string key)
            => _lists.TryGetValue(key, out var values) ? values : [];

        public static FrontMatterDocument Parse(string text)
        {
            if (!text.StartsWith("---", StringComparison.Ordinal))
                return new FrontMatterDocument(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), text.Trim());

            using var reader = new StringReader(text);
            _ = reader.ReadLine();

            var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string? currentListKey = null;

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Trim() == "---")
                    break;

                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) && currentListKey is not null)
                {
                    lists[currentListKey].Add(Unquote(trimmed[2..].Trim()));
                    continue;
                }

                currentListKey = null;
                var separator = trimmed.IndexOf(':');
                if (separator <= 0)
                    continue;

                var key = trimmed[..separator].Trim();
                var value = trimmed[(separator + 1)..].Trim();
                if (value.Length == 0)
                {
                    currentListKey = key;
                    lists[key] = [];
                    continue;
                }

                if (value.StartsWith('[') && value.EndsWith(']'))
                {
                    lists[key] = value[1..^1]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(Unquote)
                        .ToList();
                    continue;
                }

                scalars[key] = Unquote(value);
            }

            var bodyBuilder = new StringBuilder();
            while ((line = reader.ReadLine()) is not null)
            {
                bodyBuilder.AppendLine(line);
            }

            return new FrontMatterDocument(scalars, lists, bodyBuilder.ToString().Trim());
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2)
            {
                if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
                    return value[1..^1];
            }

            return value;
        }
    }
}