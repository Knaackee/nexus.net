using System.Text;
using Nexus.Commands;
using Spectre.Console;

namespace Nexus.Cli;

internal sealed class CliTuiHost : IDisposable
{
	private readonly CapturingAnsiConsoleOutput _output;
	private readonly CliApplication _app;
	private readonly CliTuiStateStore _stateStore;
	private readonly CliTerminalCapabilities _capabilities;
	private bool _disposed;
	private int _lastWidth;
	private int _lastHeight;
	private CliTuiFrame? _lastFrame;

	private CliTuiHost(CapturingAnsiConsoleOutput output, CliApplication app, CliTerminalCapabilities capabilities, CliTuiStateStore stateStore)
	{
		_output = output;
		_app = app;
		_capabilities = capabilities;
		_stateStore = stateStore;
		_lastWidth = output.Width;
		_lastHeight = output.Height;
	}

	public static CliTuiHost CreateDefault()
	{
		var capabilities = CliTerminalCapabilities.DetectCurrent();
		var output = new CapturingAnsiConsoleOutput();
		var console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Ansi = AnsiSupport.Yes,
			ColorSystem = ColorSystemSupport.Detect,
			Interactive = InteractionSupport.No,
			Out = output,
		});

		var app = new CliApplication(console: console, useInteractivePrompt: false, useTuiPresentation: true);
		var stateStore = new CliTuiStateStore(CliTuiState.Create(output.Width, output.Height, capabilities));
		return new CliTuiHost(output, app, capabilities, stateStore);
	}

	public async Task<int> RunAsync(CancellationToken ct = default)
	{
		if (!_capabilities.IsTuiViable(Console.WindowWidth, Console.WindowHeight))
		{
			return await _app.RunAsync(ct).ConfigureAwait(false);
		}

		using var session = new TerminalSession();

		try
		{
			if (!await _app.StartAsync(ct).ConfigureAwait(false))
				return 1;

			_output.Clear();
			SyncRuntimeSnapshot();
			Render(forceFullRedraw: true);

			while (!ct.IsCancellationRequested)
			{
				if (UpdateTerminalSize())
					_stateStore.Dispatch(new ResizeTerminalAction(_lastWidth, _lastHeight));

				SyncRuntimeSnapshot();

				if (Console.KeyAvailable)
				{
					var key = Console.ReadKey(intercept: true);
					var continueProcessing = await HandleKeyAsync(key, ct).ConfigureAwait(false);
					Render();
					if (!continueProcessing)
						return 0;
					continue;
				}

				Render();
				await Task.Delay(_capabilities.ReducedMotion ? 120 : 33, ct).ConfigureAwait(false);
			}
		}
		finally
		{
			Console.Write("\u001b[2J\u001b[H");
		}

		return 0;
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_app.Dispose();
		_output.Dispose();
	}

	private async Task<bool> HandleKeyAsync(ConsoleKeyInfo key, CancellationToken ct)
	{
		var state = _stateStore.Snapshot;

		if (state.ShowSessionPicker)
			return await HandleSessionPickerKeyAsync(key, ct).ConfigureAwait(false);

		if (state.ShowCommandPalette)
			return await HandleCommandPaletteKeyAsync(key, ct).ConfigureAwait(false);

		if (state.ShowSearch)
			return await HandleSearchKeyAsync(key).ConfigureAwait(false);

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C)
		{
			if (_app.Manager.ActiveSession?.State == ChatSessionState.Running)
				return await _app.ExecuteInputAsync("/cancel", ct).ConfigureAwait(false);

			return false;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Q)
			return false;

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.P)
		{
			_stateStore.Dispatch(new ToggleSessionPickerAction(true));
			return true;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.K)
		{
			_stateStore.Dispatch(new ToggleCommandPaletteAction(true));
			return true;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.F)
		{
			_stateStore.Dispatch(new ToggleSearchOverlayAction(true));
			return true;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.G)
		{
			_stateStore.Dispatch(new ApplySearchAction(string.Empty));
			_stateStore.Dispatch(new SetNoticeAction("Transcript filter cleared."));
			return true;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.E)
		{
			ExportTranscript();
			return true;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.L)
		{
			await _app.ExecuteInputAsync("/clear", ct).ConfigureAwait(false);
			_stateStore.Dispatch(new SetNoticeAction("Active chat cleared."));
			return true;
		}

		if (key.Key == ConsoleKey.Tab)
		{
			CycleSession((key.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : 1);
			return true;
		}

		if (key.Key == ConsoleKey.PageUp)
		{
			_stateStore.Dispatch(new ScrollTranscriptAction(Math.Max(1, _stateStore.Snapshot.Height / 3)));
			return true;
		}

		if (key.Key == ConsoleKey.PageDown)
		{
			_stateStore.Dispatch(new ScrollTranscriptAction(-Math.Max(1, _stateStore.Snapshot.Height / 3)));
			return true;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.UpArrow)
		{
			_stateStore.Dispatch(new BrowseHistoryAction(1));
			return true;
		}

		if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.DownArrow)
		{
			_stateStore.Dispatch(new BrowseHistoryAction(-1));
			return true;
		}

		switch (key.Key)
		{
			case ConsoleKey.Enter:
				if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
				{
					_stateStore.Dispatch(new InsertTextAction(Environment.NewLine));
					return true;
				}

				var input = _stateStore.Snapshot.Editor.Text;
				if (string.IsNullOrWhiteSpace(input))
					return true;

				_stateStore.Dispatch(new CommitSubmittedInputAction(input));
				var continueProcessing = await _app.ExecuteInputAsync(input, ct).ConfigureAwait(false);
				_stateStore.Dispatch(new SetNoticeAction(continueProcessing ? string.Empty : "Exiting Nexus CLI."));
				return continueProcessing;

			case ConsoleKey.Backspace:
				_stateStore.Dispatch(new BackspaceAction());
				return true;

			case ConsoleKey.Delete:
				_stateStore.Dispatch(new DeleteAction());
				return true;

			case ConsoleKey.Escape:
				_stateStore.Dispatch(new ReplaceEditorTextAction(string.Empty));
				_stateStore.Dispatch(new SetNoticeAction("Composer cleared."));
				return true;

			case ConsoleKey.LeftArrow:
				_stateStore.Dispatch(new MoveCursorHorizontalAction(-1));
				return true;

			case ConsoleKey.RightArrow:
				_stateStore.Dispatch(new MoveCursorHorizontalAction(1));
				return true;

			case ConsoleKey.UpArrow:
				_stateStore.Dispatch(new MoveCursorVerticalAction(-1));
				return true;

			case ConsoleKey.DownArrow:
				_stateStore.Dispatch(new MoveCursorVerticalAction(1));
				return true;

			case ConsoleKey.Home:
				_stateStore.Dispatch(new MoveCursorHomeAction());
				return true;

			case ConsoleKey.End:
				_stateStore.Dispatch(new MoveCursorEndAction());
				return true;

			default:
				if (!char.IsControl(key.KeyChar))
					_stateStore.Dispatch(new InsertTextAction(key.KeyChar.ToString()));
				return true;
		}
	}

	private Task<bool> HandleSessionPickerKeyAsync(ConsoleKeyInfo key, CancellationToken ct)
	{
		if (key.Key == ConsoleKey.Escape)
		{
			_stateStore.Dispatch(new ToggleSessionPickerAction(false));
			return Task.FromResult(true);
		}

		if (key.Key == ConsoleKey.Backspace)
		{
			var query = _stateStore.Snapshot.SessionPickerQuery;
			if (query.Length > 0)
				_stateStore.Dispatch(new SetSessionPickerQueryAction(query[..^1]));
			return Task.FromResult(true);
		}

		if (key.Key == ConsoleKey.Enter)
		{
			var matches = FilteredSessions();
			if (matches.Length > 0)
			{
				var match = matches[0];
				_app.Manager.Switch(match.Key);
				_stateStore.Dispatch(new SetNoticeAction($"Switched to {match.Key}."));
			}

			_stateStore.Dispatch(new ToggleSessionPickerAction(false));
			return Task.FromResult(true);
		}

		if (!char.IsControl(key.KeyChar))
			_stateStore.Dispatch(new SetSessionPickerQueryAction(_stateStore.Snapshot.SessionPickerQuery + key.KeyChar));

		return Task.FromResult(true);
	}

	private Task<bool> HandleCommandPaletteKeyAsync(ConsoleKeyInfo key, CancellationToken ct)
	{
		if (key.Key == ConsoleKey.Escape)
		{
			_stateStore.Dispatch(new ToggleCommandPaletteAction(false));
			return Task.FromResult(true);
		}

		if (key.Key == ConsoleKey.Backspace)
		{
			var query = _stateStore.Snapshot.CommandPaletteQuery;
			if (query.Length > 0)
				_stateStore.Dispatch(new SetCommandPaletteQueryAction(query[..^1]));
			return Task.FromResult(true);
		}

		if (key.Key == ConsoleKey.Enter)
		{
			var matches = FilteredCommands();
			if (matches.Length > 0)
			{
				var match = matches[0];
				_stateStore.Dispatch(new ReplaceEditorTextAction($"/{match.Name} "));
				_stateStore.Dispatch(new SetNoticeAction($"Inserted /{match.Name}."));
			}

			_stateStore.Dispatch(new ToggleCommandPaletteAction(false));
			return Task.FromResult(true);
		}

		if (!char.IsControl(key.KeyChar))
			_stateStore.Dispatch(new SetCommandPaletteQueryAction(_stateStore.Snapshot.CommandPaletteQuery + key.KeyChar));

		return Task.FromResult(true);
	}

	private Task<bool> HandleSearchKeyAsync(ConsoleKeyInfo key)
	{
		if (key.Key == ConsoleKey.Escape)
		{
			_stateStore.Dispatch(new ToggleSearchOverlayAction(false));
			return Task.FromResult(true);
		}

		if (key.Key == ConsoleKey.Backspace)
		{
			var query = _stateStore.Snapshot.SearchDraft;
			if (query.Length > 0)
				_stateStore.Dispatch(new SetSearchDraftAction(query[..^1]));
			return Task.FromResult(true);
		}

		if (key.Key == ConsoleKey.Enter)
		{
			_stateStore.Dispatch(new ApplySearchAction(_stateStore.Snapshot.SearchDraft));
			_stateStore.Dispatch(new SetNoticeAction(string.IsNullOrWhiteSpace(_stateStore.Snapshot.SearchDraft)
				? "Transcript filter cleared."
				: $"Filtering transcript by '{_stateStore.Snapshot.SearchDraft}'."));
			return Task.FromResult(true);
		}

		if (!char.IsControl(key.KeyChar))
			_stateStore.Dispatch(new SetSearchDraftAction(_stateStore.Snapshot.SearchDraft + key.KeyChar));

		return Task.FromResult(true);
	}

	private bool UpdateTerminalSize()
	{
		var width = Math.Max(40, Console.WindowWidth);
		var height = Math.Max(12, Console.WindowHeight);
		if (width == _lastWidth && height == _lastHeight)
			return false;

		_lastWidth = width;
		_lastHeight = height;
		_output.SetSize(width, height);
		return true;
	}

	private void SyncRuntimeSnapshot()
	{
		var sessions = _app.Manager.Sessions
			.OrderByDescending(session => string.Equals(session.Key, _app.Manager.ActiveKey, StringComparison.OrdinalIgnoreCase))
			.ThenBy(session => session.Key, StringComparer.OrdinalIgnoreCase)
			.Select(session => new CliTuiSessionItem(
				session.Key,
				session.Model,
				session.SkillName,
				session.State,
				session.MessageCount,
				string.Equals(session.Key, _app.Manager.ActiveKey, StringComparison.OrdinalIgnoreCase)))
			.ToArray();

		var snapshot = new CliRuntimeSnapshot(
			_output.GetBufferText(),
			sessions,
			_app.Manager.ActiveSession?.ToolActivity ?? [],
			_app.ListCommands().Select(command => new CliTuiCommandItem(command.Name, command.Usage, command.Description)).ToArray(),
			_app.ProviderName,
			_app.Workspace.ProjectRoot,
			DateTimeOffset.UtcNow);

		_stateStore.Dispatch(new SyncRuntimeAction(snapshot));
	}

	private void Render(bool forceFullRedraw = false)
	{
		var state = _stateStore.Snapshot;
		var frame = CliTuiRenderer.Render(state);
		var updates = forceFullRedraw || state.ForceFullRedraw
			? frame.Lines.Select((line, index) => new CliFrameLineUpdate(index, line)).ToArray()
			: CliFrameDiff.Compute(_lastFrame, frame);

		if (forceFullRedraw || state.ForceFullRedraw)
		{
			Console.Write("\u001b[2J\u001b[H");
			_stateStore.Dispatch(new RenderCompletedAction());
		}

		foreach (var update in updates)
		{
			Console.Write($"\u001b[{update.Row + 1};1H");
			Console.Write(update.Text.PadRight(Math.Max(1, Console.WindowWidth)));
		}

		Console.Write($"\u001b[{Math.Max(1, frame.CursorRow)};{Math.Max(1, frame.CursorColumn)}H");
		_lastFrame = frame;
	}

	private void CycleSession(int delta)
	{
		var sessions = _app.Manager.Sessions.OrderBy(session => session.Key, StringComparer.OrdinalIgnoreCase).ToList();
		if (sessions.Count == 0)
			return;

		var activeIndex = sessions.FindIndex(session => string.Equals(session.Key, _app.Manager.ActiveKey, StringComparison.OrdinalIgnoreCase));
		if (activeIndex < 0)
			activeIndex = 0;

		var nextIndex = (activeIndex + delta + sessions.Count) % sessions.Count;
		_app.Manager.Switch(sessions[nextIndex].Key);
		_stateStore.Dispatch(new SetNoticeAction($"Switched to {sessions[nextIndex].Key}."));
	}

	private CliTuiSessionItem[] FilteredSessions()
		=> _stateStore.Snapshot.Sessions
			.Where(session => string.IsNullOrWhiteSpace(_stateStore.Snapshot.SessionPickerQuery)
				|| session.Key.Contains(_stateStore.Snapshot.SessionPickerQuery, StringComparison.OrdinalIgnoreCase)
				|| session.Model.Contains(_stateStore.Snapshot.SessionPickerQuery, StringComparison.OrdinalIgnoreCase))
			.ToArray();

	private CliTuiCommandItem[] FilteredCommands()
		=> _stateStore.Snapshot.Commands
			.Where(command => string.IsNullOrWhiteSpace(_stateStore.Snapshot.CommandPaletteQuery)
				|| command.Name.Contains(_stateStore.Snapshot.CommandPaletteQuery, StringComparison.OrdinalIgnoreCase)
				|| command.Description.Contains(_stateStore.Snapshot.CommandPaletteQuery, StringComparison.OrdinalIgnoreCase))
			.ToArray();

	private void ExportTranscript()
	{
		var exportDirectory = Path.Combine(_app.Workspace.ProjectRoot, ".nexus", "exports");
		Directory.CreateDirectory(exportDirectory);
		var path = Path.Combine(exportDirectory, $"transcript-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
		File.WriteAllText(path, _stateStore.Snapshot.CombinedTranscript);
		_stateStore.Dispatch(new SetNoticeAction($"Transcript exported to {path}."));
	}

	private sealed class TerminalSession : IDisposable
	{
		public TerminalSession()
		{
			Console.OutputEncoding = Encoding.UTF8;
			Console.Write("\u001b[?1049h\u001b[?25l");
		}

		public void Dispose()
		{
			Console.Write("\u001b[?25h\u001b[?1049l");
		}
	}

	private sealed class CapturingAnsiConsoleOutput : IAnsiConsoleOutput, IDisposable
	{
		private readonly StringBuilder _buffer = new();
		private readonly TextWriter _writer;

		public CapturingAnsiConsoleOutput()
		{
			_writer = new BufferWriter(_buffer);
			Width = Math.Max(80, Console.WindowWidth);
			Height = Math.Max(25, Console.WindowHeight);
		}

		public int Width { get; private set; }

		public int Height { get; private set; }

		public bool IsTerminal => true;

		public TextWriter Writer => _writer;

		public void SetEncoding(Encoding encoding)
		{
		}

		public void Dispose()
		{
			_writer.Dispose();
		}

		public string GetBufferText()
		{
			lock (_buffer)
				return _buffer.ToString();
		}

		public void Clear()
		{
			lock (_buffer)
				_buffer.Clear();
		}

		public void SetSize(int width, int height)
		{
			Width = width;
			Height = height;
		}

		private sealed class BufferWriter : StringWriter
		{
			private readonly StringBuilder _buffer;

			public BufferWriter(StringBuilder buffer)
			{
				_buffer = buffer;
			}

			public override Encoding Encoding => Encoding.UTF8;

			public override void Write(char value)
			{
				lock (_buffer)
					_buffer.Append(value);
			}

			public override void Write(string? value)
			{
				if (value is null)
					return;

				lock (_buffer)
					_buffer.Append(value);
			}

			public override void WriteLine(string? value)
			{
				lock (_buffer)
				{
					if (value is not null)
						_buffer.Append(value);
					_buffer.AppendLine();
				}
			}
		}
	}
}