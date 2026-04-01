using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Core.Configuration;

namespace Nexus.CostTracking.Tests;

public class DefaultCostTrackerTests
{
    [Fact]
    public async Task Aggregates_Usage_Across_Multiple_Calls()
    {
        var tracker = new DefaultCostTracker(new DefaultModelPricingProvider(new CostTrackingOptions()
            .AddModel("gpt-4o", 1m, 5m)));

        await tracker.RecordUsageAsync("gpt-4o", new UsageSnapshot(1000, 500));
        await tracker.RecordUsageAsync("gpt-4o", new UsageSnapshot(2000, 1000));

        var snapshot = await tracker.GetSnapshotAsync();
        snapshot.TotalInputTokens.Should().Be(3000);
        snapshot.TotalOutputTokens.Should().Be(1500);
        snapshot.Models["gpt-4o"].Requests.Should().Be(2);
        snapshot.TotalCost.Should().Be(0.0105m);
    }

    [Fact]
    public async Task Unknown_Model_Does_Not_Crash_And_Sets_Flag()
    {
        var tracker = new DefaultCostTracker(new DefaultModelPricingProvider(new CostTrackingOptions()));

        await tracker.RecordUsageAsync("unknown-model", new UsageSnapshot(100, 50));

        var snapshot = await tracker.GetSnapshotAsync();
        snapshot.TotalCost.Should().Be(0);
        snapshot.HasUnknownPricing.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_Clears_All_State()
    {
        var tracker = new DefaultCostTracker(new DefaultModelPricingProvider(new CostTrackingOptions()
            .AddModel("gpt-4o", 1m, 5m)));

        await tracker.RecordUsageAsync("gpt-4o", new UsageSnapshot(100, 50));
        await tracker.ResetAsync();

        var snapshot = await tracker.GetSnapshotAsync();
        snapshot.TotalTokens.Should().Be(0);
        snapshot.Models.Should().BeEmpty();
    }
}

public class CostTrackingChatClientTests
{
    private static readonly string[] StreamingChunks = ["hel", "lo"];

    [Fact]
    public async Task GetResponseAsync_Extracts_Usage_And_Tracks_Cost()
    {
        var inner = new ReflectionBackedChatClient();
        inner.AddResponse("hello", "gpt-4o", inputTokens: 1000, outputTokens: 500);

        var pricingProvider = new DefaultModelPricingProvider(new CostTrackingOptions()
            .AddModel("gpt-4o", 1m, 5m));
        var tracker = new DefaultCostTracker(pricingProvider);
        var client = new CostTrackingChatClient(inner, tracker, pricingProvider);

        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var snapshot = await tracker.GetSnapshotAsync();
        snapshot.TotalInputTokens.Should().Be(1000);
        snapshot.TotalOutputTokens.Should().Be(500);
        snapshot.TotalCost.Should().Be(0.0035m);
    }

    [Fact]
    public async Task Streaming_Tracks_Usage_From_Updates()
    {
        var inner = new ReflectionBackedChatClient();
        inner.AddStreamingResponse(StreamingChunks, "gpt-4o-mini", inputTokens: 800, outputTokens: 200);

        var pricingProvider = new DefaultModelPricingProvider(new CostTrackingOptions()
            .AddModel("gpt-4o-mini", 0.5m, 2m));
        var tracker = new DefaultCostTracker(pricingProvider);
        var client = new CostTrackingChatClient(inner, tracker, pricingProvider);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
        }

        var snapshot = await tracker.GetSnapshotAsync();
        snapshot.TotalInputTokens.Should().Be(800);
        snapshot.TotalOutputTokens.Should().Be(200);
        snapshot.TotalCost.Should().Be(0.0008m);
    }

    [Fact]
    public async Task Missing_Usage_Does_Not_Record_Cost()
    {
        var inner = new ReflectionBackedChatClient();
        inner.AddResponse("hello");

        var pricingProvider = new DefaultModelPricingProvider(new CostTrackingOptions());
        var tracker = new DefaultCostTracker(pricingProvider);
        var client = new CostTrackingChatClient(inner, tracker, pricingProvider);

        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var snapshot = await tracker.GetSnapshotAsync();
        snapshot.TotalTokens.Should().Be(0);
    }
}

public class UsageReflectionExtractorTests
{
    [Fact]
    public void Extracts_Common_Usage_Property_Names()
    {
        var source = new
        {
            ModelId = "test-model",
            Usage = new
            {
                InputTokenCount = 120,
                OutputTokenCount = 30,
                CacheReadInputTokenCount = 10,
                CacheWriteInputTokenCount = 5,
                TotalTokenCount = 165,
            }
        };

        var extracted = UsageReflectionExtractor.TryExtractResponse(source, out var modelId, out var usage);

        extracted.Should().BeTrue();
        modelId.Should().Be("test-model");
        usage.InputTokens.Should().Be(120);
        usage.OutputTokens.Should().Be(30);
        usage.TotalTokens.Should().Be(165);
    }
}

