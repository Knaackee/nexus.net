using Nexus.Core.Contracts;

namespace Nexus.Permissions;

public sealed class ConsolePermissionPrompt : IPermissionPrompt
{
    public Task<ApprovalResult> PromptAsync(
        ApprovalRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        Console.WriteLine($"[permissions] Agent {request.RequestingAgent} requests tool '{request.ToolName ?? "unknown"}'");
        Console.WriteLine($"[permissions] {request.Description}");
        Console.Write("Approve? [y/N]: ");

        var response = Console.ReadLine();
        var approved = string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(new ApprovalResult(
            approved,
            approved ? Environment.UserName : null,
            approved ? "Approved in console" : "Denied in console"));
    }
}