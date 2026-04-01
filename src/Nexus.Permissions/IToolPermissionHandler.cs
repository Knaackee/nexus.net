using System.Text.Json;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;

namespace Nexus.Permissions;

public interface IToolPermissionHandler
{
    Task<PermissionDecision> EvaluateAsync(
        ITool tool,
        JsonElement input,
        IToolContext context,
        CancellationToken ct = default);
}