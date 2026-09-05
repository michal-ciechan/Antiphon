using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// The launch-time trust gate, driven over the same delegate pair the server adapters hold
/// (snapshot-screen / write-input) rather than a live pty, so the decision logic is pinned in CI
/// instead of only in the headed canary.
///
/// The highlighted-list fixture is the live Claude 2.1.258 screen transcribed from
/// <c>C:\logs\antiphon\session-runner\7bbe0c614ab44807809f51c3a2177c4f.ansi.log</c>
/// (CARD-0390, 2026-09-05). The numbered-menu fixture is the 2026-08-16 shape CARD-0047 answered
/// with <c>"1"</c>, kept so older Claude (and any return of indexes) still takes the digit.
/// </summary>
public class ClaudeStartupTrustPromptTests
{
    /// <summary>Live 2.1.258 unnumbered confirm. Highlight default is No; marker is ASCII <c>&gt;</c>.</summary>
    private const string TrustScreen = """
        ────────────────────────────────────────────────────────────────────────────────────────────────────
         Accessing workspace:

         C:\logs\antiphon\diagnose

         Quick safety check: Is this a project you created or one you trust? (Like your own code, a well-known open source
         project, or work from your team). If not, take a moment to review what's in this folder first.

         Claude Code'll be able to read, edit, and execute files here.

         Security guide

         > No, exit
           Yes, I trust this folder

         Enter to confirm · Esc to cancel
        """;

    private const string LegacyNumberedTrustScreen = """
        ────────────────────────────────────────────────────────────
         Accessing workspace:

         C:\logs\antiphon\check-interpreter

         Quick safety check: Is this a project you created or one you trust? (Like your own code, a
         well-known open source project, or work from your team). If not, take a moment to review
         what's in this folder first.

         Claude Code'll be able to read, edit, and execute files here.

         Security guide

         ❯ 1. Yes, I trust this folder
           2. No, exit

         Enter to confirm · Esc to cancel
        """;

    private const string ReadyScreen = """
        ╭──────────────────────────────────────────────────────────╮
        │ >                                                        │
        ╰──────────────────────────────────────────────────────────╯
          ? for shortcuts
        """;

    private const string PermissionScreen = """
        ╭──────────────────────────────────────────────────────────╮
        │ Bash command                                             │
        │   rm -rf /                                               │
        │                                                          │
        │ Do you want to proceed?                                  │
        │ ❯ 1. Yes                                                 │
        │   2. No, and tell Claude what to do differently          │
        ╰──────────────────────────────────────────────────────────╯
        """;

    private const string UnrecognisedTrustScreen = """
         Accessing workspace:

         C:\logs\antiphon\diagnose

         Quick safety check: Is this a project you created or one you trust?

         Esc to cancel
        """;

    private const string HighlightYesGt = """
         Accessing workspace:
           No, exit
         > Yes, I trust this folder
         Enter to confirm
        """;

    private const string HighlightNoGt = """
         Accessing workspace:
         > No, exit
           Yes, I trust this folder
         Enter to confirm
        """;

    private const string HighlightYesDiamond = """
         Accessing workspace:
           No, exit
         ❯ Yes, I trust this folder
         Enter to confirm
        """;

    private const string HighlightNoDiamond = """
         Accessing workspace:
         ❯ No, exit
           Yes, I trust this folder
         Enter to confirm
        """;

    private const string HighlightGatedNo = """
         Accessing workspace:
         > No, continue without these permissions
           Yes, I trust this folder
         Enter to confirm
        """;

