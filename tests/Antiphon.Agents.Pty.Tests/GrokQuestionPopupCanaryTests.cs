using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0241 S4: headed canary that measures Grok's <c>ask_user_question</c> popup chrome so
/// <c>GrokQuestionPopup</c> can grow two independent literals. The 2026-08-23 incident did not
/// capture a pty screen (CARD-0159 §9). Until this canary lands those literals, the matcher is
/// withhold-Esc-only (always-false).
///
/// Pins: popup present after <c>ask_user_question</c>; <c>/usage</c> does not match; one typed
/// option + Enter clears it; Esc is not the answer path.
///
/// Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>), <c>[Explicit]</c>: spends a real Grok turn.
/// Keep the two literals here in lockstep with
/// <c>server/Application/Services/GrokQuestionPopup.cs</c>.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0241")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokQuestionPopupCanaryTests
{
    // Empty until the first headed run pastes SnapshotScreen chrome here AND into GrokQuestionPopup.
    private const string HeadingLiteral = "";
    private const string FooterLiteral = "";

    private static bool PopupPresent(string? screen) =>
        !string.IsNullOrEmpty(screen)
        && HeadingLiteral.Length > 0
        && FooterLiteral.Length > 0
        && screen.Contains(HeadingLiteral, StringComparison.Ordinal)
        && screen.Contains(FooterLiteral, StringComparison.Ordinal);

    [Test]
    public async Task Popup_present_after_ask_user_question_usage_does_not_match_answer_clears_esc_does_not()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(
            nameof(Popup_present_after_ask_user_question_usage_does_not_match_answer_clears_esc_does_not));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            await runner.WriteAsync("/usage");
            await Task.Delay(50);
            await runner.WriteAsync("\r");
            await Task.Delay(800);
            var usageScreen = runner.SnapshotScreen();
            log("USAGE SCREEN:\n" + GkSession.Tail(usageScreen, 1200));
            PopupPresent(usageScreen).ShouldBeFalse("/usage must not match the question-popup matcher");
            await runner.WriteAsync("\u001b");
            await Task.Delay(400);

            await runner.WriteAsync(
                "Call ask_user_question now with exactly two options: "
                + "\"Proceed as planned (Recommended)\" and \"Hold - I have a change\". "
                + "Do not continue until I answer.");
            await Task.Delay(50);
            await runner.WriteAsync("\r");

            var tool = await WaitForAskUserQuestionAsync(updates, TimeSpan.FromMinutes(2));
            tool.ShouldNotBeNull(
                "the turn must open ask_user_question. Screen:\n" + runner.SnapshotScreen());
            await Task.Delay(800);
            var popupScreen = runner.SnapshotScreen();
            log("QUESTION POPUP SCREEN:\n" + popupScreen);

            if (HeadingLiteral.Length == 0 || FooterLiteral.Length == 0)
            {
                throw new SkipTestException(
                    "GrokQuestionPopup literals are unmeasured. Paste two independent chrome "
                    + "strings from the dump in TestOutput/GrokCanary into GrokQuestionPopup "
                    + "and this canary. Screen:\n" + popupScreen);
            }

            PopupPresent(popupScreen).ShouldBeTrue(
                "popup present after ask_user_question. Screen:\n" + popupScreen);

            await runner.WriteAsync("\u001b");
            await Task.Delay(400);
            var afterEsc = runner.SnapshotScreen();
            log("AFTER ESC:\n" + GkSession.Tail(afterEsc, 1200));
            PopupPresent(afterEsc).ShouldBeTrue(
                "Esc is not the answer path — the popup must still be present. Screen:\n" + afterEsc);

            await runner.WriteAsync("Proceed as planned (Recommended)");
            await Task.Delay(50);
            await runner.WriteAsync("\r");
            await Task.Delay(1500);
            var afterAnswer = runner.SnapshotScreen();
            log("AFTER ANSWER:\n" + GkSession.Tail(afterAnswer, 1200));
            PopupPresent(afterAnswer).ShouldBeFalse(
                "one typed option + Enter clears the popup. Screen:\n" + afterAnswer);
        }
        finally
        {
            GkSession.BestEffortDelete(cwd);
        }
    }

    private static async Task<GrokUpdateRow?> WaitForAskUserQuestionAsync(
        string updatesPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var rows = GkSession.ReadUpdates(updatesPath);
            var hit = rows.LastOrDefault(r =>
                r.Kind == "tool_call" && r.Raw.Contains("ask_user_question", StringComparison.Ordinal));
            if (hit is not null)
                return hit;
            await Task.Delay(250);
        }

        return null;
    }
}
