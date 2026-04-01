# Nexus CLI TUI Plan

## Goal

Make `Nexus.Cli` feel like a modern, full-screen coding agent terminal in the same class as Claude Code's CLI, while keeping the UX robust under resize, streaming output, long-running tools, and cross-platform terminal differences.

## Short Answer

Yes, this is achievable with `Spectre.Console`, but not by treating it as a complete TUI framework.

`Spectre.Console` is strong at rendering polished terminal content: layouts, panels, tables, markup, progress, and live updates. It is not a full retained-mode terminal application runtime. Its own docs explicitly warn that `LiveDisplay`, `Progress`, and `Status` are not thread safe and should not be mixed with other interactive components. That means a Claude Code–level full-screen UX requires a single UI runtime above Spectre that owns rendering, input, resize handling, and state transitions.

## 2026 Best-Practice Conclusions

### 1. Use the terminal's alternate screen buffer for full-screen mode

Best practice in 2026 is still to enter full-screen apps using VT sequences rather than painting over the shell scrollback. On Windows, this requires VT mode to be enabled and works with the documented alternate screen buffer sequence `ESC[?1049h`, then restored with `ESC[?1049l`.

Implication for Nexus:

- Full-screen chat mode should run in the alternate buffer.
- On exit, crash, or Ctrl+C, always restore the main buffer and cursor visibility.
- Never leave the user with a damaged terminal state.

### 2. Do not resize the user's terminal window

Modern CLIs generally react to terminal size changes; they do not force the terminal to a specific size.

Implication for Nexus:

- Detect width and height changes.
- Recompute layout when size changes.
- If the terminal is too small, show a deliberate degraded layout or a "window too small" screen.

### 3. Centralize all rendering through one render loop

The reliable pattern for terminal apps in 2026 is a single owner of stdout and a single render loop. Ad-hoc writes from background tasks are what usually cause tearing, cursor drift, double prompts, and repaint bugs.

Implication for Nexus:

- No `Console.Write*` or direct `IAnsiConsole` writes from session logic.
- Background work publishes state changes and events.
- The UI thread renders from immutable or snapshot state.
- Spectre should be used as a renderer, not as the app's concurrency model.

### 4. Treat resize as a first-class event

Resize bugs are usually not "layout bugs"; they are event-ordering bugs between input, streaming output, and repaint.

Implication for Nexus:

- Introduce explicit `TerminalResized(width, height)` events.
- Debounce resize bursts.
- Force a full re-layout and repaint after resize.
- Preserve logical cursor and scroll position across resize.

### 5. Separate input state from rendered output state

The prompt/input line, chat transcript, side panels, and tool activity area should be separate state slices.

Implication for Nexus:

- The input box must not be part of the transcript text stream.
- Streaming tokens append to a message model, not directly to the terminal.
- Tool logs belong in a dedicated pane or expandable event stream.

### 6. Prefer diff-based redraws, but keep full redraw as a safe fallback

The best terminal apps minimize output churn. In practice, that means comparing the last frame with the next frame and only redrawing changed lines. But a full redraw fallback is still necessary after terminal corruption, resize, or alt-buffer re-entry.

Implication for Nexus:

- Start with frame-based redraws and line diffing.
- Add a `ForceFullRedraw` path.
- Keep paint frequency capped.

### 7. Cap refresh rates and coalesce streaming updates

Token streaming can easily outpace readable repaint frequency.

Implication for Nexus:

- Buffer token chunks into message state.
- Render at a bounded cadence, for example 15-30 FPS while active and slower when idle.
- Coalesce bursts of agent/tool events into one frame.

### 8. Design for degraded terminals

Not every host supports every feature equally well.

Implication for Nexus:

- Detect ANSI/VT capability, color depth, Unicode support, input redirection, and output redirection.
- Fall back to a line-oriented mode when full-screen mode is unavailable.
- Keep a non-TUI path for CI, pipes, and scripted use.

### 9. Accessibility still matters in terminal apps

Best practice is high-contrast defaults, semantic status text, restrained animation, and keyboard-only workflows.

Implication for Nexus:

- No color-only meaning.
- Motion can be disabled.
- Every key action should have a discoverable shortcut.
- Small terminal fallback should remain usable.

