using System.Text.Json;
using Nexus.Core.Agents;
using McpServerConfig = Nexus.Protocols.Mcp.McpServerConfig;
using McpTransport = Nexus.Protocols.Mcp.McpTransport;
using HttpSseTransport = Nexus.Protocols.Mcp.HttpSseTransport;
using StdioTransport = Nexus.Protocols.Mcp.StdioTransport;

namespace Nexus.Cli;

internal static class CliMcpConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<McpServerConfig> Load(CliWorkspaceOptions workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var merged = new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);
        MergeInto(merged, workspace.UserMcpConfigPath);
        MergeInto(merged, workspace.ProjectMcpConfigPath);
        return merged.Values.ToArray();
    }

    private static void MergeInto(Dictionary<string, McpServerConfig> target, string path)
    {
        if (!File.Exists(path))
            return;

        var parsed = JsonSerializer.Deserialize<CliMcpConfigDocument>(File.ReadAllText(path), SerializerOptions)
            ?? new CliMcpConfigDocument();

        var baseDirectory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        foreach (var entry in parsed.Servers)
        {
            target[entry.Key] = new McpServerConfig
            {
                Name = entry.Key,
                Transport = CreateTransport(entry.Key, entry.Value, baseDirectory),
                AllowedTools = entry.Value.AllowedTools is null
                    ? null
                    : new ToolFilter
                    {
                        Include = entry.Value.AllowedTools.Include ?? [],
                        Exclude = entry.Value.AllowedTools.Exclude ?? [],
                    },
                ConnectionTimeout = TimeSpan.FromSeconds(entry.Value.ConnectionTimeoutSeconds ?? 30),
            };
        }
    }

    private static McpTransport CreateTransport(string serverName, CliMcpServerDefinition definition, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(definition.Command))
        {
            return new StdioTransport(
                definition.Command,
                definition.Args?.ToArray(),
                ResolveOptionalPath(baseDirectory, definition.WorkingDirectory));
        }

        if (!string.IsNullOrWhiteSpace(definition.Endpoint))
        {
            return new HttpSseTransport(new Uri(definition.Endpoint, UriKind.Absolute));
        }

        throw new InvalidOperationException($"MCP server '{serverName}' must define either 'command' or 'endpoint'.");
    }

    private static string? ResolveOptionalPath(string baseDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private sealed record CliMcpConfigDocument
    {
        public Dictionary<string, CliMcpServerDefinition> Servers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record CliMcpServerDefinition
    {
        public string? Command { get; init; }
        public IReadOnlyList<string>? Args { get; init; }
        public string? WorkingDirectory { get; init; }
        public string? Endpoint { get; init; }
        public int? ConnectionTimeoutSeconds { get; init; }
        public CliAllowedToolsDefinition? AllowedTools { get; init; }
    }

    private sealed record CliAllowedToolsDefinition
    {
        public IReadOnlyList<string>? Include { get; init; }
        public IReadOnlyList<string>? Exclude { get; init; }
    }
}