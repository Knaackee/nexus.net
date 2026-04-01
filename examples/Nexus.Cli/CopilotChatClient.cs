using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Nexus.Cli;

/// <summary>
/// IChatClient backed by the GitHub Copilot chat/completions API.
/// Supports streaming via SSE. Refreshes Copilot token automatically.
/// </summary>
internal sealed class CopilotChatClient : IChatClient
{
    private readonly string _model;
    private readonly HttpClient _http;
    private CopilotToken? _token;

    public CopilotChatClient(string model)
    {
        _model = model;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusCli/0.1.0");
        _http.DefaultRequestHeaders.Add("Editor-Version", "Nexus/0.1.0");
        _http.DefaultRequestHeaders.Add("Editor-Plugin-Version", "nexus-cli/0.1.0");
        _http.DefaultRequestHeaders.Add("Copilot-Integration-Id", "vscode-chat");
    }

    public void Dispose() => _http.Dispose();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private async Task<string> GetEndpointAsync(CancellationToken ct)
    {
        if (_token is null || _token.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
            _token = await CopilotAuth.GetTokenAsync(ct).ConfigureAwait(false);

        var baseUri = _token.Endpoints?.Api ?? "https://api.githubcopilot.com";
        return $"{baseUri.TrimEnd('/')}/chat/completions";
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.Token);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var endpoint = await GetEndpointAsync(ct).ConfigureAwait(false);
        var body = BuildRequestBody(messages, options, stream: false);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuth(request);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = JsonDocument.Parse(json);
        var content = parsed.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        var reply = new ChatMessage(ChatRole.Assistant, content);
        return new ChatResponse([reply]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var endpoint = await GetEndpointAsync(ct).ConfigureAwait(false);
        var body = BuildRequestBody(messages, options, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuth(request);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            ChatResponseUpdate? update = null;
            try
            {
                var chunk = JsonDocument.Parse(data);
                var choices = chunk.RootElement.GetProperty("choices");

                if (choices.GetArrayLength() == 0)
                    continue;

                var delta = choices[0].GetProperty("delta");

                if (delta.TryGetProperty("content", out var contentProp))
                {
                    var text = contentProp.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        update = new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(text)],
                        };
                    }
                }
            }
            catch (Exception) when (data is not null)
            {
                // Skip malformed or unexpected chunks
            }

            if (update is not null)
                yield return update;
        }
    }

    private string BuildRequestBody(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var msgs = messages.Select(m => new ApiMessage
        {
            Role = m.Role == ChatRole.System ? "system" : m.Role == ChatRole.Assistant ? "assistant" : "user",
            Content = m.Text ?? "",
        }).ToList();

        var payload = new ApiRequest
        {
            Model = _model,
            Messages = msgs,
            Stream = stream,
            MaxTokens = options?.MaxOutputTokens ?? 4096,
            Temperature = (float?)(options?.Temperature) ?? 0.1f,
        };

        return JsonSerializer.Serialize(payload, ApiJsonContext.Default.ApiRequest);
    }

    internal sealed class ApiMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    internal sealed class ApiRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required List<ApiMessage> Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; } = 4096;

        [JsonPropertyName("temperature")]
        public float Temperature { get; init; } = 0.1f;
    }
}

[JsonSerializable(typeof(CopilotChatClient.ApiRequest))]
[JsonSerializable(typeof(CopilotChatClient.ApiMessage))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
