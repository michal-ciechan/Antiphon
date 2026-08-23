using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0137 S1: headed canary measuring Claude's mid-life overlay contract. Esc-on-idle and
/// overlay-dismiss are unmeasured on Claude until this file goes green — the catalog stays
/// <c>Unknown</c> and S5/S6 must not act on Claude without that fact.
///
/// Idle: one Esc on an empty composer, then a body still renders. Overlay: open <c>/model</c>
/// (the plan's named Claude overlay) and assert one Esc restores the composer. Fragments
/// captured here are the only ones a proactive detector may match.
///
/// Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>), <c>[Explicit]</c>. Does not submit a model turn.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0137")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeOverlayCanaryTests
{
    private const string DismissKey = "\u001b";
    private const int SettleMs = 400;

    [Test]
    public async Task Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");

        var before = runner.SnapshotScreen();
        Console.WriteLine("IDLE SCREEN:\n" + before);

        await runner.WriteAsync(DismissKey);
        await Task.Delay(SettleMs);
        var afterEsc = runner.SnapshotScreen();
        Console.WriteLine("AFTER IDLE ESC:\n" + afterEsc);

        // Esc-Esc opens Claude's rewind/history picker. One Esc on idle must not.
        afterEsc.ShouldNotContain("rewind", Case.Insensitive);
        afterEsc.ShouldNotContain("Restore this point", Case.Insensitive);
        ComposerDeliveryEvidence.FragmentIsVisible(afterEsc, "Select model")
            .ShouldBeFalse("one idle Esc must not open the /model overlay");

        var token = "CL-ESC-" + Guid.NewGuid().ToString("N")[..8];
        await runner.WriteAsync(token);
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
            TimeSpan.FromSeconds(8)))
            .ShouldBeTrue("a body typed after idle Esc must still render. Screen:\n" + runner.SnapshotScreen());
    }

    [Test]
    public async Task One_Esc_dismisses_the_model_overlay_and_restores_the_composer()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");

        await runner.WriteAsync("/model");
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, "/model"),
            TimeSpan.FromSeconds(5)))
            .ShouldBeTrue("/model must render in the composer before Enter. Screen:\n" + runner.SnapshotScreen());
        await Task.Delay(800);
        await runner.WriteAsync("\r");
        await Task.Delay(SettleMs);

        var overlay = runner.SnapshotScreen();
        Console.WriteLine("OVERLAY SCREEN:\n" + overlay);
        Console.WriteLine(
            "FRAGMENTS: select-model="
            + ComposerDeliveryEvidence.FragmentIsVisible(overlay, "Select model")
            + " opus=" + ComposerDeliveryEvidence.FragmentIsVisible(overlay, "Opus")
            + " sonnet=" + ComposerDeliveryEvidence.FragmentIsVisible(overlay, "Sonnet")
            + " esc=" + ComposerDeliveryEvidence.FragmentIsVisible(overlay, "esc"));

        await runner.WriteAsync(DismissKey);
        await Task.Delay(SettleMs);

        var restored = runner.SnapshotScreen();
        Console.WriteLine("AFTER DISMISS ESC:\n" + restored);

        var token = "CL-RESTORE-" + Guid.NewGuid().ToString("N")[..8];
        await runner.WriteAsync(token);
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
            TimeSpan.FromSeconds(8)))
            .ShouldBeTrue(
                "one Esc must restore the composer after /model. Overlay was:\n" + overlay
                + "\nAfter Esc:\n" + restored
                + "\nNow:\n" + runner.SnapshotScreen());
    }
}
