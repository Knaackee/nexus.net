using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Nexus.Protocols.A2A;

/// <summary>Default A2A client using HTTP JSON-RPC.</summary>
public sealed class HttpA2AClient : IA2AClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public HttpA2AClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<AgentCard> DiscoverAsync(Uri agentCardUri, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(agentCardUri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentCard>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty agent card response.");
    }

    public async Task<A2ATask> SendTaskAsync(Uri endpoint, A2ATaskRequest request, CancellationToken ct = default)
    {
        var rpcRequest = new JsonRpcRequest
        {
            Method = "tasks/send",
            Params = JsonSerializer.SerializeToElement(request, JsonOptions),
        };

        var response = await _httpClient.PostAsJsonAsync(endpoint, rpcRequest, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var rpcResponse = await response.Content.ReadFromJsonAsync<JsonRpcResponse>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty RPC response.");

        if (rpcResponse.Error is not null)
            throw new A2AException(rpcResponse.Error.Value.GetProperty("message").GetString() ?? "Unknown A2A error");

        return rpcResponse.Result.Deserialize<A2ATask>(JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize A2A task.");
    }

    public async IAsyncEnumerable<A2ATaskUpdate> StreamTaskAsync(
        Uri endpoint, A2ATaskRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rpcRequest = new JsonRpcRequest
        {
            Method = "tasks/sendSubscribe",
            Params = JsonSerializer.SerializeToElement(request, JsonOptions),
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(rpcRequest, options: JsonOptions),
        };

        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = line["data:".Length..].Trim();
            if (json == "[DONE]") break;

            var update = JsonSerializer.Deserialize<A2ATaskUpdate>(json, JsonOptions);
            if (update is not null)
                yield return update;
        }
    }

    public async Task CancelTaskAsync(Uri endpoint, string taskId, CancellationToken ct = default)
    {
        var rpcRequest = new JsonRpcRequest
        {
            Method = "tasks/cancel",
            Params = JsonSerializer.SerializeToElement(new { id = taskId }, JsonOptions),
        };

        var response = await _httpClient.PostAsJsonAsync(endpoint, rpcRequest, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record JsonRpcRequest
    {
        public string JsonRpc { get; init; } = "2.0";
        public required string Method { get; init; }
        public JsonElement Params { get; init; }
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
    }

    private sealed record JsonRpcResponse
    {
        public string JsonRpc { get; init; } = "2.0";
        public JsonElement Result { get; init; }
        public JsonElement? Error { get; init; }
        public string? Id { get; init; }
    }
}

public sealed class A2AException : Exception
{
    public A2AException(string message) : base(message) { }
    public A2AException(string message, Exception inner) : base(message, inner) { }
}
