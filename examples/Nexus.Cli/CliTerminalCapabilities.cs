namespace Nexus.Cli;

internal sealed record CliTerminalCapabilities(
    bool SupportsAnsi,
    bool SupportsFullScreen,
    bool IsInputRedirected,
    bool IsOutputRedirected,
    bool SupportsUnicode,
    bool ReducedMotion,
    int MinimumWidth,
    int MinimumHeight)
{
    public static CliTerminalCapabilities DetectCurrent()
        => Create(
            Console.IsInputRedirected,
            Console.IsOutputRedirected,
            OperatingSystem.IsWindows() || !Console.IsOutputRedirected,
            !string.Equals(Environment.GetEnvironmentVariable("NEXUS_CLI_REDUCED_MOTION"), "0", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(Environment.GetEnvironmentVariable("NEXUS_CLI_REDUCED_MOTION"), "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Environment.GetEnvironmentVariable("NEXUS_CLI_REDUCED_MOTION"), "true", StringComparison.OrdinalIgnoreCase)));

    public static CliTerminalCapabilities Create(
        bool isInputRedirected,
        bool isOutputRedirected,
        bool supportsAnsi,
        bool reducedMotion = false,
        bool supportsUnicode = true,
        int minimumWidth = 60,
        int minimumHeight = 16)
    {
        var fullScreen = supportsAnsi && !isInputRedirected && !isOutputRedirected;
        return new CliTerminalCapabilities(
            supportsAnsi,
            fullScreen,
            isInputRedirected,
            isOutputRedirected,
            supportsUnicode,
            reducedMotion,
            minimumWidth,
            minimumHeight);
    }

    public bool IsTuiViable(int width, int height)
        => SupportsFullScreen && width >= MinimumWidth && height >= MinimumHeight;
}