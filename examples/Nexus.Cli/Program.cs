namespace Nexus.Cli;

internal static class Program
{
    private static async Task<int> Main()
    {
        using var app = CliApplication.CreateDefault();
        return await app.RunAsync().ConfigureAwait(false);
    }
}
