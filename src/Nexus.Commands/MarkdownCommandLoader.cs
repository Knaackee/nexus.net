using System.Text;

namespace Nexus.Commands;

public sealed class MarkdownCommandLoader : ICommandLoader
{
    public IReadOnlyList<ICommand> LoadFromDirectory(string path, CommandSource source = CommandSource.Custom, bool optional = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            if (optional)
                return [];

            throw new DirectoryNotFoundException($"Command directory '{fullPath}' does not exist.");
        }

        return Directory.EnumerateFiles(fullPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => ParseFile(file, source))
            .Cast<ICommand>()
            .ToArray();
    }

    private static FileCommand ParseFile(string filePath, CommandSource source)
    {
        var text = File.ReadAllText(filePath);
        var document = FrontMatterDocument.Parse(text);
        var name = document.GetScalar("name") ?? Path.GetFileNameWithoutExtension(filePath);
        var description = document.GetScalar("description") ?? $"Loaded from {Path.GetFileName(filePath)}";
        var usage = document.GetScalar("usage") ?? $"/{name}";
        var aliases = document.GetList("aliases");
        var typeText = document.GetScalar("type");
        var type = string.Equals(typeText, "prompt", StringComparison.OrdinalIgnoreCase)
            ? CommandType.Prompt
            : CommandType.Action;

        return new FileCommand(
            name,
            description,
            usage,
            aliases,
            type,
            source,
            document.Body,
            filePath);
    }

    private sealed class FileCommand : ICommand
    {
        private readonly string _template;

        public FileCommand(
            string name,
            string description,
            string usage,
            IReadOnlyList<string> aliases,
            CommandType type,
            CommandSource source,
            string template,
            string sourcePath)
        {
            Name = name;
            Description = description;
            Usage = usage;
            Aliases = aliases;
            Type = type;
            Source = source;
            SourcePath = sourcePath;
            _template = template;
        }

        public string Name { get; }
        public string Description { get; }
        public string Usage { get; }
        public IReadOnlyList<string> Aliases { get; }
        public CommandType Type { get; }
        public CommandSource Source { get; }
        public string SourcePath { get; }

        public Task<CommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken ct = default)
        {
            var rendered = RenderTemplate(_template, invocation);
            var result = Type == CommandType.Prompt
                ? CommandResult.Continue(promptToSend: rendered)
                : CommandResult.Continue(output: rendered);

            return Task.FromResult(result);
        }

        private static string RenderTemplate(string template, CommandInvocation invocation)
        {
            var rendered = template
                .Replace("{{name}}", invocation.Name, StringComparison.OrdinalIgnoreCase)
                .Replace("{{args}}", invocation.ArgumentText, StringComparison.OrdinalIgnoreCase)
                .Replace("{{raw}}", invocation.RawInput, StringComparison.OrdinalIgnoreCase);

            for (var index = 0; index < invocation.Arguments.Count; index++)
            {
                rendered = rendered.Replace($"{{{{arg{index}}}}}", invocation.Arguments[index], StringComparison.OrdinalIgnoreCase);
            }

            return rendered.Trim();
        }
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