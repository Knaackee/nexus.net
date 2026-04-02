namespace Nexus.Cli;

internal sealed record CliLaunchOptions(bool UseTui)
{
	public static CliLaunchOptions Parse(string[] args)
		=> new(args.Any(arg => string.Equals(arg, "--tui", StringComparison.OrdinalIgnoreCase)));
}