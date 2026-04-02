using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Nexus.Live.Integration.Tests;

internal sealed class LiveOllamaChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public LiveOllamaChatClient(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public List<IReadOnlyList<ChatMessage>> ReceivedMessages { get; } = [];

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(messages, stream: false, options);
        ReceivedMessages.Add(messages.ToList());

        using var response = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var content = document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, content)]);
        AttachUsage(chatResponse, document.RootElement);
        return chatResponse;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(messages, stream: true, options);
        ReceivedMessages.Add(messages.ToList());

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        JsonElement? finalPayload = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            finalPayload = document.RootElement.Clone();
            var message = document.RootElement.GetProperty("message");
            var text = message.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(text)],
                };
            }
        }

        if (finalPayload.HasValue)
        {
            var finalUpdate = new ChatResponseUpdate();
            AttachUsage(finalUpdate, finalPayload.Value);
            yield return finalUpdate;
        }
    }

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private object CreateRequest(IEnumerable<ChatMessage> messages, bool stream, ChatOptions? options)
        => new
        {
            model = _model,
            stream,
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                content = m.Text ?? string.Empty,
            }).ToList(),
            options = new Dictionary<string, object?>
            {
                ["temperature"] = options?.Temperature ?? 0.1,
                ["num_predict"] = options?.MaxOutputTokens,
            },
        };

    private static void AttachUsage(object target, JsonElement payload)
    {
        var inputTokens = payload.TryGetProperty("prompt_eval_count", out var inputElement) ? inputElement.GetInt32() : 0;
        var outputTokens = payload.TryGetProperty("eval_count", out var outputElement) ? outputElement.GetInt32() : 0;
        var usage = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            TotalTokenCount = inputTokens + outputTokens,
        };

        var additionalPropertiesProperty = target.GetType().GetProperty("AdditionalProperties");
        if (additionalPropertiesProperty is null)
            return;

        var dictionary = additionalPropertiesProperty.GetValue(target);
        if (dictionary is null && additionalPropertiesProperty.CanWrite)
        {
            dictionary = Activator.CreateInstance(additionalPropertiesProperty.PropertyType);
            additionalPropertiesProperty.SetValue(target, dictionary);
        }

        var indexer = dictionary?.GetType().GetProperty("Item");
        indexer?.SetValue(dictionary, usage, ["Usage"]);
    }
}

internal sealed class OllamaLiveTestEnvironment : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    private OllamaLiveTestEnvironment(HttpClient httpClient, string endpoint, string model)
    {
        _httpClient = httpClient;
        _endpoint = endpoint;
        Model = model;
        ChatClient = new LiveOllamaChatClient(_httpClient, model);
    }

    public string Endpoint => _endpoint;
    public string Model { get; }
    public LiveOllamaChatClient ChatClient { get; }

    public static async Task<OllamaLiveTestEnvironment?> CreateAsync(CancellationToken ct = default)
    {
        var endpoint = Environment.GetEnvironmentVariable("NEXUS_OLLAMA_ENDPOINT") ?? "http://127.0.0.1:11434";
        var timeoutSeconds = ReadTimeoutSeconds();
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        try
        {
            using var response = await httpClient.GetAsync("/api/tags", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var configuredModel = Environment.GetEnvironmentVariable("NEXUS_OLLAMA_MODEL");
            var models = document.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()).Where(static name => !string.IsNullOrWhiteSpace(name)).ToList();

            var model = configuredModel is not null
                ? models.FirstOrDefault(m => string.Equals(m, configuredModel, StringComparison.OrdinalIgnoreCase))
                : models.FirstOrDefault();

            if (model is null)
                return null;

            return new OllamaLiveTestEnvironment(httpClient, endpoint, model);
        }
        catch (Exception ex)
        {
            _ = ex;
            httpClient.Dispose();
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private static int ReadTimeoutSeconds()
    {
        var configured = Environment.GetEnvironmentVariable("NEXUS_OLLAMA_TIMEOUT_SECONDS");
        return int.TryParse(configured, out var seconds) && seconds > 0
            ? seconds
            : 300;
    }
}