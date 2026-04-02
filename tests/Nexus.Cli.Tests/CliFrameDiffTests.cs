using FluentAssertions;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliFrameDiffTests
{
	[Fact]
	public void Compute_Returns_Only_Changed_Lines()
	{
		var previous = new CliTuiFrame(["a", "b", "c"], 1, 1);
		var current = new CliTuiFrame(["a", "x", "c"], 1, 1);

		var diff = CliFrameDiff.Compute(previous, current);

		diff.Should().ContainSingle();
		diff[0].Row.Should().Be(1);
		diff[0].Text.Should().Be("x");
	}
}