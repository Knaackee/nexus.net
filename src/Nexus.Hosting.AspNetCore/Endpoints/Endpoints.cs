using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Nexus.Core.Events;
using Nexus.Orchestration;
using Nexus.Protocols.AgUi;

namespace Nexus.Hosting.AspNetCore.Endpoints;

/// <summary>
/// Streams AG-UI events over Server-Sent Events (SSE) for frontend consumption.
/// </summary>
public static class AgUiEndpoint
{
    public static async Task HandleAsync(
        HttpContext httpContext,
        IOrchestrator orchestrator,
        ITaskGraph graph,
        CancellationToken ct = default)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var events = orchestrator.ExecuteGraphStreamingAsync(graph, ct);
        var agUiEvents = AgUiEventBridge.BridgeAsync(events, ct);

        await foreach (var evt in agUiEvents.WithCancellation(ct))
        {
            var json = JsonSerializer.Serialize(evt, AgUiSerializerContext.Default.AgUiEvent);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Handles A2A JSON-RPC requests over HTTP.
/// </summary>
public static class A2AEndpoint
{
    public static async Task HandleAsync(
        HttpContext httpContext,
        Func<JsonElement, CancellationToken, Task<JsonElement>> handler,
        CancellationToken ct = default)
    {
        if (httpContext.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        JsonElement requestBody;
        try
        {
            using var doc = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct)
                .ConfigureAwait(false);
            requestBody = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Invalid JSON" }, ct).ConfigureAwait(false);
            return;
        }

        var result = await handler(requestBody, ct).ConfigureAwait(false);

        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(result, ct).ConfigureAwait(false);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(AgUiEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(UserInputRequest))]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiRunStartedEvent), "runStarted")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiRunFinishedEvent), "runFinished")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiTextChunkEvent), "textChunk")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiReasoningChunkEvent), "reasoningChunk")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiToolCallStartEvent), "toolCallStart")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiToolCallEndEvent), "toolCallEnd")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiApprovalRequestedEvent), "approvalRequested")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiUserInputRequestEvent), "userInputRequest")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiStateDeltaEvent), "stateDelta")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiStateSnapshotEvent), "stateSnapshot")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiStepStartedEvent), "stepStarted")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiStepFinishedEvent), "stepFinished")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiCustomEvent), "custom")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AgUiErrorEvent), "error")]
internal sealed partial class AgUiSerializerContext : System.Text.Json.Serialization.JsonSerializerContext;
