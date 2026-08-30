using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0159 S0: headed canary that submitting a prompt into a working Grok turn emits
/// <c>turn_completed stop_reason=cancelled</c> then the new <c>user_message_chunk</c>, in that
/// order. Pins the live 2026-08-23 incident (43 ms gap) against the real TUI so fakegrok's
/// <c>ANTIPHON_FAKE_SUBMIT_WHILE_WORKING=cancel</c> knob stays honest. Opt-in
/// (<c>ANTIPHON_HEADED_TESTS=1</c>), spends real Grok turns.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0159")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokSubmitWhileWorkingCanaryTests
{
    [Test]
    public async Task Submit_during_a_tool_turn_writes_cancelled_then_the_new_user_chunk()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Submit_during_a_tool_turn_writes_cancelled_then_the_new_user_chunk));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            await runner.WriteAsync(
                "Run this exact command and wait for it to finish: pwsh -NoProfile -Command \"Start-Sleep -Seconds 25; 'slept'\". Use the terminal tool. Do not skip it.");
            await Task.Delay(50);
            await runner.WriteAsync("\r");

            var tool = await WaitForUpdateAsync(
                updates, r => r.Kind == "tool_call", TimeSpan.FromMinutes(2));
            tool.ShouldNotBeNull(
                "the turn must reach a tool call before we can submit into it. Screen:\n"
                + runner.SnapshotScreen());
            log("tool_call landed; submitting mid-turn");

            await runner.WriteAsync("Proceed as planned");
            await Task.Delay(50);
            await runner.WriteAsync("\r");
            log("second prompt + Enter sent; waiting 20s for jsonl…");
            await Task.Delay(TimeSpan.FromSeconds(20));

            var rows = GkSession.ReadUpdates(updates);
            log("ROW KINDS: " + string.Join(", ",
                rows.GroupBy(r => $"{r.Method}:{r.Kind}").Select(g => $"{g.Key}×{g.Count()}")));
            foreach (var c in rows.Where(r => r.Kind == "turn_completed"))
                log($"turn_completed stop_reason='{c.StopReason}' ts={c.AgentTimestampMs}: "
                    + GkSession.Truncate(c.Raw, 400));

            var cancelled = rows.LastOrDefault(r => r.Kind == "turn_completed" && r.StopReason == "cancelled");
            cancelled.ShouldNotBeNull(
                "submitting into a working turn must emit turn_completed stop_reason=cancelled");

            var userAfter = rows
                .SkipWhile(r => r != cancelled)
                .Skip(1)
                .FirstOrDefault(r => r.Kind == "user_message_chunk");
            userAfter.ShouldNotBeNull("the new prompt must land as user_message_chunk AFTER the cancel");
            userAfter!.Text.ShouldContain("Proceed as planned");

            if (cancelled!.AgentTimestampMs is long cancelTs && userAfter.AgentTimestampMs is long userTs)
            {
                var gap = userTs - cancelTs;
                log($"gap cancel→user chunk: {gap}ms");
                gap.ShouldBeGreaterThanOrEqualTo(0, "file order matches timestamp order of the incident");
            }

            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }
    }

    [Test]
    public async Task Submit_during_a_streaming_text_turn_writes_cancelled_then_the_new_user_chunk()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(
            nameof(Submit_during_a_streaming_text_turn_writes_cancelled_then_the_new_user_chunk));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            await runner.WriteAsync(
                "Write out the numbers from one to three hundred as English words, one per line. Use no tools.");
            await Task.Delay(50);
            await runner.WriteAsync("\r");

            (await WaitForUpdateAsync(
                updates,
                r => r.Kind is "agent_message_chunk" or "agent_thought_chunk",
                TimeSpan.FromMinutes(2))).ShouldNotBeNull(
                "the turn must start streaming before it can be interrupted. Screen:\n"
                + runner.SnapshotScreen());

            await runner.WriteAsync("stop and report");
            await Task.Delay(50);
            await runner.WriteAsync("\r");
            log("second prompt + Enter sent mid-stream; waiting 20s…");
            await Task.Delay(TimeSpan.FromSeconds(20));

            var rows = GkSession.ReadUpdates(updates);
            var completions = rows.Where(r => r.Kind == "turn_completed").ToList();
            foreach (var c in completions)
                log($"turn_completed stop_reason='{c.StopReason}': " + GkSession.Truncate(c.Raw, 300));

            // Plan §9: whether Grok queues rather than cancels mid-stream is unmeasured. Record
            // the shape; if a cancelled row is present the order must match the tool-turn arm.
            if (completions.Any(c => c.StopReason == "cancelled"))
            {
                var cancelled = completions.Last(c => c.StopReason == "cancelled");
                var userAfter = rows.SkipWhile(r => r != cancelled).Skip(1)
                    .FirstOrDefault(r => r.Kind == "user_message_chunk");
                userAfter.ShouldNotBeNull("cancelled then user_message_chunk, same as the tool-turn arm");
            }
            else
            {
                log("NO cancelled turn_completed mid-stream — Grok queued rather than cancelled. Recorded, not a fail.");
            }

            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }
    }

    private static async Task<GrokUpdateRow?> WaitForUpdateAsync(
        string updatesPath, Func<GrokUpdateRow, bool> match, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = GkSession.ReadUpdates(updatesPath).LastOrDefault(match);
            if (hit is not null)
                return hit;
            await Task.Delay(200);
        }
        return null;
    }
}