public class CostTrackingBuilderTests
{
    [Fact]
    public async Task AddCostTracking_Registers_Services_And_Wraps_Default_Client()
    {
        var services = new ServiceCollection();

        services.AddNexus(nexus =>
        {
            nexus.UseChatClient(_ =>
            {
                var client = new ReflectionBackedChatClient();
                client.AddResponse("hello", "gpt-4o", 100, 50);
                return client;
            });
            nexus.AddCostTracking(c => c.AddModel("gpt-4o", 1m, 5m));
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IChatClient>();
        client.Should().BeOfType<CostTrackingChatClient>();

        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        var tracker = provider.GetRequiredService<ICostTracker>();
        var snapshot = await tracker.GetSnapshotAsync();
        snapshot.TotalTokens.Should().Be(150);
    }
}

internal sealed class ReflectionBackedChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new();
    private readonly Queue<List<ChatResponseUpdate>> _updates = new();

    public void AddResponse(string text, string? modelId = null, int inputTokens = 0, int outputTokens = 0)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        SetModelAndUsage(response, modelId, inputTokens, outputTokens);
        _responses.Enqueue(response);
        _updates.Enqueue([
            CreateUpdate(text, modelId, inputTokens, outputTokens),
        ]);
    }

    public void AddStreamingResponse(IEnumerable<string> chunks, string? modelId = null, int inputTokens = 0, int outputTokens = 0)
    {
        var updateList = chunks.Select(chunk => CreateUpdate(chunk)).ToList();
        updateList.Add(CreateUpdate(string.Empty, modelId, inputTokens, outputTokens));
        _updates.Enqueue(updateList);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(chunks)));
        SetModelAndUsage(response, modelId, inputTokens, outputTokens);
        _responses.Enqueue(response);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Task.FromResult(_responses.Dequeue());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in _updates.Dequeue())
        {
            yield return update;
            await Task.Yield();
        }
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private static ChatResponseUpdate CreateUpdate(string text, string? modelId = null, int inputTokens = 0, int outputTokens = 0)
    {
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = string.IsNullOrEmpty(text) ? [] : [new TextContent(text)],
        };
        SetModelAndUsage(update, modelId, inputTokens, outputTokens);
        return update;
    }

    private static void SetModelAndUsage(object target, string? modelId, int inputTokens, int outputTokens)
    {
        SetPropertyIfExists(target, "ModelId", modelId);

        if (inputTokens == 0 && outputTokens == 0)
            return;

        var usageProperty = target.GetType().GetProperty("Usage", BindingFlags.Instance | BindingFlags.Public);
        var usage = Activator.CreateInstance(typeof(ChatResponse).GetProperty("Usage", BindingFlags.Instance | BindingFlags.Public)!.PropertyType);
        if (usage is null)
            return;

        SetPropertyIfExists(usage, "InputTokenCount", inputTokens);
        SetPropertyIfExists(usage, "OutputTokenCount", outputTokens);
        SetPropertyIfExists(usage, "TotalTokenCount", inputTokens + outputTokens);
        SetPropertyIfExists(usage, "CachedInputTokenCount", 0L);
        SetPropertyIfExists(usage, "PromptTokenCount", inputTokens);
        SetPropertyIfExists(usage, "CompletionTokenCount", outputTokens);
        SetPropertyIfExists(usage, "InputTokens", inputTokens);
        SetPropertyIfExists(usage, "OutputTokens", outputTokens);
        SetPropertyIfExists(usage, "TotalTokens", inputTokens + outputTokens);

        if (usageProperty?.CanWrite == true)
        {
            usageProperty.SetValue(target, usage);
            return;
        }

        var additionalPropertiesProperty = target.GetType().GetProperty("AdditionalProperties", BindingFlags.Instance | BindingFlags.Public);
        if (additionalPropertiesProperty?.CanWrite != true)
            return;

        var dictionary = additionalPropertiesProperty.GetValue(target);
        if (dictionary is null)
        {
            dictionary = Activator.CreateInstance(additionalPropertiesProperty.PropertyType);
            additionalPropertiesProperty.SetValue(target, dictionary);
        }

        var indexer = dictionary?.GetType().GetProperty("Item");
        indexer?.SetValue(dictionary, usage, ["Usage"]);
    }

    private static void SetPropertyIfExists(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite == true)
            property.SetValue(target, ConvertValue(value, property.PropertyType));
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(value))
            return value;

        if (effectiveType.IsEnum)
            return Enum.ToObject(effectiveType, value);

        return Convert.ChangeType(value, effectiveType, System.Globalization.CultureInfo.InvariantCulture);
    }
}