using Antiphon.TestSupport.LiveClaude;
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
[Category("OptIn")]
[Category("HeadedCanary")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeRemoteControlMenuCanaryTests
{
    // Independent of RemoteControlMenuScreen — this is the measurement the matcher is built on.
    private const string DisconnectLiteral = "Disconnect this session";
    private const string FooterLiteral = "Esc to continue";
    private const string OneEscTest = "One_Esc_dismisses_the_remote_control_management_menu";
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
        if (!ready)
            throw new SkipTestException(LiveClaudeEvidence.Skip(
                "menu-ready-skip", OneEscTest, null, runner.SnapshotScreen(), null, null));

        await runner.WriteAsync("/remote-control");
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, "/remote-control"),
            TimeSpan.FromSeconds(8)))
            .ShouldBeTrue(LiveClaudeEvidence.Assertion(
                "menu-composer", OneEscTest, null, runner.SnapshotScreen(), null, null));
        await Task.Delay(800);
        await runner.WriteAsync("\r");

        var appeared = await runner.WaitForScreenAsync(MenuPresent, TimeSpan.FromSeconds(8));
        if (!appeared)
        {
            throw new SkipTestException(LiveClaudeEvidence.Skip(
                "menu-absent-skip", OneEscTest, null, runner.SnapshotScreen(), null, null));
        }

        var menu = runner.SnapshotScreen();
        LiveClaudeEvidence.Success("menu-screen", OneEscTest, null, menu, null, null);
        menu.Contains(DisconnectLiteral, StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue(LiveClaudeEvidence.Assertion("menu-disconnect", OneEscTest, null, menu, null, null));
        menu.Contains(FooterLiteral, StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue(LiveClaudeEvidence.Assertion("menu-footer", OneEscTest, null, menu, null, null));

        await runner.WriteAsync(DismissKey);
        await Task.Delay(SettleMs);

        var after = runner.SnapshotScreen();
        LiveClaudeEvidence.Success("menu-after", OneEscTest, null, after, null, null);
        MenuPresent(after).ShouldBeFalse(LiveClaudeEvidence.Assertion(
            "menu-dismissed", OneEscTest, null, after, null, null));

        var token = "RC-ESC-" + Guid.NewGuid().ToString("N")[..8];
        await runner.WriteAsync(token);
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
            TimeSpan.FromSeconds(8)))
            .ShouldBeTrue(LiveClaudeEvidence.Assertion(
                "menu-body", OneEscTest, null, runner.SnapshotScreen(), null, null));
    }

    [Test]
    public async Task C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt()
    {
        var executable = ClSession.RequireCore(LiveClaudeCore.Menu);
        using var profile = ClSession.RequireProfile(executable);
        ClSession.SkipIfNotEligible(executable);
        var sessionId = Guid.NewGuid().ToString("D");
        await using var runner = new PtyAgentRunner("modern");
        var launch = ClSession.BuildProfileLaunch(profile, "menu", sessionId);
        var (app, args) = ClSession.BuildCoreLaunch(executable, launch.Args);
        await runner.StartAsync(app, args, cwd: launch.Cwd, cols: launch.Cols, rows: launch.Rows, env: launch.Env);
        executable.RequireNativeObservation();
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready)
            throw new SkipTestException("pending native acceptance: real Claude TUI did not reach ready");

        await ClaudeRemoteControlAtStartupCanaryTests.EnsureMenuBridgeAsync(runner, profile, sessionId);
        var delivery = new LiveClaudeDeliverySession(runner, profile, sessionId);
        var bridge = delivery.Bridge() ?? throw new LiveClaudeRefusal("bridge-unestablished");
        await delivery.SendAsync("/remote-control");
        var appeared = await runner.WaitForScreenAsync(MenuPresent, TimeSpan.FromSeconds(12));
        if (!appeared) throw new LiveClaudeRefusal("menu-unobserved");
        var beforeEsc = delivery.Bridge();
        await runner.WriteAsync(DismissKey);
        await Task.Delay(SettleMs);
        var firstClear = !MenuPresent(runner.SnapshotScreen());
        await Task.Delay(SettleMs);
        var secondClear = !MenuPresent(runner.SnapshotScreen());
        if (!firstClear || !secondClear) throw new LiveClaudeRefusal("menu-not-dismissed");
        var afterEsc = delivery.Bridge();
        var body = ClaudeSubmitContractHarness.PromptFor("claude");
        var request = delivery.Capture(body);
        var rows = await LiveClaudeDeliveryEvidence.SubmitOnceAsync(request, () => delivery.SendAsync(body),
            () => delivery.ObserveAsync(request, completion: false), completion: false);
        var afterSubmit = delivery.Bridge();
        LiveClaudeDeliveryEvidence.RequireMenu(request, rows, bridge, beforeEsc, afterEsc, afterSubmit);
    }
}
