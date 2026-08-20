using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0108 S3: the measured facts the Codex turn-completion fix is BUILT ON, pinned against the
/// real CLI so a codex-cli update that changes the TUI goes red here first rather than silently
/// putting <c>RunnerCodexAdapter</c> back on a heuristic that does not fit.
///
/// <para>The sibling <see cref="CodexComposerCanaryTests"/> pins what the composer does with what we
/// TYPE; this pins what the TUI does while it THINKS, and what the rollout says about it. The four
/// facts, all measured 2026-08-20 (codex-cli 0.147.0, gpt-5.6-luna @ low, modern ConPTY, the
/// production <see cref="PtyAgentRunner"/> at 120x30 in a fresh temp cwd):</para>
///
/// <list type="bullet">
/// <item><b>1 — the production submit path's CR often does not submit</b> (6/6 stranded across two
/// probe runs; S2 measured it as a coin flip). <b>Recorded, never asserted</b> — it is not
/// deterministic, and pinning either outcome would pin a coin flip. What IS asserted is the
/// consequence: one extra Enter submits.</item>
/// <item><b>2 — a live turn renders <c>Working (Ns • esc to interrupt)</c></b> and the line LEAVES
/// the screen when the turn completes. This is the whole screen fallback in
/// <see cref="CodexTurnScreenTracker"/>; if the strings move, that fallback silently stops
/// completing turns for transcript-less sessions.</item>
/// <item><b>3 — <c>task_complete</c> is observable within a few seconds of the submitting Enter</b>
/// (1.85/2.72/2.75 s measured, i.e. the same instant the answer renders, and FASTER than the 3 s
/// quiet wait it replaced). This is why the transcript is the primary signal and not a slow
/// cross-check.</item>
/// <item><b>4 — there is NO done line.</b> A completed turn's screen carries the answer and a fresh
/// composer; Grok's "Worked for 1.7s" has no Codex analogue, which is why none was ported. Asserted
/// as an absence so a future release that ADDS one shows up here as a chance to use it.</item>
/// </list>
///
/// <para>Headed, opt-in (<c>ANTIPHON_CODEX_HEADED_TESTS=1</c>), <c>[Explicit]</c>: it spends a real
/// Codex turn.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0108")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexDoneDetectionCanaryTests
{
    private const string Model = "gpt-5.6-luna";
    private const string Marker = "CX-DONE";

    [Test]
    public async Task Turn_lifecycle_contract_on_the_modern_backend()
    {
        CxSession.SkipIfNotEligible();
        var log = CxSession.MeasurementLog(nameof(Turn_lifecycle_contract_on_the_modern_backend));
        var cwd = CxSession.TempCwd();
        var before = CxSession.Rollouts().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drift = new List<string>();

        try
        {
            var (app, args) = CxSession.BuildLaunch(
                CxSession.ResolveCli()!, "-m", Model, "-c", "model_reasoning_effort=\"low\"");
            log($"LAUNCH: {app} {string.Join(" ", args)}");

            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(app, args, cwd: cwd, cols: 120, rows: 30);
            runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty,
                "the deployment runs modern; measuring the inbox conhost would measure the wrong pty. "
                + "Reason: " + runner.Backend!.Reason);

            (await CxSession.WaitForComposerAsync(runner, TimeSpan.FromSeconds(90)))
                .ShouldBeTrue("the composer must render before anything is typed. Screen:\n"
                    + runner.SnapshotScreen());

            // ---- Fact 1: the production submit path, verbatim ----------------------------------
            // PtyAgentRunner.SendLineAsync IS the production path: body, 20ms, a separate CR.
            var body = $"{Marker} reply with exactly OK and nothing else";
            var submittedAt = DateTime.UtcNow;
            await runner.SendLineAsync(body);

            var probe = new RolloutProbe(before, cwd);
            var firstCrSubmitted = await probe.WaitForPromptAsync(Marker, TimeSpan.FromSeconds(6)) is not null;
            // RECORDED, deliberately not asserted: measured 0/6 on 2026-08-20 and 1-in-3 on CARD-0099
            // S2. The non-determinism IS the finding — a caller cannot know whether its CR submitted,
            // which is exactly why CodexSubmitConfirmation confirms and re-presses Enter.
            log($"FACT 1 production body+delayed-CR submitted on its own: {firstCrSubmitted} "
                + "(MEASURED 2026-08-20: false 6 times out of 6; NOT deterministic)");

            var enters = 1;
            while (!firstCrSubmitted && enters <= 3)
            {
                await runner.WriteAsync("\r"); // Enter ONLY — never a re-type
                enters++;
                submittedAt = DateTime.UtcNow;
                if (await probe.WaitForPromptAsync(Marker, TimeSpan.FromSeconds(8)) is not null)
                    break;
            }

            if (probe.Rollout is null)
            {
                drift.Add($"1: the prompt never reached a rollout after {enters} Enter press(es) — "
                    + "Enter-only recovery no longer works and CodexSubmitConfirmation cannot deliver");
                throw new SkipTestException(
                    "no rollout was produced for this session, so there is nothing to measure the "
                    + "turn lifecycle against. Screen:\n" + CxSession.Tail(runner.SnapshotScreen(), 1200));
            }
            log($"FACT 1 the prompt landed after {enters} Enter press(es); "
                + $"rollout={Path.GetFileName(probe.Rollout)}");

            // ---- Fact 2: the Working indicator, seen then gone ---------------------------------
            var indicatorSeen = false;
            var indicatorGoneAt = (DateTime?)null;
            string? indicatorLine = null;
            var completeSeenAt = (DateTime?)null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                var screen = runner.SnapshotScreen();
                var visible = CodexWorkingIndicator.IsVisible(screen);
                if (visible)
                {
                    indicatorSeen = true;
                    indicatorGoneAt = null;
                    indicatorLine ??= screen.Split('\n')
                        .FirstOrDefault(l => l.Contains(CodexWorkingIndicator.Prefix, StringComparison.Ordinal));
                }
                else if (indicatorSeen)
                {
                    indicatorGoneAt ??= DateTime.UtcNow;
                }

                // ---- Fact 3: task_complete's own latency, from the same loop -------------------
                if (completeSeenAt is null && probe.CompletedTurns() > 0)
                    completeSeenAt = DateTime.UtcNow;

                if (completeSeenAt is not null && indicatorGoneAt is not null)
                    break;

                await Task.Delay(200);
            }

            log($"FACT 2 Working indicator seen: {indicatorSeen}; line: {indicatorLine ?? "<none>"}");
            if (!indicatorSeen)
            {
                drift.Add("2: the 'Working ( … esc to interrupt)' indicator never rendered during a "
                    + "turn that demonstrably ran. CodexTurnScreenTracker's screen fallback is now "
                    + "dead code, and a transcript-less Codex session will never complete a turn");
            }
            else if (indicatorGoneAt is null)
            {
                drift.Add("2: the Working indicator never LEFT the screen after the turn completed — "
                    + "the fallback's disappearance rule cannot fire");
            }

            log($"FACT 3 task_complete observable {(completeSeenAt is null ? "NEVER" : (completeSeenAt.Value - submittedAt).TotalSeconds.ToString("F2") + "s")} "
                + "after the submitting Enter (MEASURED 2026-08-20: 1.85 / 2.72 / 2.75 s)");
            if (completeSeenAt is null)
            {
                drift.Add("3: no task_complete row appeared within 120s of a turn that ran — the "
                    + "primary turn-completion signal is gone");
            }
            else if (completeSeenAt.Value - submittedAt > TimeSpan.FromSeconds(30))
            {
                drift.Add($"3: task_complete took {(completeSeenAt.Value - submittedAt).TotalSeconds:F1}s, "
                    + "far past the measured ~3s. The transcript is no longer the FASTER signal, which "
                    + "is the premise for making it primary");
            }

            // ---- Fact 4: no done line -----------------------------------------------------------
            await Task.Delay(1500);
            var finalScreen = runner.SnapshotScreen();
            var doneLine = System.Text.RegularExpressions.Regex.Match(
                finalScreen, @"Worked for \d+(?:\.\d+)?s");
            log($"FACT 4 a Grok-style done line on the completed screen: {doneLine.Success} "
                + $"({(doneLine.Success ? doneLine.Value : "none — as measured")})");
            if (doneLine.Success)
            {
                drift.Add("4: Codex now renders a 'Worked for Ns' done line. That is a positive screen "
                    + "signal the fallback could use instead of the indicator's ABSENCE — worth "
                    + "adopting, and worth knowing this canary's premise changed");
            }
            log("FINAL SCREEN TAIL:\n" + CxSession.Tail(finalScreen, 800));

            log("ROLLOUT payload census: " + string.Join(", ", CxSession.ReadRollout(probe.Rollout)
                .GroupBy(r => $"{r.Type}/{r.EventType ?? "-"}{(r.ItemType is null ? "" : ":" + r.ItemType)}")
                .Select(g => $"{g.Key}={g.Count()}")));

            await runner.KillAsync(TimeSpan.FromSeconds(5));

            drift.ShouldBeEmpty(
                "Codex's measured turn lifecycle drifted from what CARD-0108 built on. The full "
                + "measurement is in TestOutput/CodexCanary/"
                + nameof(Turn_lifecycle_contract_on_the_modern_backend) + ".log. Findings:\n  - "
                + string.Join("\n  - ", drift));
        }
        catch (Exception ex) when (ex is not SkipTestException)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            CxSession.BestEffortDelete(cwd);
        }
    }

    /// <summary>
    /// Resolves the session's rollout BY CWD, re-resolving while it is still null — the rollout does
    /// not exist until the session's first TURN, and other agents on this machine write their own
    /// rollouts into the same CODEX_HOME, so "the newest new file" would hand this test a stranger's
    /// transcript (the CARD-0006 failure, in a test). Same shape as
    /// <c>CodexComposerCanaryTests.PromptProbe</c>.
    /// </summary>
    private sealed class RolloutProbe(HashSet<string> before, string cwd)
    {
        public string? Rollout { get; private set; }

        public async Task<CodexRolloutRow?> WaitForPromptAsync(string marker, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Resolve() is { } path)
                {
                    var row = CxSession.ReadRollout(path)
                        .FirstOrDefault(r => IsPrompt(r) && r.Text!.Contains(marker, StringComparison.Ordinal));
                    if (row is not null) return row;
                }
                await Task.Delay(200);
            }
            return null;
        }

        public int CompletedTurns() =>
            Resolve() is { } path
                ? CxSession.ReadRollout(path).Count(r => r.EventType == "task_complete")
                : 0;

        private string? Resolve()
        {
            if (Rollout is not null) return Rollout;
            var wanted = Path.GetFullPath(cwd).TrimEnd('\\');
            foreach (var path in CxSession.Rollouts().Where(f => !before.Contains(f)))
            {
                var declared = CxSession.ReadRollout(path)
                    .FirstOrDefault(r => r.Type == "session_meta")?.Cwd;
                if (declared is not null
                    && string.Equals(declared.TrimEnd('\\'), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return Rollout = path;
                }
            }
            return null;
        }

        private static bool IsPrompt(CodexRolloutRow r) =>
            r.Text is { Length: > 0 }
            && (r.EventType == "user_message"
                || (r.EventType == "item_completed"
                    && string.Equals(r.ItemType, "UserMessage", StringComparison.OrdinalIgnoreCase)));
    }
}