### 10. Test terminal behavior, not just business logic

TUI regressions often hide outside normal unit tests.

Implication for Nexus:

- Snapshot-test frames for representative states.
- Unit-test reducers and layout selection.
- Add integration tests around resize, streaming, cancel, and recovery.
- If practical, add PTY-based end-to-end tests later.

## Library Decision

## Can Spectre.Console do it?

Yes, with constraints.

What Spectre is good at:

- High-quality rendering primitives
- Layout composition
- Rich text, tables, panels, trees, rules, status visuals
- A polished aesthetic with relatively little code

What Spectre is not giving us out of the box:

- A full application event loop
- Structured focus management
- Advanced input widgets and editing behavior
- Thread-safe concurrent live regions
- A complete retained-mode windowing system

## Should Nexus switch to Terminal.Gui instead?

Not as the default direction.

Why not:

- `Terminal.Gui` is stronger as a traditional TUI toolkit with widgets and event loop support.
- But the current stable `v1` line is in maintenance mode, and `v2` is still the future branch.
- Nexus already depends on Spectre and already has a CLI style built on it.
- Claude Code's style is closer to a custom terminal runtime with selective rendering than a classic desktop-like widget toolkit.

Recommendation:

- Stay on `Spectre.Console`.
- Build a small Nexus TUI runtime around it.
- Only run a `Terminal.Gui` spike if Spectre-based editing, focus, or scrolling becomes the main bottleneck after Phase 1.

## Recommended Architecture

## 1. Add a terminal runtime layer

Create a thin runtime owned by the CLI app.

Suggested components:

- `TerminalSession`: enters alt screen, configures cursor visibility, restores terminal on exit
- `TerminalCapabilities`: detects VT, Unicode, color depth, redirection, window size support
- `TuiHost`: owns the main loop, render cadence, input loop, and shutdown
- `TuiStateStore`: central app state and reducer/dispatcher
- `FrameRenderer`: converts state into a Spectre render tree or line frame
- `TerminalOutput`: writes frames, performs diff redraws, handles full redraw fallback

## 2. Use unidirectional state flow

Recommended model:

- Background agent/session logic emits events
- Reducers apply events into `TuiState`
- Render loop reads the latest stable snapshot
- Input dispatches intent actions, not direct rendering calls

Core state slices:

- active session metadata
- transcript model
- composer/input buffer
- tool activity stream
- session list/sidebar
- status/footer
- modal state
- terminal size and capability state

## 3. Split the UI into stable regions

Initial full-screen layout:

- Header: app name, provider, active model, workspace
- Left sidebar: chats, agents, or sessions
- Main transcript pane: conversation and tool summaries
- Optional right pane: tool events, context, or session details
- Bottom composer: multiline input, mode hints, token/tool status
- Footer: shortcuts, connection status, transient notices

For smaller terminals:

- hide right pane first
- collapse sidebar second
- preserve transcript and composer at all costs

## 4. Keep one live surface

Because Spectre live rendering is not thread safe and should not be combined casually with prompts/progress/status, Nexus should expose exactly one full-screen live surface in TUI mode.

That means:

- no nested live displays
- no ad-hoc `Status()` during TUI mode
- no prompt APIs once the TUI loop is active
- all spinners/progress bars must be rendered as part of the unified frame

## 5. Build a real input editor, not a line reader

The current CLI still centers around line input. A serious TUI needs an editor model.

Minimum editor capabilities:

- insert/delete
- left/right/home/end navigation
- multiline compose
- paste handling
- history recall
- submit vs newline behavior
- cancel/escape behavior
- predictable cursor placement after resize

Phase 2 editor capabilities:

- word navigation
- selection
- undo/redo
- slash-command completion
- session switcher and fuzzy navigation

## 6. Model streaming output explicitly

Streaming assistant output should be appended to a logical message buffer and rendered from state.

Rules:

- chunk arrival never writes to terminal directly
- chunk arrival only updates message state
- the UI loop decides when to repaint
- a completed message can be normalized or rewrapped after final token arrival

## 7. Handle tool output differently from assistant prose

Claude Code feels usable because tool activity is visible but does not drown the transcript.

