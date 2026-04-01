using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using Nexus.Core.Agents;

namespace Nexus.Protocols.Mcp;

internal sealed class DefaultMcpHostManager : IMcpHostManager
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IReadOnlyList<McpServerConfig> _configuredServers;
    private readonly Dictionary<string, McpConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DefaultMcpHostManager(IOptions<McpOptions> options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _loggerFactory = loggerFactory;
        _configuredServers = options.Value.Servers.ToArray();
    }

    public IReadOnlyList<IMcpConnection> Connections => _connections.Values.Cast<IMcpConnection>().ToArray();

    public async Task<IMcpConnection> ConnectAsync(McpServerConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connections.TryGetValue(config.Name, out var existing))
                return existing;

            var transport = CreateTransport(config);
            var client = await McpClientFactory.CreateAsync(
                transport,
                clientOptions: new McpClientOptions(),
                loggerFactory: _loggerFactory,
                cancellationToken: ct).ConfigureAwait(false);

            var connection = await McpConnection.CreateAsync(config, client, ct).ConfigureAwait(false);
            _connections.Add(config.Name, connection);
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(CancellationToken ct = default)
    {
        var connections = await EnsureConfiguredConnectionsAsync(ct).ConfigureAwait(false);
        return connections.SelectMany(connection => connection.Tools).ToArray();
    }

    public async Task<IReadOnlyList<AIFunction>> DiscoverFunctionsAsync(CancellationToken ct = default)
    {
        var connections = await EnsureConfiguredConnectionsAsync(ct).ConfigureAwait(false);
        return connections
            .SelectMany(connection => connection.Functions)
            .Cast<AIFunction>()
            .ToArray();
    }

    public async Task<IReadOnlyList<McpResourceDescriptor>> DiscoverResourcesAsync(CancellationToken ct = default)
    {
        var connections = await EnsureConfiguredConnectionsAsync(ct).ConfigureAwait(false);
        return connections.SelectMany(connection => connection.Resources).ToArray();
    }

    public async Task<IReadOnlyList<McpPromptDescriptor>> DiscoverPromptsAsync(CancellationToken ct = default)
    {
        var connections = await EnsureConfiguredConnectionsAsync(ct).ConfigureAwait(false);
        return connections.SelectMany(connection => connection.Prompts).ToArray();
    }

    public async Task DisconnectAsync(string serverName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        McpConnection? removed = null;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connections.Remove(serverName, out var connection))
                removed = connection;
        }
        finally
        {
            _gate.Release();
        }

        if (removed is not null)
            await removed.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        List<McpConnection> connections;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            connections = _connections.Values.ToList();
            _connections.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var connection in connections)
            await connection.DisposeAsync().ConfigureAwait(false);

        _gate.Dispose();
    }

    private async Task<IReadOnlyList<McpConnection>> EnsureConfiguredConnectionsAsync(CancellationToken ct)
    {
        foreach (var server in _configuredServers)
            await ConnectAsync(server, ct).ConfigureAwait(false);

        return _connections.Values.ToArray();
    }

    private IClientTransport CreateTransport(McpServerConfig config)
        => config.Transport switch
        {
            StdioTransport stdio => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = config.Name,
                Command = stdio.Command,
                Arguments = stdio.Arguments,
                WorkingDirectory = stdio.WorkingDirectory,
            }, _loggerFactory),
            HttpSseTransport sse => new SseClientTransport(new SseClientTransportOptions
            {
                Name = config.Name,
                Endpoint = sse.Endpoint,
                ConnectionTimeout = config.ConnectionTimeout,
            }, _loggerFactory),
            _ => throw new NotSupportedException($"Unsupported MCP transport '{config.Transport.GetType().Name}'."),
        };

    private static bool IsToolAllowed(ToolFilter? filter, string toolName)
    {
        if (filter is null)
            return true;

        if (filter.Include is { Count: > 0 } include
            && !include.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (filter.Exclude is { Count: > 0 } exclude
            && exclude.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private sealed class McpConnection : IMcpConnection
    {
        private readonly IMcpClient _client;

        private McpConnection(
            string serverName,
            IMcpClient client,
            IReadOnlyList<McpClientTool> functions,
            IReadOnlyList<McpToolDescriptor> tools,
            IReadOnlyList<McpResourceDescriptor> resources,
            IReadOnlyList<McpPromptDescriptor> prompts)
        {
            ServerName = serverName;
            _client = client;
            Functions = functions;
            Tools = tools;
            Resources = resources;
            Prompts = prompts;
        }

        public string ServerName { get; }

        public bool IsConnected => true;

        public IReadOnlyList<McpClientTool> Functions { get; }

        public IReadOnlyList<McpToolDescriptor> Tools { get; }

        public IReadOnlyList<McpResourceDescriptor> Resources { get; }

        public IReadOnlyList<McpPromptDescriptor> Prompts { get; }

        public static async Task<McpConnection> CreateAsync(McpServerConfig config, IMcpClient client, CancellationToken ct)
        {
            var functions = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
            var filteredFunctions = functions
                .Where(function => IsToolAllowed(config.AllowedTools, function.Name))
                .Select(function => function
                    .WithName($"mcp.{config.Name}.{function.Name}")
                    .WithDescription($"[{config.Name}] {function.Description}"))
                .ToArray();

            var resources = await client.ListResourcesAsync(ct).ConfigureAwait(false);
            var prompts = await client.ListPromptsAsync(ct).ConfigureAwait(false);

            return new McpConnection(
                config.Name,
                client,
                filteredFunctions,
                filteredFunctions.Select(function => new McpToolDescriptor
                {
                    Name = function.Name,
                    Description = function.Description,
                    InputSchema = function.JsonSchema,
                    ServerName = config.Name,
                }).ToArray(),
                resources.Select(resource => new McpResourceDescriptor
                {
                    Uri = resource.Uri.ToString(),
                    Name = resource.Name,
                    Description = resource.Description,
                    MimeType = resource.MimeType,
                    ServerName = config.Name,
                }).ToArray(),
                prompts.Select(prompt => new McpPromptDescriptor
                {
                    Name = prompt.Name,
                    Description = prompt.Description,
                    ServerName = config.Name,
                }).ToArray());
        }

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}