using FluentAssertions;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliLaunchOptionsTests
{
	[Fact]
	public void Parse_Recognizes_Tui_Flag()
	{
		CliLaunchOptions.Parse(["--tui"]).UseTui.Should().BeTrue();
	}

	[Fact]
	public void Parse_Defaults_To_Line_Mode()
	{
		CliLaunchOptions.Parse([]).UseTui.Should().BeFalse();
	}
}