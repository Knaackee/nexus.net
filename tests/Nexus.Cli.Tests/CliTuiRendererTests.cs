using FluentAssertions;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliTuiRendererTests
{
	private static readonly CliTerminalCapabilities Capabilities = CliTerminalCapabilities.Create(false, false, true);

	[Fact]
	public void Render_LowWidth_Shows_Compact_Header()
	{
		var state = CliTuiState.Create(50, 20, Capabilities) with
		{
			ProviderName = "Ollama",
			WorkspaceRoot = "D:/Development/nexus",
		};

		var frame = CliTuiRenderer.Render(state);

		frame.Lines[0].Should().Contain("compact");
	}

	[Fact]
	public void Render_CommandPalette_Draws_Overlay()
	{
		var state = CliTuiState.Create(120, 30, Capabilities) with
		{
			Commands = [new CliTuiCommandItem("help", "/help", "Show help")],
			ShowCommandPalette = true,
		};

		var frame = CliTuiRenderer.Render(state);

		frame.Lines.Any(line => line.Contains("Commands", StringComparison.Ordinal)).Should().BeTrue();
		frame.Lines.Any(line => line.Contains("/help", StringComparison.Ordinal)).Should().BeTrue();
	}
}