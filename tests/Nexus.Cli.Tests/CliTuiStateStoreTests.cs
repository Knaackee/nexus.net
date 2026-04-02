using FluentAssertions;
using Xunit;

namespace Nexus.Cli.Tests;

public sealed class CliTuiStateStoreTests
{
	private static readonly CliTerminalCapabilities Capabilities = CliTerminalCapabilities.Create(false, false, true);

	[Fact]
	public void Editor_Actions_Update_Text_And_Cursor()
	{
		var store = new CliTuiStateStore(CliTuiState.Create(120, 40, Capabilities));

		store.Dispatch(new InsertTextAction("hello"));
		store.Dispatch(new MoveCursorHorizontalAction(-2));
		store.Dispatch(new InsertTextAction("X"));

		store.Snapshot.Editor.Text.Should().Be("helXlo");
		store.Snapshot.Editor.CursorIndex.Should().Be(4);
	}

	[Fact]
	public void CommitSubmittedInput_Adds_History_And_Transcript()
	{
		var store = new CliTuiStateStore(CliTuiState.Create(120, 40, Capabilities));

		store.Dispatch(new ReplaceEditorTextAction("/help"));
		store.Dispatch(new CommitSubmittedInputAction("/help"));

		store.Snapshot.Editor.Text.Should().BeEmpty();
		store.Snapshot.Editor.History.Should().ContainSingle().Which.Should().Be("/help");
		store.Snapshot.TranscriptLogText.Should().Contain("> /help");
	}

	[Fact]
	public void BrowseHistory_Loads_Previous_Entry()
	{
		var store = new CliTuiStateStore(CliTuiState.Create(120, 40, Capabilities));

		store.Dispatch(new CommitSubmittedInputAction("first"));
		store.Dispatch(new CommitSubmittedInputAction("second"));
		store.Dispatch(new BrowseHistoryAction(1));

		store.Snapshot.Editor.Text.Should().Be("second");
	}
}