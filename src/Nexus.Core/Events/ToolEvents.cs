using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Core.Tools;

namespace Nexus.Core.Events;

public abstract record ToolEvent(string ToolName, DateTimeOffset Timestamp)
{
    protected ToolEvent(string toolName) : this(toolName, DateTimeOffset.UtcNow) { }
}

public record ToolProgressEvent(string ToolName, string Message, double? ProgressPercent)
    : ToolEvent(ToolName, DateTimeOffset.UtcNow);

public record ToolLogEvent(string ToolName, string LogLine, LogLevel Level)
    : ToolEvent(ToolName, DateTimeOffset.UtcNow);

public record ToolPartialResultEvent(string ToolName, JsonElement PartialResult)
    : ToolEvent(ToolName, DateTimeOffset.UtcNow);

public record ToolCompletedEvent(string ToolName, ToolResult Result)
    : ToolEvent(ToolName, DateTimeOffset.UtcNow);
