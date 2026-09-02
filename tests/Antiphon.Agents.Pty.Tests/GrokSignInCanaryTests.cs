using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0324 S4: headed canary that the live Grok 1.0.13 sign-in screen still matches
/// <see cref="GrokSignInPromptDetector"/>. Opens a browser tab to <c>auth.x.ai</c> on the
/// host — kill the process, never approve the device login.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0324")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokSignInCanaryTests
{
    [Test]
    public async Task Detector_matches_the_live_sign_in_screen_within_10s()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Detector_matches_the_live_sign_in_screen_within_10s));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var freshHome = Directory.CreateTempSubdirectory("antiphon-grok-signin-home").FullName;
        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath,
                GkSession.LaunchArgs(sessionId),
                cwd: cwd,
                env: new Dictionary<string, string>
                {
                    ["GROK_HOME"] = freshHome,
                    ["GROK_DISABLE_AUTOUPDATER"] = "1",
                },
                cols: 120,
                rows: 30);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            string? screen = null;
            var matched = false;
            while (DateTime.UtcNow < deadline)
            {
                screen = runner.SnapshotScreen();
                if (GrokSignInPromptDetector.IsVisibleOnScreen(screen))
                {
                    matched = true;
                    break;
                }

                if (runner.Exited.IsCompleted)
                    break;
                await Task.Delay(200);
            }

            log("SIGN-IN SCREEN:\n" + GkSession.Tail(screen ?? runner.SnapshotScreen(), 2000));
            matched.ShouldBeTrue(
                "an unauthenticated GROK_HOME must paint the OAuth device-approval / welcome "
                + "screen within 10s so the launch detector can fail-fast. Screen:\n"
                + (screen ?? runner.SnapshotScreen()));

            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            GkSession.BestEffortDelete(freshHome);
            GkSession.BestEffortDelete(cwd);
        }
    }
}
