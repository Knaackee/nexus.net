using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Nexus.Cli;

internal interface ICliChatProvider : IDisposable
{
	string ProviderName { get; }
	bool RequiresAuthentication { get; }
	string DefaultModel { get; }
	IReadOnlyList<string> AvailableModels { get; }
	Task InitializeAsync(CancellationToken ct = default);
	Task AuthenticateAsync(CancellationToken ct = default);
	bool SupportsModel(string model);
	IChatClient CreateClient(string model);
	void Logout();
}

internal static class CliChatProviders
{
	public static ICliChatProvider CreateFromEnvironment()
	{
		var provider = Environment.GetEnvironmentVariable("NEXUS_CLI_PROVIDER");
		return string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase)
			? OllamaCliChatProvider.FromEnvironment()
			: new CopilotCliChatProvider();
	}
}

internal sealed class CopilotCliChatProvider : ICliChatProvider
{
	private readonly HttpClient _httpClient;
	private readonly string? _configuredModel;
	private readonly List<string> _availableModels = [];

	public CopilotCliChatProvider(HttpClient? httpClient = null, string? configuredModel = null)
	{
		_httpClient = httpClient ?? CreateHttpClient();
		_configuredModel = string.IsNullOrWhiteSpace(configuredModel)
			? Environment.GetEnvironmentVariable("NEXUS_CLI_COPILOT_MODEL")
			: configuredModel;
		DefaultModel = _configuredModel ?? string.Empty;
	}

	public string ProviderName => "GitHub Copilot";
	public bool RequiresAuthentication => true;
	public string DefaultModel { get; private set; }
	public IReadOnlyList<string> AvailableModels => _availableModels;

	public async Task InitializeAsync(CancellationToken ct = default)
	{
		var token = await CopilotAuth.GetTokenAsync(ct).ConfigureAwait(false);
		var baseUri = token.Endpoints?.Api ?? "https://api.githubcopilot.com";
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUri.TrimEnd('/')}/models");
		ApplyHeaders(request, token.Token);

		using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		var responseBody = await JsonSerializer.DeserializeAsync(stream, CopilotCliModelJsonContext.Default.CopilotModelListResponse, ct).ConfigureAwait(false)
			?? new CopilotModelListResponse();
		var models = responseBody.Data;

		_availableModels.Clear();
		_availableModels.AddRange(models
			.Where(model => model.ModelPickerEnabled
				&& string.Equals(model.Policy?.State, "enabled", StringComparison.OrdinalIgnoreCase)
				&& (model.SupportedEndpoints?.Contains("/chat/completions", StringComparer.OrdinalIgnoreCase) ?? false))
			.Select(model => model.Id)
			.Distinct(StringComparer.OrdinalIgnoreCase));

		if (_availableModels.Count == 0)
			throw new InvalidOperationException("GitHub Copilot did not return any selectable chat models.");

		DefaultModel = !string.IsNullOrWhiteSpace(_configuredModel)
			? _availableModels.FirstOrDefault(model => string.Equals(model, _configuredModel, StringComparison.OrdinalIgnoreCase)) ?? _configuredModel
			: _availableModels[0];
	}

	public async Task AuthenticateAsync(CancellationToken ct = default)
		=> await CopilotAuth.GetTokenAsync(ct).ConfigureAwait(false);

	public bool SupportsModel(string model)
		=> _availableModels.Count == 0 || AvailableModels.Contains(model, StringComparer.OrdinalIgnoreCase);

	public IChatClient CreateClient(string model) => new CopilotChatClient(model);

	public void Logout() => CopilotAuth.Logout();

	public void Dispose() => _httpClient.Dispose();

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient();
		client.DefaultRequestHeaders.UserAgent.ParseAdd("NexusCli/0.1.0");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		client.DefaultRequestHeaders.Add("Editor-Version", "Nexus/0.1.0");
		client.DefaultRequestHeaders.Add("Editor-Plugin-Version", "nexus-cli/0.1.0");
		client.DefaultRequestHeaders.Add("Copilot-Integration-Id", "vscode-chat");
		return client;
	}

	private static void ApplyHeaders(HttpRequestMessage request, string token)
	{
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		request.Headers.UserAgent.ParseAdd("NexusCli/0.1.0");
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("Editor-Version", "Nexus/0.1.0");
		request.Headers.Add("Editor-Plugin-Version", "nexus-cli/0.1.0");
		request.Headers.Add("Copilot-Integration-Id", "vscode-chat");
	}
}

internal sealed class OllamaCliChatProvider : ICliChatProvider
{
	private readonly HttpClient _httpClient;
	private readonly string? _configuredModel;
	private readonly List<string> _availableModels = [];

