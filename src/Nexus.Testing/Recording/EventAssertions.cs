using Nexus.Core.Agents;
using Nexus.Core.Events;

namespace Nexus.Testing.Recording;

/// <summary>
/// Fluent assertion helpers for recorded agent events.
/// </summary>
public static class EventAssertions
{
    public static EventAssertionBuilder Should(this IReadOnlyList<AgentEvent> events)
        => new(events);
}

public sealed class EventAssertionBuilder
{
    private readonly IReadOnlyList<AgentEvent> _events;

    internal EventAssertionBuilder(IReadOnlyList<AgentEvent> events)
    {
        _events = events;
    }

    public EventAssertionBuilder ContainToolCall(string toolName)
    {
        if (!_events.OfType<ToolCallStartedEvent>().Any(e => e.ToolName == toolName))
            throw new InvalidOperationException($"Expected tool call '{toolName}' but none was found.");
        return this;
    }

    public EventAssertionBuilder ContainTextChunk(string? containing = null)
    {
        var chunks = _events.OfType<TextChunkEvent>().ToList();
        if (chunks.Count == 0)
            throw new InvalidOperationException("Expected at least one TextChunkEvent but none was found.");
        if (containing is not null && !chunks.Any(c => c.Text.Contains(containing, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"No TextChunkEvent contains '{containing}'.");
        return this;
    }

    public EventAssertionBuilder HaveCompletedSuccessfully()
    {
        if (!_events.OfType<AgentCompletedEvent>().Any(e => e.Result.Status == AgentResultStatus.Success))
            throw new InvalidOperationException("Expected a successful AgentCompletedEvent but none was found.");
        return this;
    }

    public EventAssertionBuilder HaveCount(int expected)
    {
        if (_events.Count != expected)
            throw new InvalidOperationException($"Expected {expected} events but found {_events.Count}.");
        return this;
    }

    public EventAssertionBuilder HaveCountGreaterThan(int minimum)
    {
        if (_events.Count <= minimum)
            throw new InvalidOperationException($"Expected more than {minimum} events but found {_events.Count}.");
        return this;
    }

    public EventAssertionBuilder NotContain<T>() where T : AgentEvent
    {
        if (_events.OfType<T>().Any())
            throw new InvalidOperationException($"Expected no {typeof(T).Name} events but found some.");
        return this;
    }
}
