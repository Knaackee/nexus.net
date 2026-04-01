using System.Diagnostics;
using System.Text.Json;
using Nexus.Core.Tools;

namespace Nexus.Tools.Standard;

public sealed class ShellTool : ITool
{
    private readonly StandardToolOptions _options;

    public ShellTool(StandardToolOptions options)
    {
        _options = options;
    }

    public string Name => "shell";

    public string Description => "Executes a shell command in the configured working directory.";

    public ToolAnnotations? Annotations => new()
    {
        RequiresApproval = true,
        IsReadOnly = false,
        IsDestructive = true,
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement input, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var workingDirectory = ToolJson.GetOptionalString(input, "workingDirectory") ?? _options.WorkingDirectory ?? _options.BaseDirectory;
            var resolvedWorkingDirectory = PathSandbox.ResolvePath(_options.BaseDirectory, workingDirectory);

            ProcessStartInfo startInfo;
            var commandLine = ToolJson.GetOptionalString(input, "commandLine");
            if (!string.IsNullOrWhiteSpace(commandLine))
            {
                startInfo = BuildShellWrapperStartInfo(commandLine, resolvedWorkingDirectory);
            }
            else
            {
                var command = ToolJson.GetRequiredString(input, "command");
                startInfo = new ProcessStartInfo(command)
                {
                    WorkingDirectory = resolvedWorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };

                foreach (var argument in ToolJson.GetOptionalStringArray(input, "arguments"))
                    startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.ShellTimeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var result = new ShellCommandResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
            return process.ExitCode == 0
                ? ToolResult.Success(result)
                : ToolResult.Failure($"Command exited with code {process.ExitCode}: {result.StandardError}");
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(ex.Message);
        }
    }

    private static ProcessStartInfo BuildShellWrapperStartInfo(string commandLine, string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                Arguments = $"/d /c {commandLine}",
            };
        }

        return new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = $"-lc \"{commandLine.Replace("\"", "\\\"") }\"",
        };
    }
}