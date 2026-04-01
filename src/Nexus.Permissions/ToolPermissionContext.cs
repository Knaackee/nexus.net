using System.Text.Json;
using Nexus.Core.Agents;
using Nexus.Core.Contracts;
using Nexus.Core.Tools;

namespace Nexus.Permissions;

public sealed record ToolPermissionContext(
    string ToolName,
    JsonElement Input,
    ToolAnnotations? Annotations,
    AgentId AgentId,
    CorrelationContext Correlation);