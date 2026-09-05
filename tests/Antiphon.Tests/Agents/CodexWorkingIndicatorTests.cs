using Antiphon.Agents.Pty;
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