	public OllamaCliChatProvider(string endpoint, string? configuredModel = null, TimeSpan? timeout = null, HttpClient? httpClient = null)
	{
		_configuredModel = string.IsNullOrWhiteSpace(configuredModel) ? null : configuredModel;
		_httpClient = httpClient ?? new HttpClient
		{
			BaseAddress = new Uri(endpoint, UriKind.Absolute),
			Timeout = timeout ?? TimeSpan.FromSeconds(ReadTimeoutSeconds()),
		};
		DefaultModel = _configuredModel ?? string.Empty;
	}

	public string ProviderName => "local Ollama";
	public bool RequiresAuthentication => false;
	public string DefaultModel { get; private set; }
	public IReadOnlyList<string> AvailableModels => _availableModels;

	public static OllamaCliChatProvider FromEnvironment()
	{
		var endpoint = Environment.GetEnvironmentVariable("NEXUS_OLLAMA_ENDPOINT") ?? "http://127.0.0.1:11434";
		var model = Environment.GetEnvironmentVariable("NEXUS_OLLAMA_MODEL");
		return new OllamaCliChatProvider(endpoint, model);
	}

	public async Task InitializeAsync(CancellationToken ct = default)
	{
		using var response = await _httpClient.GetAsync("/api/tags", ct).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

		_availableModels.Clear();
		_availableModels.AddRange(document.RootElement
			.GetProperty("models")
			.EnumerateArray()
			.Select(model => model.GetProperty("name").GetString())
			.Where(static model => !string.IsNullOrWhiteSpace(model))
			.Cast<string>());

		if (_availableModels.Count == 0)
			throw new InvalidOperationException("No models are installed in Ollama. Run 'ollama pull <model>' first.");

		DefaultModel = _configuredModel is not null
			? _availableModels.FirstOrDefault(model => string.Equals(model, _configuredModel, StringComparison.OrdinalIgnoreCase)) ?? _configuredModel
			: _availableModels[0];
	}

	public Task AuthenticateAsync(CancellationToken ct = default) => Task.CompletedTask;

	public bool SupportsModel(string model)
		=> _availableModels.Count == 0 || _availableModels.Contains(model, StringComparer.OrdinalIgnoreCase);

	public IChatClient CreateClient(string model) => new OllamaChatClient(_httpClient, model);

	public void Logout()
	{
	}

	public void Dispose() => _httpClient.Dispose();

	private static int ReadTimeoutSeconds()
	{
		var configured = Environment.GetEnvironmentVariable("NEXUS_OLLAMA_TIMEOUT_SECONDS");
		return int.TryParse(configured, out var seconds) && seconds > 0
			? seconds
			: 300;
	}
}

internal sealed class OllamaChatClient : IChatClient
{
	private readonly HttpClient _httpClient;
	private readonly string _model;

	public OllamaChatClient(HttpClient httpClient, string model)
	{
		_httpClient = httpClient;
		_model = model;
	}

	public async Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(messages, stream: false, options);

		using var response = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
		var assistantMessage = CreateAssistantMessage(document.RootElement.GetProperty("message"));

