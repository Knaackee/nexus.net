using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;
using Nexus.Core.Agents;
using Nexus.Core.Configuration;
using Nexus.Core.Pipeline;

namespace Nexus.Skills;

public enum SkillSource
{
    Inline,
    Project,
    User,
    Plugin,
    Custom,
}

public sealed record SkillDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? SystemPrompt { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    public string? ModelId { get; init; }
    public string? WhenToUse { get; init; }
    public SkillSource Source { get; init; } = SkillSource.Inline;
    public string? SourcePath { get; init; }
}

public interface ISkillCatalog
{
    SkillDefinition? Resolve(string name);
    IReadOnlyList<SkillDefinition> ListAll();
    IReadOnlyList<SkillDefinition> FindRelevant(string userMessage, IReadOnlyList<ChatMessage>? messages = null, int maxResults = 3);
}

public sealed class SkillCatalog : ISkillCatalog
{
    private readonly Dictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);

    public void Register(SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        _skills[skill.Name] = skill;
    }

    public SkillDefinition? Resolve(string name)
        => _skills.GetValueOrDefault(name);

    public IReadOnlyList<SkillDefinition> ListAll()
        => _skills.Values
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<SkillDefinition> FindRelevant(string userMessage, IReadOnlyList<ChatMessage>? messages = null, int maxResults = 3)
    {
        if (_skills.Count == 0 || maxResults <= 0)
            return [];

        var query = SkillRelevanceMatcher.BuildQuery(userMessage, messages);
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return _skills.Values
            .Select(skill => new { Skill = skill, Score = SkillRelevanceMatcher.Score(skill, query) })
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Skill.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(candidate => candidate.Skill)
            .ToArray();
    }
}

public static class SkillDefinitionExtensions
{
    public static AgentDefinition ApplyTo(this SkillDefinition skill, AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(agent);

        return agent with
        {
            SystemPrompt = CombinePrompts(agent.SystemPrompt, skill.SystemPrompt),
            ModelId = string.IsNullOrWhiteSpace(skill.ModelId) ? agent.ModelId : skill.ModelId,
            ToolNames = agent.ToolNames
                .Concat(skill.ToolNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public static AgentDefinition BuildAgentDefinition(
        this SkillDefinition skill,
        string agentName,
        AgentDefinition? baseline = null)
    {
        var seed = baseline ?? new AgentDefinition { Name = agentName };
        return skill.ApplyTo(seed with { Name = agentName });
    }

    private static string? CombinePrompts(string? basePrompt, string? skillPrompt)
    {
        if (string.IsNullOrWhiteSpace(basePrompt))
            return skillPrompt;

        if (string.IsNullOrWhiteSpace(skillPrompt))
            return basePrompt;

        return $"{basePrompt}{Environment.NewLine}{Environment.NewLine}{skillPrompt}";
    }
}

public sealed class SkillOptions
{
    internal IList<SkillDefinition> Skills { get; } = [];
    internal IList<SkillDirectoryRegistration> Directories { get; } = [];
}

public sealed class SkillInjectionOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxSkills { get; set; } = 3;
}

public sealed record SkillDirectoryRegistration(string Path, SkillSource Source, bool Optional = true);

public interface ISkillLoader
{
    IReadOnlyList<SkillDefinition> LoadFromDirectory(string path, SkillSource source = SkillSource.Custom, bool optional = true);
}

public static class SkillServiceCollectionExtensions
{
    public static SkillBuilder UseDefaults(this SkillBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = GetOrCreateOptions(builder.Services);
        GetOrCreateInjectionOptions(builder.Services);
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    public static SkillBuilder ConfigureInjection(this SkillBuilder builder, Action<SkillInjectionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = GetOrCreateInjectionOptions(builder.Services);
        configure(options);
        EnsureRegistered(builder.Services, GetOrCreateOptions(builder.Services));
        return builder;
    }

    public static SkillBuilder AddSkill(this SkillBuilder builder, SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(skill);

        var options = GetOrCreateOptions(builder.Services);
        options.Skills.Add(skill);
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    public static SkillBuilder AddSkill(
        this SkillBuilder builder,
        string name,
        string? description = null,
        string? systemPrompt = null,
        params string[] toolNames)
        => builder.AddSkill(new SkillDefinition
        {
            Name = name,
            Description = description,
            SystemPrompt = systemPrompt,
            ToolNames = toolNames,
        });

    public static SkillBuilder AddDirectory(
        this SkillBuilder builder,
        string path,
        SkillSource source = SkillSource.Custom,
        bool optional = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var options = GetOrCreateOptions(builder.Services);
        options.Directories.Add(new SkillDirectoryRegistration(path, source, optional));
        EnsureRegistered(builder.Services, options);
        return builder;
    }

    private static SkillOptions GetOrCreateOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(SkillOptions))?.ImplementationInstance as SkillOptions;
        if (existing is not null)
            return existing;

        var created = new SkillOptions();
        services.AddSingleton(created);
        return created;
    }

    private static SkillInjectionOptions GetOrCreateInjectionOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(service => service.ServiceType == typeof(SkillInjectionOptions))?.ImplementationInstance as SkillInjectionOptions;
        if (existing is not null)
            return existing;

        var created = new SkillInjectionOptions();
        services.AddSingleton(created);
        return created;
    }

    private static void EnsureRegistered(IServiceCollection services, SkillOptions options)
    {
        services.TryAddSingleton<ISkillLoader, MarkdownSkillLoader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMiddleware, SkillInjectionMiddleware>());

        services.TryAddSingleton<ISkillCatalog>(sp =>
        {
            var catalog = new SkillCatalog();
            var loader = sp.GetRequiredService<ISkillLoader>();

            foreach (var directory in options.Directories)
            {
                foreach (var skill in loader.LoadFromDirectory(directory.Path, directory.Source, directory.Optional))
                    catalog.Register(skill);
            }

            foreach (var skill in options.Skills)
                catalog.Register(skill);

            return catalog;
        });
    }
}