Recommended treatment:

- show high-level tool cards in the transcript
- stream detailed tool logs into a side pane or expandable area
- distinguish `running`, `succeeded`, `failed`, `cancelled`
- allow collapsing noisy output

## Implementation Phases

## Phase 0: Design Spike

Goal:

- Prove that Spectre plus a custom runtime can deliver a stable full-screen app.

Deliverables:

- alternate-screen entry and restoration
- unified render loop
- static multi-pane layout
- basic resize detection
- basic input editor
- fake streaming transcript

Exit criteria:

- no terminal corruption after exit
- no duplicate prompt lines after resize
- no flicker severe enough to distract during streaming

## Phase 1: Runtime Foundation

Build:

- `TuiHost`, `TerminalSession`, `TerminalCapabilities`
- frame model and frame diffing
- resize event handling and full redraw fallback
- central dispatcher/state store

Tests:

- terminal capability detection
- reducer tests
- frame diff tests
- resize behavior tests

## Phase 2: Core Chat UX

Build:

- transcript pane
- session sidebar
- composer/editor
- footer help and status
- streaming assistant messages
- cancel handling

Tests:

- transcript wrapping across widths
- editor cursor movement
- submit/newline behavior
- session switching

## Phase 3: Tooling UX

Build:

- tool activity pane
- event grouping
- live progress and status inside the unified frame
- error surfaces and retry affordances

Tests:

- concurrent tool events
- failed tool rendering
- long output truncation and expansion

## Phase 4: Advanced Ergonomics

Build:

- fuzzy session picker
- slash-command palette
- viewport scrolling
- message jump/search
- copy-friendly transcript export
- theme tuning

Tests:

- keyboard navigation matrix
- viewport persistence after resize
- search and filter behavior

## Phase 5: Polishing and Fallback Modes

Build:

- reduced-motion mode
- low-width layout fallbacks
- plain line-mode fallback for redirected environments
- crash-safe cleanup and recovery

Tests:

- redirected stdin/stdout
- tiny terminal sizes
- abrupt cancellation and Ctrl+C cleanup

## Practical Rules For Nexus.Cli

These rules should be treated as non-negotiable if the goal is a serious TUI.

1. Only one component owns terminal output in TUI mode.
2. Session logic never writes directly to the console.
3. All UI state updates are serialized through one dispatcher.
4. Resize always triggers layout recomputation.
5. Full redraw must always be available as a recovery path.
6. Alternate screen and cursor visibility must be restored in `finally` blocks.
7. TUI mode must have a clean fallback to line mode.

## Proposed First Deliverable

Build a `--tui` mode in `Nexus.Cli` behind a feature flag before replacing the default interactive mode.

Why:

- It reduces migration risk.
- It allows A/B comparison with the current CLI.
- It gives room to prove resize, repaint, and streaming correctness before committing fully.

## Proposed Success Criteria

Nexus CLI should be considered ready to replace the current interactive loop when it can do all of the following reliably:

- open in full-screen alternate buffer
- survive repeated terminal resizes without visual corruption
- stream assistant output smoothly
- show tool activity without transcript spam
- support keyboard-only navigation and editing
- restore the terminal cleanly on exit, failure, and Ctrl+C
- fall back automatically when full-screen mode is not viable

## Recommendation

Proceed with a Spectre-based TUI runtime, not a library swap.

This gives Nexus the best chance of matching or beating Claude Code's CLI aesthetic while keeping the implementation aligned with the current codebase. The real work is not choosing prettier widgets; it is enforcing a proper terminal architecture: one render loop, one input model, one state store, one cleanup path.

## Source Notes

This plan is based on the current Nexus CLI structure and these public references reviewed on 2026-04-01:

- Spectre.Console documentation for `LiveDisplay`, `Layout`, `Status`, and `Progress`
- Spectre.Console project documentation and repository metadata
- Microsoft documentation for `System.Console`
- Microsoft Windows console virtual terminal sequence documentation, including alternate screen buffer, cursor visibility, scrolling margins, and VT input/output modes
- Terminal.Gui NuGet/documentation overview, including the note that `v1` is stable but in maintenance mode while `v2` remains the future direction