		var chatResponse = new ChatResponse([assistantMessage]);
		AttachUsage(chatResponse, document.RootElement);
		return chatResponse;
	}

	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(messages, stream: true, options);

		using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
		{
			Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
		};

		using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		using var reader = new StreamReader(stream, Encoding.UTF8);

		JsonElement? finalPayload = null;
		var generatedCallId = 0;
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
			var toolCalls = ReadToolCalls(message, ref generatedCallId);

			if (!string.IsNullOrWhiteSpace(text) || toolCalls.Count > 0)
			{
				var contents = new List<AIContent>();
				if (!string.IsNullOrWhiteSpace(text))
					contents.Add(new TextContent(text));
				contents.AddRange(toolCalls);

				yield return new ChatResponseUpdate
				{
					Role = ChatRole.Assistant,
					Contents = contents,
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
			messages = BuildMessages(messages),
			tools = BuildTools(options),
			options = new Dictionary<string, object?>
			{
				["temperature"] = options?.Temperature ?? 0.1,
				["num_predict"] = options?.MaxOutputTokens,
			},
		};

	private static ChatMessage CreateAssistantMessage(JsonElement message)
	{
		var contents = new List<AIContent>();
		if (message.TryGetProperty("content", out var contentElement))
		{
			var text = contentElement.GetString();
			if (!string.IsNullOrWhiteSpace(text))
				contents.Add(new TextContent(text));
		}

		var generatedCallId = 0;
		contents.AddRange(ReadToolCalls(message, ref generatedCallId));
		return new ChatMessage(ChatRole.Assistant, contents);
	}

	private static List<object> BuildMessages(IEnumerable<ChatMessage> messages)
	{
		var serialized = new List<object>();
		var toolNamesByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var message in messages)
		{
			if (message.Role == ChatRole.Tool)
			{
				foreach (var content in message.Contents.OfType<FunctionResultContent>())
				{
					serialized.Add(new
					{
						role = "tool",
						content = SerializeToolResult(content.Result),
						tool_name = toolNamesByCallId.GetValueOrDefault(content.CallId),
					});
				}

				continue;
			}

			var text = message.Text ?? string.Empty;
			var functionCalls = message.Contents.OfType<FunctionCallContent>().ToList();
			foreach (var functionCall in functionCalls)
				toolNamesByCallId[functionCall.CallId] = functionCall.Name;

			serialized.Add(new
			{
				role = message.Role.ToString().ToLowerInvariant(),
				content = text,
				tool_calls = functionCalls.Count == 0
					? null
					: functionCalls.Select(functionCall => new
					{
						type = "function",
						function = new
						{
							name = functionCall.Name,
							arguments = functionCall.Arguments,
						},
					}).ToList(),
			});
		}

		return serialized;
	}

	private static List<OllamaToolDefinition>? BuildTools(ChatOptions? options)
	{
		if (options?.Tools is not { Count: > 0 })
			return null;

		return options.Tools.Select(tool =>
			new OllamaToolDefinition(
				"function",
				new OllamaFunctionDefinition(
					tool.Name,
					tool.Description,
					tool is AIFunction function ? function.JsonSchema : JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }))))
			.ToList();
	}

	private static List<FunctionCallContent> ReadToolCalls(JsonElement message, ref int generatedCallId)
	{
		var results = new List<FunctionCallContent>();
		if (!message.TryGetProperty("tool_calls", out var toolCallsElement) || toolCallsElement.ValueKind != JsonValueKind.Array)
			return results;

		foreach (var toolCall in toolCallsElement.EnumerateArray())
		{
			if (!toolCall.TryGetProperty("function", out var functionElement))
				continue;

			var name = functionElement.TryGetProperty("name", out var nameElement)
				? nameElement.GetString()
				: null;
			if (string.IsNullOrWhiteSpace(name))
				continue;

			var callId = toolCall.TryGetProperty("id", out var idElement) && !string.IsNullOrWhiteSpace(idElement.GetString())
				? idElement.GetString()!
				: FormattableString.Invariant($"ollama-call-{++generatedCallId}");

			var arguments = functionElement.TryGetProperty("arguments", out var argumentsElement)
				? ConvertToDictionary(argumentsElement)
				: new Dictionary<string, object?>();

			results.Add(new FunctionCallContent(callId, name, arguments));
		}

		return results;
	}

	private static Dictionary<string, object?> ConvertToDictionary(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.String)
		{
			var raw = element.GetString();
			if (!string.IsNullOrWhiteSpace(raw))
			{
				using var document = JsonDocument.Parse(raw);
				return ConvertToDictionary(document.RootElement);
			}
		}

		if (element.ValueKind != JsonValueKind.Object)
			return [];

		var dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var property in element.EnumerateObject())
			dictionary[property.Name] = ConvertJsonValue(property.Value);

		return dictionary;
	}

	private static object? ConvertJsonValue(JsonElement value)
		=> value.ValueKind switch
		{
			JsonValueKind.Object => ConvertToDictionary(value),
			JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToList(),
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.TryGetInt64(out var integer)
				? integer
				: value.TryGetDouble(out var floating)
					? floating
					: value.GetDecimal(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null => null,
			_ => value.GetRawText(),
		};

	private static string SerializeToolResult(object? result)
		=> result switch
		{
			null => "null",
			string text => text,
			_ => result.ToString() ?? "null",
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

internal sealed class CopilotModelDescriptor
{
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("model_picker_enabled")]
	public bool ModelPickerEnabled { get; init; }

	[JsonPropertyName("policy")]
	public CopilotModelPolicy? Policy { get; init; }

	[JsonPropertyName("supported_endpoints")]
	public List<string>? SupportedEndpoints { get; init; }
}

internal sealed class CopilotModelPolicy
{
	[JsonPropertyName("state")]
	public string? State { get; init; }
}

internal sealed class CopilotModelListResponse
{
	[JsonPropertyName("data")]
	public List<CopilotModelDescriptor> Data { get; init; } = [];
}

[JsonSerializable(typeof(CopilotModelListResponse))]
internal sealed partial class CopilotCliModelJsonContext : JsonSerializerContext;

internal sealed record OllamaToolDefinition(string Type, OllamaFunctionDefinition Function);

internal sealed record OllamaFunctionDefinition(string Name, string Description, JsonElement Parameters);