internal static class SkillRelevanceMatcher
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "code", "for", "from", "how", "i", "if",
        "in", "into", "is", "it", "of", "on", "or", "review", "task", "that", "the", "this", "to",
        "use", "user", "when", "with", "write", "you", "your"
    };

    public static string BuildQuery(string userMessage, IReadOnlyList<ChatMessage>? messages)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(userMessage))
            segments.Add(userMessage);

        if (messages is not null)
        {
            foreach (var message in messages.Where(static message => message.Role == ChatRole.User).TakeLast(3))
            {
                if (!string.IsNullOrWhiteSpace(message.Text))
                    segments.Add(message.Text!);
            }
        }

        return Normalize(string.Join(Environment.NewLine, segments));
    }

    public static int Score(SkillDefinition skill, string query)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return 0;

        var score = 0;
        var skillName = Normalize(skill.Name.Trim());
        if (!string.IsNullOrWhiteSpace(skillName) && query.Contains(skillName, StringComparison.OrdinalIgnoreCase))
            score += 10;

        score += ScoreText(skill.Description, queryTokens, 2);
        score += ScoreText(skill.WhenToUse, queryTokens, 3);
        score += ScoreText(skill.SystemPrompt, queryTokens, 1);
        score += ScoreText(skill.Name, queryTokens, 4);
        return score;
    }

    private static int ScoreText(string? text, HashSet<string> queryTokens, int weight)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var textTokens = Tokenize(text);
        if (textTokens.Count == 0)
            return 0;

        return textTokens.Count(queryTokens.Contains) * weight;
    }

    private static HashSet<string> Tokenize(string text)
    {
        text = Normalize(text);
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new List<char>(text.Length);

        static void Flush(List<char> chars, HashSet<string> target)
        {
            if (chars.Count == 0)
                return;

            var token = new string(chars.ToArray()).Trim().ToLowerInvariant();
            chars.Clear();
            if (token.Length < 3 || StopWords.Contains(token))
                return;

            target.Add(token);
        }

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                buffer.Add(ch);
            }
            else
            {
                Flush(buffer, tokens);
            }
        }

        Flush(buffer, tokens);
        return tokens;
    }

    private static string Normalize(string text)
        => text
            .Replace("C#", "csharp", StringComparison.OrdinalIgnoreCase)
            .Replace("F#", "fsharp", StringComparison.OrdinalIgnoreCase)
            .Replace(".NET", "dotnet", StringComparison.OrdinalIgnoreCase);
}