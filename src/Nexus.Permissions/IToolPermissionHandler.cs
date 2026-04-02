using System.Text.Json;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Permissions;

/// <summary>
/// Evaluates whether an agent is permitted to invoke a specific tool with given arguments.
/// </summary>
public interface IToolPermissionHandler
{
    /// <summary>Evaluates the permission for a tool invocation and returns a decision.</summary>
    Task<PermissionDecision> EvaluateAsync(
        ITool tool,
        JsonElement input,
        IToolContext context,
        CancellationToken ct = default);
}