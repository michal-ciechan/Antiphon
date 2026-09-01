using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0292 S6: headed canary pinning the real Claude /remote-control MANAGEMENT MENU literals
/// the matcher anchors on, and that one Esc dismisses it. Needs a session whose bridge is already
/// live — otherwise <c>/remote-control</c> arms instead of opening the menu, and we skip.
///
/// Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>), <c>[Explicit]</c>. Operator-run, like
/// <see cref="ClaudeTrustPromptCanaryTests"/>. Does not submit a model turn.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeRemoteControlMenuCanaryTests
{
    // Independent of RemoteControlMenuScreen — this is the measurement the matcher is built on.
    private const string DisconnectLiteral = "Disconnect this session";
    private const string FooterLiteral = "Esc to continue";
    private const string DismissKey = "\u001b";
    private const int SettleMs = 400;

    private static bool MenuPresent(string screen) =>
        screen.Contains(DisconnectLiteral, StringComparison.Ordinal)
        && screen.Contains(FooterLiteral, StringComparison.Ordinal);

    [Test]
    public async Task One_Esc_dismisses_the_remote_control_management_menu()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");

        await runner.WriteAsync("/remote-control");
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, "/remote-control"),
            TimeSpan.FromSeconds(8)))
            .ShouldBeTrue("/remote-control must render in the composer before Enter. Screen:\n"
                + runner.SnapshotScreen());
        await Task.Delay(800);
        await runner.WriteAsync("\r");

        var appeared = await runner.WaitForScreenAsync(MenuPresent, TimeSpan.FromSeconds(8));
        if (!appeared)
        {
            throw new SkipTestException(
                "no management menu appeared — this session was not already bridged, so "
                + "/remote-control armed instead of opening Disconnect/Continue. Screen:\n"
                + runner.SnapshotScreen());
        }

        var menu = runner.SnapshotScreen();
        Console.WriteLine("RC MENU SCREEN:\n" + menu);
        menu.ShouldContain(DisconnectLiteral);
        menu.ShouldContain(FooterLiteral);

        await runner.WriteAsync(DismissKey);
        await Task.Delay(SettleMs);

        var after = runner.SnapshotScreen();
        Console.WriteLine("AFTER MENU ESC:\n" + after);
        MenuPresent(after).ShouldBeFalse("one Esc must dismiss the menu. After:\n" + after);

        var token = "RC-ESC-" + Guid.NewGuid().ToString("N")[..8];
        await runner.WriteAsync(token);
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
            TimeSpan.FromSeconds(8)))
            .ShouldBeTrue("a body typed after dismissing the menu must render. Screen:\n"
                + runner.SnapshotScreen());
    }
}
