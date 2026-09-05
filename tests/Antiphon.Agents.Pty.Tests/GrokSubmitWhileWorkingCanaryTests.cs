using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0159 S0: headed canary that submitting a prompt into a working Grok turn emits
/// <c>turn_completed stop_reason=cancelled</c> then the new <c>user_message_chunk</c>, in that
/// order. Pins the live 2026-08-23 incident (43 ms gap) against the real TUI so fakegrok's
/// <c>ANTIPHON_FAKE_SUBMIT_WHILE_WORKING=cancel</c> knob stays honest.
/// CARD-0355 S0 adds the complementary queue canary: a single Enter into a still-open tool
/// turn must stay silent on <c>updates.jsonl</c> until drain. Opt-in
/// (<c>ANTIPHON_HEADED_TESTS=1</c>), spends real Grok turns.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0159")]
[Category("Card0355")]
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

    /// <summary>
    /// CARD-0355 S0: a distinctive single-line follow-up Enter'd into a still-open Grok tool
    /// turn must not appear as <c>user_message_chunk</c> until the predecessor
    /// <c>turn_completed</c>, then exactly once. The queue pane is toggled with BEL only here;
    /// screen fragments are logged, never used as a production predicate.
    /// </summary>
    [Test]
    public async Task Queued_follow_up_during_a_tool_turn_is_silent_on_jsonl_until_drain()
    {
        GkSession.SkipIfNotEligible();
        GkSession.SkipUnlessFollowUpQueues(GkSession.DefaultGrokHome);
        var log = GkSession.MeasurementLog(
            nameof(Queued_follow_up_during_a_tool_turn_is_silent_on_jsonl_until_drain));
        var followUp = GkSession.ReadFollowUpBehavior(GkSession.DefaultGrokHome);
        log($"follow_up_behavior={followUp} (absent key is vendor default queue)");

        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);
        var marker = "GK-Q-" + Guid.NewGuid().ToString("N")[..12];

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            await runner.WriteAsync(
                "Run this exact command and wait for it to finish: pwsh -NoProfile -Command "
                + "\"Start-Sleep -Seconds 70; 'slept'\". Use the terminal tool. Do not skip it.");
            await Task.Delay(50);
            await runner.WriteAsync("\r");

            var tool = await WaitForUpdateAsync(
                updates, r => r.Kind == "tool_call", TimeSpan.FromMinutes(2));
            tool.ShouldNotBeNull(
                "the turn must reach a tool call before a follow-up can be queued into it. Screen:\n"
                + runner.SnapshotScreen());

            var beforeRows = GkSession.ReadUpdates(updates);
            var completesBefore = beforeRows.Count(r => r.Kind == "turn_completed");
            log("PRE-QUEUE SCREEN:\n" + GkSession.Tail(runner.SnapshotScreen(), 1600));
            log("PRE-QUEUE ROW KINDS: " + SummarizeKinds(beforeRows));
            log($"turn_completed count before follow-up: {completesBefore}");

            await runner.WriteAsync(marker);
            (await runner.WaitForScreenAsync(
                s => s.Contains(marker, StringComparison.Ordinal),
                TimeSpan.FromSeconds(8)))
                .ShouldBeTrue("the follow-up marker must render in the composer before Enter. Screen:\n"
                    + runner.SnapshotScreen());
            await runner.WriteAsync("\r");
            log($"one Enter sent with marker {marker}; not re-entering");

            // Let the TUI accept the Enter, then snapshot the default screen and the toggled pane
            // while the predecessor is still open. A second Enter would drain the queue immediately.
            await Task.Delay(800);
            AssertNoMarkerChunkBeforePredecessorComplete(updates, marker, completesBefore, log);

            var defaultScreen = runner.SnapshotScreen();
            log("DEFAULT SCREEN AFTER QUEUE ENTER:\n" + GkSession.Tail(defaultScreen, 1600));
            log(DescribeQueueVisibility("default", defaultScreen, marker));

            await runner.WriteAsync(GkSession.QueuePaneToggle);
            await Task.Delay(500);
            var paneScreen = runner.SnapshotScreen();
            log("QUEUE-PANE TOGGLE (Ctrl+' / BEL) SCREEN:\n" + GkSession.Tail(paneScreen, 1600));
            log(DescribeQueueVisibility("toggled-pane", paneScreen, marker));

            await runner.WriteAsync(GkSession.QueuePaneToggle);
            await Task.Delay(400);
            log("PANE TOGGLED BACK. Screen tail:\n" + GkSession.Tail(runner.SnapshotScreen(), 800));

            var predecessor = await WaitForUpdateAsync(
                updates,
                r => r.Kind == "turn_completed"
                    && GkSession.ReadUpdates(updates).Count(x => x.Kind == "turn_completed") > completesBefore,
                TimeSpan.FromMinutes(3));
            predecessor.ShouldNotBeNull(
                "predecessor tool turn must complete so the queued body can drain. Screen:\n"
                + runner.SnapshotScreen());
            log($"predecessor turn_completed stop_reason='{predecessor!.StopReason}' ts={predecessor.AgentTimestampMs}: "
                + GkSession.Truncate(predecessor.Raw, 400));
            predecessor.StopReason.ShouldNotBe("cancelled",
                "CARD-0355 measures the in-memory queue, not CARD-0159 cancel-and-send. "
                + "A cancelled predecessor means Enter aborted the tool turn instead of queueing.");

            var afterComplete = GkSession.ReadUpdates(updates);
            var markerBeforeComplete = RowsBefore(afterComplete, predecessor)
                .Where(r => r.Kind == "user_message_chunk" && r.Text?.Contains(marker, StringComparison.Ordinal) == true)
                .ToList();
            markerBeforeComplete.ShouldBeEmpty(
                "queued body must not reach user_message_chunk before predecessor turn_completed. Rows:\n"
                + string.Join("\n", markerBeforeComplete.Select(r => GkSession.Truncate(r.Raw, 200))));

            var drained = await WaitForUpdateAsync(
                updates,
                r => r.Kind == "user_message_chunk" && r.Text?.Contains(marker, StringComparison.Ordinal) == true,
                TimeSpan.FromMinutes(2));
            drained.ShouldNotBeNull(
                "exactly one marker-bearing user_message_chunk must appear after drain. Screen:\n"
                + runner.SnapshotScreen());

            var finalRows = GkSession.ReadUpdates(updates);
            log("FINAL ROW ORDER: " + string.Join(" | ",
                finalRows.Select(r => $"{r.Kind}{(r.StopReason is { } s ? $"/{s}" : "")}")));
            log("FINAL ROW KINDS: " + SummarizeKinds(finalRows));
            var markerChunks = finalRows
                .Where(r => r.Kind == "user_message_chunk" && r.Text?.Contains(marker, StringComparison.Ordinal) == true)
                .ToList();
            markerChunks.Count.ShouldBe(1, "drain is one ordinary user_message_chunk, not N queue rows");
            var markerIndex = finalRows.FindIndex(r => r == markerChunks[0]);
            var predIndex = finalRows.FindIndex(r => r == predecessor);
            markerIndex.ShouldBeGreaterThan(predIndex,
                "the drained user_message_chunk must sit after predecessor turn_completed");

            log("POST-DRAIN SCREEN:\n" + GkSession.Tail(runner.SnapshotScreen(), 1200));
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

    private static void AssertNoMarkerChunkBeforePredecessorComplete(
        string updatesPath, string marker, int completesBefore, Action<string> log)
    {
        var rows = GkSession.ReadUpdates(updatesPath);
        var completes = rows.Count(r => r.Kind == "turn_completed");
        var markerChunks = rows
            .Where(r => r.Kind == "user_message_chunk" && r.Text?.Contains(marker, StringComparison.Ordinal) == true)
            .ToList();
        log("MID-QUEUE ROW KINDS: " + SummarizeKinds(rows));
        if (completes == completesBefore && markerChunks.Count > 0)
        {
            throw new ShouldAssertException(
                "marker reached user_message_chunk before the predecessor ended. "
                + GkSession.Truncate(markerChunks[0].Raw, 300));
        }
    }

    private static IEnumerable<GrokUpdateRow> RowsBefore(List<GrokUpdateRow> rows, GrokUpdateRow boundary)
    {
        foreach (var row in rows)
        {
            if (row == boundary) yield break;
            yield return row;
        }
    }

    private static string SummarizeKinds(IReadOnlyList<GrokUpdateRow> rows) =>
        string.Join(", ", rows.GroupBy(r => $"{r.Method}:{r.Kind}").Select(g => $"{g.Key}×{g.Count()}"));

    private static string DescribeQueueVisibility(string label, string screen, string marker)
    {
        var hasMarker = screen.Contains(marker, StringComparison.Ordinal);
        var queuedHint = screen.Contains("queued", StringComparison.OrdinalIgnoreCase)
            || screen.Contains("follow-up", StringComparison.OrdinalIgnoreCase)
            || screen.Contains("follow up", StringComparison.OrdinalIgnoreCase);
        return $"{label}: markerVisible={hasMarker} queueHint={queuedHint}";
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
