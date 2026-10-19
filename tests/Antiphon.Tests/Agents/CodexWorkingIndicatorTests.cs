using Antiphon.Agents.Pty;
using Antiphon.Agents.Pty.Tests;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0108 S2: the measured strings the Codex screen fallback now hangs on. The headed
/// <c>CodexDoneDetectionCanaryTests</c> proves the real TUI still renders them; this proves the
/// matcher reads them the way that canary measures, without spending a model turn.
/// </summary>
[Category("Unit")]
public class CodexWorkingIndicatorTests
{
    [Test]
    public void C907_submitted_frame_is_working()
    {
        CodexWorkingIndicator.IsVisible(CodexStartupFixtures.V0160Running)
            .ShouldBeTrue("C907 captured 0.160 Working row");
    }

    [Test]
    public void C907_status_words_outside_the_status_row_are_not_working()
    {
        // Derived incomplete/prose inputs, not new observed status grammar. The existing
        // predicate requires both exact substrings on one row; it does not parse row location.
        foreach (var (label, screen) in new[]
        {
            ("idle", CodexStartupFixtures.V0160Idle),
            ("done", CodexStartupFixtures.V0160Done),
            ("answer prose", "Working on a reply. Press escape to interrupt it."),
            ("draft", "› Working (on a draft)"),
            ("help", "Press esc to interrupt) a running task"),
            ("partial", "◦ Working (1s • esc to inter"),
            ("split", "◦ Working (1s\n• esc to interrupt)"),
            ("MCP spinner", "⠧ Starting MCP servers (1/3): test")
        })
            CodexWorkingIndicator.IsVisible(screen).ShouldBeFalse("C907 " + label);

        var terminal = new TerminalScreen(120, 30);
        terminal.Feed("\x1b]0;Working (1s • esc to interrupt)\a");
        CodexWorkingIndicator.IsVisible(terminal.GetScreenText())
            .ShouldBeFalse("C907 OSC title is not a rendered status row");
    }

    [Test]
    public void C907_captured_lifecycle_requires_seen_gone_and_settle()
    {
        var t0 = new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
        var working = CodexStartupFixtures.V0160Running;
        var done = CodexStartupFixtures.V0160Done;
        var never = new CodexTurnScreenTracker(TimeSpan.FromSeconds(1));
        never.Observe(done, done, 1, t0).ShouldBeFalse("C907 never seen initial");
        never.Observe(done, done, 1, t0.AddMinutes(1)).ShouldBeFalse("C907 never seen quiet");

        var tracker = new CodexTurnScreenTracker(TimeSpan.FromSeconds(1));
        tracker.Observe(working, working, 1, t0).ShouldBeFalse("C907 seen");
        tracker.Observe(working, working, 1, t0.AddSeconds(2)).ShouldBeFalse("C907 still visible");
        tracker.Observe(done, done, 2, t0.AddSeconds(3)).ShouldBeFalse("C907 gone repaint");
        tracker.Observe(done, done, 2, t0.AddMilliseconds(3999)).ShouldBeFalse("C907 settle minus one");
        tracker.Observe(done, done, 2, t0.AddSeconds(4)).ShouldBeTrue("C907 whole quiet interval");

        var repaint = new CodexTurnScreenTracker(TimeSpan.FromSeconds(1));
        repaint.Observe(working, working, 1, t0).ShouldBeFalse();
        repaint.Observe(done, done, 2, t0.AddSeconds(1)).ShouldBeFalse();
        repaint.Observe(done, done, 3, t0.AddMilliseconds(1999)).ShouldBeFalse("C907 late repaint");
        repaint.Observe(done, done, 3, t0.AddSeconds(2)).ShouldBeFalse("C907 old deadline invalid");
        repaint.Observe(done, done, 3, t0.AddMilliseconds(2999)).ShouldBeTrue("C907 new deadline");
        foreach (var raw in new[] { "", "\x1b[0m" })
        {
            var empty = new CodexTurnScreenTracker(TimeSpan.FromSeconds(1));
            empty.Observe(working, raw, 1, t0).ShouldBeFalse();
            empty.Observe(done, raw, 1, t0.AddSeconds(2)).ShouldBeFalse("C907 no visible raw output");
        }
    }

