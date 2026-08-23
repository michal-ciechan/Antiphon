using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0137 S1: headed canary pinning Grok's mid-life overlay contract — Esc is a no-op on an
/// idle empty composer, and one Esc dismisses the <c>/usage</c> overlay restoring composer
/// focus. The investigation (docs/investigations/2026-08-23-card-0137-overlay-focus-normal-delivery-investigation.md
/// §3.1) measured this twice against session <c>1e4976d4</c>; this file is the CI-side alarm
/// when a TUI upgrade moves the fragments or the dismiss key.
///
/// Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>), <c>[Explicit]</c>: opens <c>/usage</c> on a
/// live SuperGrok account. Does not submit a model turn.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0137")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokUsageOverlayCanaryTests
{
    private const string DismissKey = "\u001b";
    private const int SettleMs = 400;

    [Test]
    public async Task Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            var before = runner.SnapshotScreen();
            log("IDLE SCREEN:\n" + GkSession.Tail(before, 1200));

            await runner.WriteAsync(DismissKey);
            await Task.Delay(SettleMs);
            var afterEsc = runner.SnapshotScreen();
            log("AFTER IDLE ESC:\n" + GkSession.Tail(afterEsc, 1200));

            ComposerDeliveryEvidence.FragmentIsVisible(afterEsc, "Esc close")
                .ShouldBeFalse("one Esc on an idle composer must not open an overlay. Screen:\n" + afterEsc);

            var token = "GK-ESC-" + Guid.NewGuid().ToString("N")[..8];
            await runner.WriteAsync(token);
            (await runner.WaitForScreenAsync(
                s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
                TimeSpan.FromSeconds(8)))
                .ShouldBeTrue("a body typed after idle Esc must still render. Screen:\n" + runner.SnapshotScreen());
        }
        finally
        {
            GkSession.BestEffortDelete(cwd);
        }
    }

    [Test]
    public async Task One_Esc_dismisses_the_usage_overlay_and_restores_the_composer()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(One_Esc_dismisses_the_usage_overlay_and_restores_the_composer));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            await runner.WriteAsync("/usage");
            (await runner.WaitForScreenAsync(
                s => ComposerDeliveryEvidence.FragmentIsVisible(s, "/usage"),
                TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("/usage must render in the composer before Enter. Screen:\n" + runner.SnapshotScreen());
            await Task.Delay(200);
            await runner.WriteAsync("\r");

            (await runner.WaitForScreenAsync(
                s => ComposerDeliveryEvidence.FragmentIsVisible(s, "c copy session ID"),
                TimeSpan.FromSeconds(15)))
                .ShouldBeTrue(
                    "/usage must open the measured overlay (not the idle 'Weekly limit' status bar). Screen:\n"
                    + runner.SnapshotScreen());

            var overlay = runner.SnapshotScreen();
            log("OVERLAY SCREEN:\n" + GkSession.Tail(overlay, 1600));
            log("FRAGMENTS: copy-session-id="
                + ComposerDeliveryEvidence.FragmentIsVisible(overlay, "c copy session ID")
                + " weekly-limit=" + ComposerDeliveryEvidence.FragmentIsVisible(overlay, "Weekly limit")
                + " esc-close=" + ComposerDeliveryEvidence.FragmentIsVisible(overlay, "Esc close"));

            await runner.WriteAsync(DismissKey);
            await Task.Delay(SettleMs);

            var restored = runner.SnapshotScreen();
            log("AFTER DISMISS ESC:\n" + GkSession.Tail(restored, 1200));
            ComposerDeliveryEvidence.FragmentIsVisible(restored, "c copy session ID")
                .ShouldBeFalse("one Esc must close the overlay. Screen:\n" + restored);

            var token = "GK-RESTORE-" + Guid.NewGuid().ToString("N")[..8];
            await runner.WriteAsync(token);
            (await runner.WaitForScreenAsync(
                s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
                TimeSpan.FromSeconds(8)))
                .ShouldBeTrue("composer focus must be restored after one Esc. Screen:\n" + runner.SnapshotScreen());
        }
        finally
        {
            GkSession.BestEffortDelete(cwd);
        }
    }
}