    [Test]
    public async Task The_2_1_258_dialog_is_answered_by_moving_the_highlight_then_Enter()
    {
        var screen = ScriptedScreen.HighlightedList(thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustCleared);
        result.Prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.TrustFolder);
        result.Prompt.Layout.ShouldBe(ClaudeTrustDialogLayout.HighlightedList);
        result.Detail.ShouldNotBeNull();
        result.Detail.ShouldContain("j");
        screen.Inputs.ShouldBe(["j", "\r"]);
        screen.Exited.ShouldBeFalse();
    }

    [Test]
    public async Task A_dialog_that_ignores_j_is_moved_with_Down()
    {
        var screen = ScriptedScreen.HighlightedList(thenShowing: ReadyScreen, movesOn: "\x1b[B");

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustCleared);
        screen.Inputs.ShouldBe(["j", "\x1b[B", "\r"]);
        screen.Exited.ShouldBeFalse();
    }

    [Test]
    public async Task A_dialog_whose_highlight_never_moves_gets_no_Enter()
    {
        var screen = ScriptedScreen.HighlightedList(thenShowing: ReadyScreen, movesOn: null);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustNotCleared);
        result.Answered.ShouldBeTrue();
        screen.Inputs.ShouldBe(["j", "\x1b[B", "\x0e"]);
        screen.Inputs.ShouldNotContain("\r");
        screen.Exited.ShouldBeFalse();
    }

    [Test]
    public async Task A_dialog_already_highlighting_Yes_gets_only_Enter()
    {
        var screen = ScriptedScreen.HighlightedList(thenShowing: ReadyScreen, startOnYes: true);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustCleared);
        screen.Inputs.ShouldBe(["\r"]);
        screen.Exited.ShouldBeFalse();
    }

    [Test]
    public async Task The_legacy_numbered_dialog_still_takes_the_digit()
    {
        var screen = new ScriptedScreen(LegacyNumberedTrustScreen, clearedBy: "1", thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustCleared);
        result.Prompt!.Layout.ShouldBe(ClaudeTrustDialogLayout.NumberedMenu);
        screen.Inputs.ShouldBe(["1"]);
    }

    [Test]
    public async Task An_unrecognised_trust_layout_types_nothing()
    {
        var screen = new ScriptedScreen(UnrecognisedTrustScreen, clearedBy: "1", thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustUnanswerable);
        result.Answered.ShouldBeFalse();
        result.Prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.TrustFolder);
        result.Prompt.Layout.ShouldBe(ClaudeTrustDialogLayout.Unknown);
        screen.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(HighlightYesGt, ClaudeTrustDialogHighlight.Yes)]
    [Arguments(HighlightNoGt, ClaudeTrustDialogHighlight.No)]
    [Arguments(HighlightYesDiamond, ClaudeTrustDialogHighlight.Yes)]
    [Arguments(HighlightNoDiamond, ClaudeTrustDialogHighlight.No)]
    [Arguments(HighlightGatedNo, ClaudeTrustDialogHighlight.No)]
    [Arguments(ReadyScreen, ClaudeTrustDialogHighlight.Unknown)]
    public void ReadHighlight_names_the_marked_option(string screen, ClaudeTrustDialogHighlight expected)
    {
        ClaudeBlockingPromptDetector.ReadHighlight(screen).ShouldBe(expected);
    }

    [Test]
    public void The_live_2_1_258_fixture_highlights_No()
    {
        ClaudeBlockingPromptDetector.Detect(TrustScreen)!.Layout
            .ShouldBe(ClaudeTrustDialogLayout.HighlightedList);
        ClaudeBlockingPromptDetector.ReadHighlight(TrustScreen)
            .ShouldBe(ClaudeTrustDialogHighlight.No);
    }

    [Test]
    public async Task A_healthy_screen_is_left_completely_alone()
    {
        var screen = new ScriptedScreen(ReadyScreen, clearedBy: "1", thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.None);
        result.Prompt.ShouldBeNull();
        screen.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task A_tool_permission_modal_is_reported_but_never_answered()
    {
        var screen = new ScriptedScreen(PermissionScreen, clearedBy: "1", thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.NotAnswerable);
        result.Prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.ToolPermission);
        screen.Inputs.ShouldBeEmpty(
            "auto-confirming a permission modal would grant a tool call nobody authorised");
    }

    [Test]
    public async Task A_trust_dialog_that_survives_the_answer_is_reported_not_cleared()
    {
        var screen = ScriptedScreen.HighlightedList(thenShowing: ReadyScreen, startOnYes: true, clearsOnEnter: false);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromMilliseconds(300));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustNotCleared);
        result.Answered.ShouldBeTrue();
        screen.Inputs.ShouldBe(["\r"]);
        screen.Exited.ShouldBeFalse();
    }

    /// <summary>A screen that changes only when the expected key arrives.</summary>
    private sealed class ScriptedScreen
    {
        private string _screen;
        private readonly string? _clearedBy;
        private readonly string _thenShowing;
        private readonly bool _isList;
        private ClaudeTrustDialogHighlight _highlight;
        private readonly string? _movesOn;
        private readonly bool _clearsOnEnter;
        private readonly string _noLabel = "";
        private readonly char _marker;

        public ScriptedScreen(string initial, string? clearedBy, string thenShowing)
        {
            _screen = initial;
            _clearedBy = clearedBy;
            _thenShowing = thenShowing;
        }

        private ScriptedScreen(
            string thenShowing,
            string? movesOn,
            bool startOnYes,
            bool clearsOnEnter,
            string noLabel)
        {
            _isList = true;
            _thenShowing = thenShowing;
            _movesOn = movesOn;
            _highlight = startOnYes ? ClaudeTrustDialogHighlight.Yes : ClaudeTrustDialogHighlight.No;
            _clearsOnEnter = clearsOnEnter;
            _noLabel = noLabel;
            _marker = '>';
            _screen = RenderList();
        }

        public static ScriptedScreen HighlightedList(
            string thenShowing,
            string? movesOn = "j",
            bool startOnYes = false,
            bool clearsOnEnter = true,
            string noLabel = "No, exit")
            => new(thenShowing, movesOn, startOnYes, clearsOnEnter, noLabel);

        public List<string> Inputs { get; } = [];

        public bool Exited { get; private set; }

        public Task<string> SnapshotAsync(CancellationToken ct) => Task.FromResult(_screen);

        public Task WriteAsync(string input, CancellationToken ct)
        {
            Inputs.Add(input);
            if (_isList)
            {
                if (_movesOn is not null
                    && input == _movesOn
                    && _highlight == ClaudeTrustDialogHighlight.No)
                {
                    _highlight = ClaudeTrustDialogHighlight.Yes;
                    _screen = RenderList();
                }
                else if (input == "\r")
                {
                    if (_highlight == ClaudeTrustDialogHighlight.Yes)
                    {
                        if (_clearsOnEnter)
                            _screen = _thenShowing;
                    }
                    else
                    {
                        Exited = true;
                    }
                }
                return Task.CompletedTask;
            }

            if (_clearedBy is not null && input == _clearedBy)
                _screen = _thenShowing;
            return Task.CompletedTask;
        }

        private string RenderList()
        {
            var noRow = _highlight == ClaudeTrustDialogHighlight.No
                ? $" {_marker} {_noLabel}"
                : $"   {_noLabel}";
            var yesRow = _highlight == ClaudeTrustDialogHighlight.Yes
                ? $" {_marker} Yes, I trust this folder"
                : "   Yes, I trust this folder";
            return $"""
                ────────────────────────────────────────────────────────────────────────────────────────────────────
                 Accessing workspace:

                 C:\logs\antiphon\diagnose

                 Quick safety check: Is this a project you created or one you trust? (Like your own code, a well-known open source
                 project, or work from your team). If not, take a moment to review what's in this folder first.

                 Claude Code'll be able to read, edit, and execute files here.

                 Security guide

                {noRow}
                {yesRow}

                 Enter to confirm · Esc to cancel
                """;
        }
    }
}
