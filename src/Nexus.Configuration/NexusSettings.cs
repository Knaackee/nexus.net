using System.Text.Json.Serialization;

namespace Nexus.Configuration;

public sealed record NexusSettings
{
    public PermissionSettings Permissions { get; set; } = new();

    public ModelSettings Models { get; set; } = new();

    public BudgetSettings Budget { get; set; } = new();

    public ToolSettings Tools { get; set; } = new();

    public MemorySettings Memory { get; set; } = new();

    public static NexusSettings Default { get; } = CreateDefault();

    public static NexusSettings CreateDefault()
        => new()
        {
            Permissions = new PermissionSettings { Mode = "default" },
            Models = new ModelSettings(),
            Budget = new BudgetSettings { MaxTurns = 50 },
            Tools = new ToolSettings { MaxConcurrency = 4 },
            Memory = new MemorySettings { Directory = ".nexus/memory", MaxIndexLines = 200 },
        };
}

public sealed record PermissionSettings
{
    public string? Mode { get; init; }

    public IReadOnlyList<PermissionRuleSettings> Rules { get; init; } = [];
}

public sealed record PermissionRuleSettings
{
    public string? Tool { get; init; }

    public string? Action { get; init; }
}

public sealed record ModelSettings
{
    [JsonPropertyName("default")]
    public string? Default { get; init; }

    public string? Compaction { get; init; }
}

public sealed record BudgetSettings
{
    public decimal? MaxCostUsd { get; init; }

    public int? MaxTurns { get; init; }
}

public sealed record ToolSettings
{
    public int? MaxConcurrency { get; init; }

    public IReadOnlyList<string> CompactableTools { get; init; } = [];
}

public sealed record MemorySettings
{
    public string? Directory { get; init; }

    public int? MaxIndexLines { get; init; }
}