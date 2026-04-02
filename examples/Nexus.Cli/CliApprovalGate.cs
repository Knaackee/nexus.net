using Nexus.Core.Contracts;

namespace Nexus.Cli;

internal sealed class CliApprovalGate : IApprovalGate
{
    private readonly bool _allowShell;

    public CliApprovalGate(bool allowShell = false)
    {
        _allowShell = allowShell;
    }

    public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (string.Equals(request.ToolName, "shell", StringComparison.OrdinalIgnoreCase) && !_allowShell)
        {
            return Task.FromResult(new ApprovalResult(
                false,
                Comment: "Shell tool execution is disabled in Nexus.Cli by default. Set NEXUS_CLI_ALLOW_SHELL=1 to enable it."));
        }

        return Task.FromResult(new ApprovalResult(true, "nexus-cli-auto-approve", "Approved by Nexus.Cli policy"));
    }

    public static CliApprovalGate FromEnvironment()
    {
        return new CliApprovalGate(IsShellAllowedFromEnvironment());
    }

    public static bool IsShellAllowedFromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("NEXUS_CLI_ALLOW_SHELL");
        return string.Equals(configured, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configured, "true", StringComparison.OrdinalIgnoreCase);
    }
}