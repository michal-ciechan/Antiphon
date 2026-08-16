using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// The launch-time trust gate, driven over the same delegate pair the server adapters hold
/// (snapshot-screen / write-input) rather than a live pty, so the decision logic is pinned in CI
/// instead of only in the headed canary.
///
/// The screens are the REAL ones: the trust text is transcribed from
/// <c>C:\logs\antiphon\session-runner\98ffd322….ansi.log</c>, the session CARD-0047's standing check
/// interpreter died on (2026-08-16).
/// </summary>
public class ClaudeStartupTrustPromptTests
{
    private const string TrustScreen = """
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

    [Test]
    public async Task A_trust_dialog_is_answered_with_the_digit_and_reported_cleared()
    {
        var screen = new ScriptedScreen(TrustScreen, clearedBy: "1", thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustCleared);
        result.Prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.TrustFolder);
        // The DIGIT, not Enter: Enter accepts whatever is highlighted, which after any stray key is
        // not necessarily "Yes, I trust this folder".
        screen.Inputs.ShouldBe(["1"]);
    }

    [Test]
    public async Task A_healthy_screen_is_left_completely_alone()
    {
        var screen = new ScriptedScreen(ReadyScreen, clearedBy: "1", thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromSeconds(2));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.None);
        result.Prompt.ShouldBeNull();
        // A gate that types into a session which was never blocked is worse than no gate at all.
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
        // The failure the caller must be able to see: we typed, and the TUI is still blocked. Saying
        // "ready" here is what produced the kill/restart loop in the first place.
        var screen = new ScriptedScreen(TrustScreen, clearedBy: null, thenShowing: ReadyScreen);

        var result = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            screen.SnapshotAsync, screen.WriteAsync, TimeSpan.FromMilliseconds(300));

        result.Outcome.ShouldBe(ClaudeStartupBlockOutcome.TrustNotCleared);
        result.Answered.ShouldBeTrue();
    }

    /// <summary>A screen that changes only when the expected key arrives.</summary>
    private sealed class ScriptedScreen(string initial, string? clearedBy, string thenShowing)
    {
        private string _screen = initial;

        public List<string> Inputs { get; } = [];

        public Task<string> SnapshotAsync(CancellationToken ct) => Task.FromResult(_screen);

        public Task WriteAsync(string input, CancellationToken ct)
        {
            Inputs.Add(input);
            if (clearedBy is not null && input == clearedBy)
                _screen = thenShowing;
            return Task.CompletedTask;
        }
    }
}
