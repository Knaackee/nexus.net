using FluentAssertions;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliTerminalCapabilitiesTests
{
	[Fact]
	public void Create_Disables_Tui_For_Redirected_Output()
	{
		var capabilities = CliTerminalCapabilities.Create(
			isInputRedirected: false,
			isOutputRedirected: true,
			supportsAnsi: true);

		capabilities.IsTuiViable(120, 40).Should().BeFalse();
	}

	[Fact]
	public void Create_Requires_Minimum_Size()
	{
		var capabilities = CliTerminalCapabilities.Create(
			isInputRedirected: false,
			isOutputRedirected: false,
			supportsAnsi: true,
			minimumWidth: 80,
			minimumHeight: 20);

		capabilities.IsTuiViable(79, 30).Should().BeFalse();
		capabilities.IsTuiViable(120, 19).Should().BeFalse();
		capabilities.IsTuiViable(120, 30).Should().BeTrue();
	}
}