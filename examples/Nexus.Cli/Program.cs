namespace Nexus.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = CliLaunchOptions.Parse(args);
        if (options.UseTui)
        {
            using var tui = CliTuiHost.CreateDefault();
            return await tui.RunAsync().ConfigureAwait(false);
        }

        using var app = CliApplication.CreateDefault();
        return await app.RunAsync().ConfigureAwait(false);
    }
}
