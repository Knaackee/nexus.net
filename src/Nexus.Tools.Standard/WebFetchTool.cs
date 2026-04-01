using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class WebFetchTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly StandardToolOptions _options;

    public WebFetchTool(HttpClient httpClient, StandardToolOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "web_fetch";

    public string Description => "Fetches the content of a web page via HTTP GET.";

    public ToolAnnotations? Annotations => new()
    {
        IsReadOnly = true,
        IsIdempotent = true,
        IsOpenWorld = true,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var url = ToolJson.GetRequiredString(input, "url");
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (content.Length > _options.MaxFetchCharacters)
                content = content[.._options.MaxFetchCharacters];

            return ToolResult.Success(new WebFetchResult(url, (int)response.StatusCode, content));
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }
}