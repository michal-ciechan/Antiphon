using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0137 S1: headed canary measuring Codex's mid-life overlay contract. Esc-on-idle is the
/// only overlay fact this file is allowed to establish — Codex <c>/usage</c> is forbidden
/// (CARD-0141: it can redeem the account's one usage-limit reset) and <c>/status</c> is
/// measured to render into scrollback with no overlay. Without a measured overlay-dismiss,
/// <c>TerminalOverlay.State</c> stays <c>Unknown</c> and S5/S6 must not act on Codex.
///
/// Headed, opt-in (<c>ANTIPHON_CODEX_HEADED_TESTS=1</c>), <c>[Explicit]</c>. Does not submit a
/// model turn.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0137")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexOverlayCanaryTests
{
    private const string DismissKey = "\u001b";
    private const int SettleMs = 400;

    [Test]
    public async Task Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders()
    {
        CxSession.SkipIfNotEligible();
        var log = CxSession.MeasurementLog(nameof(Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders));
        var cwd = CxSession.TempCwd();

        try
        {
            var (app, args) = CxSession.BuildLaunch(CxSession.ResolveCli()!);
            await using var runner = new PtyAgentRunner("modern");
            var env = CxSession.HeadedEnv();
            env["TERM"] = "xterm-256color";
            await runner.StartAsync(app, args, cwd: cwd, cols: 120, rows: 34, env: env);
            (await CxSession.WaitForComposerAsync(runner, TimeSpan.FromSeconds(60)))
                .ShouldBeTrue("the composer must render. Screen:\n" + runner.SnapshotScreen());

            var before = runner.SnapshotScreen();
            log("IDLE SCREEN:\n" + CxSession.Tail(before, 1200));

            await runner.WriteAsync(DismissKey);
            await Task.Delay(SettleMs);
            var afterEsc = runner.SnapshotScreen();
            log("AFTER IDLE ESC:\n" + CxSession.Tail(afterEsc, 1200));

            ComposerDeliveryEvidence.FragmentIsVisible(afterEsc, "Redeem usage limit reset")
                .ShouldBeFalse("one Esc on idle must not open the forbidden /usage picker. Screen:\n" + afterEsc);

            var token = "CX-ESC-" + Guid.NewGuid().ToString("N")[..8];
            await runner.WriteAsync(token);
            (await runner.WaitForScreenAsync(
                s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
                TimeSpan.FromSeconds(8)))
                .ShouldBeTrue("a body typed after idle Esc must still render. Screen:\n" + runner.SnapshotScreen());
        }
        finally
        {
            CxSession.BestEffortDelete(cwd);
        }
    }
}