    [Test]
    public void The_measured_working_line_is_visible()
    {
        // Verbatim shape from the 2026-08-20 probe: bullet, elapsed seconds, esc hint.
        CodexWorkingIndicator.IsVisible(
            "  codex\n  thinking about it\n\n• Working (12s • esc to interrupt)\n  > \n")
            .ShouldBeTrue();
    }

    [Test]
    public void A_completed_turns_screen_is_not_working()
    {
        CodexWorkingIndicator.IsVisible(
            "  codex\n  PONG\n\n  > \n  gpt-5.6-luna low\n").ShouldBeFalse();
    }

    [Test]
    public void Half_the_line_is_not_the_line()
    {
        // Either half alone is text a model could put in its own answer, which is why both are
        // required — and required on ONE row, so an answer mentioning one and a hint mentioning
        // the other cannot combine into a false live turn.
        CodexWorkingIndicator.IsVisible("I am Working (on it)").ShouldBeFalse();
        CodexWorkingIndicator.IsVisible("press esc to interrupt)").ShouldBeFalse();
        CodexWorkingIndicator.IsVisible("Working (\nesc to interrupt)").ShouldBeFalse();
    }

    [Test]
    public void Nothing_on_an_empty_or_null_screen()
    {
        CodexWorkingIndicator.IsVisible(null).ShouldBeFalse();
        CodexWorkingIndicator.IsVisible("").ShouldBeFalse();
    }

    [Test]
    public void The_tracker_needs_the_whole_lifecycle_not_merely_quiet()
    {
        var tracker = new CodexTurnScreenTracker(TimeSpan.FromSeconds(3));
        var t0 = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

        // A silent, never-working session: quiet for a full minute is still not done. This is the
        // stranded-composer shape, and the old bare-quiet rule called it complete at 3s.
        tracker.Observe("  > the prompt nobody submitted\n", "codex ready\n", 7, t0).ShouldBeFalse();
        tracker.Observe("  > the prompt nobody submitted\n", "codex ready\n", 7, t0.AddMinutes(1))
            .ShouldBeFalse();
        tracker.IndicatorSeen.ShouldBeFalse();
    }

    [Test]
    public void The_tracker_completes_once_the_indicator_has_come_and_gone_and_output_settled()
    {
        var tracker = new CodexTurnScreenTracker(TimeSpan.FromSeconds(3));
        var t0 = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        const string working = "• Working (3s • esc to interrupt)\n";
        const string idle = "  PONG\n  > \n";

        tracker.Observe(working, "out", 1, t0).ShouldBeFalse();
        tracker.Observe(working, "out", 2, t0.AddSeconds(1)).ShouldBeFalse();
        tracker.IndicatorSeen.ShouldBeTrue();

        // Indicator gone, but the screen is still repainting: not yet.
        tracker.Observe(idle, "out", 3, t0.AddSeconds(2)).ShouldBeFalse();
        tracker.IndicatorGone.ShouldBeTrue();
        tracker.Observe(idle, "out", 3, t0.AddSeconds(4)).ShouldBeFalse();

        // Quiet has now run its course over a settled mark.
        tracker.Observe(idle, "out", 3, t0.AddSeconds(5.1)).ShouldBeTrue();
    }

    [Test]
    public void An_empty_snapshot_never_completes_even_with_the_full_lifecycle()
    {
        // CARD-0052 stays in force: nothing visible from the child means nothing to read back.
        var tracker = new CodexTurnScreenTracker(TimeSpan.FromSeconds(1));
        var t0 = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

        tracker.Observe("• Working (1s • esc to interrupt)\n", "", 1, t0).ShouldBeFalse();
        tracker.Observe("", "", 1, t0.AddSeconds(5)).ShouldBeFalse();
    }
}
