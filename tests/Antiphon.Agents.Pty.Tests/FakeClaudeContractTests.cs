using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Locks the Claude TUI <em>submit contract</em> against the fake Claude (a real console program driven
/// through the same ConPTY path as the real CLI). These run in CI — no <c>ANTIPHON_HEADED_TESTS</c>, no
/// real Claude, no auth — because the peer is deterministic.
///
/// They exist because the bug that shipped (queued messages landing in the composer but never submitting)
/// was invisible to <c>FakeAgentProtocolAdapter</c>, a C#-level stub whose <c>SendInputAsync</c> is just
/// <c>SentInput += input</c> — it has no terminal, so <c>"body\r"</c> in one write and <c>"body"</c> then
/// <c>"\r"</c> in two writes look identical to it. The defect lived at the real PTY boundary, which only a
/// live peer exercises. <c>SessionMessageQueueService.DeliverAsync</c> now sends the body and the
/// submitting CR as two separate writes; these tests pin both sides of that distinction.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
public class FakeClaudeContractTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    private static void SkipIfUnavailable()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");
    }

    private static async Task<PtyAgentRunner> LaunchReadyFakeAsync(
        IDictionary<string, string>? env = null)
    {
        var runner = new PtyAgentRunner();
        await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30, env: env);
        var ready = await runner.WaitForOutputAsync(s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15));
        ready.ShouldBeTrue("fake Claude should print its readiness banner");
        runner.ClearLiveBuffer();
        return runner;
    }

    // The BUG path: text and the submitting CR in a SINGLE write. The TUI reads it as a paste — the CR
    // collapses to a literal newline and the line is NOT submitted. This is exactly what the old
    // DeliverAsync did (`body.TrimEnd() + "\r"` in one SendInputAsync), so the message was stranded.
    [Test]
    public async Task Text_and_CR_in_one_write_does_NOT_submit()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("queued message\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:queued message"), TimeSpan.FromSeconds(2));
        submitted.ShouldBeFalse("text+CR in one write must be treated as a paste, not a submit");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The FIX path: body, then the CR as a SEPARATE write — the two-write shape DeliverAsync now uses
    // (two SendInputAsync calls). The lone CR is a discrete Enter and submits the buffered line.
    [Test]
    public async Task Body_then_separate_CR_submits()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("queued message");
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:queued message"), TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue("body followed by a separate CR must submit");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // SendLineAsync is the runner primitive (body, 20ms, "\r") that the queue's two-write delivery mirrors;
    // pin that it submits against the same peer, so a regression in either layer is caught here.
    [Test]
    public async Task SendLineAsync_submits_a_turn_and_emits_idle_signal()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.SendLineAsync("hello there");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:hello there"), TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue();

        // The turn-end signal RunnerClaudeAdapter keys on must reach our capture so higher layers see
        // "idle". We assert the " for Ns" done pattern specifically — the idle OSC title is also emitted
        // but ConPTY consumes window-title sequences, so the done pattern is the one that survives.
        // (Waited-for, not snapshotted: the SUBMITTED match above can win the race against the
        // trailing turn-end bytes.)
        var done = await runner.WaitForOutputAsync(s => s.Contains(" for 1s"), TimeSpan.FromSeconds(3));
        done.ShouldBeTrue("done pattern (\" for Ns\") must be emitted at turn end");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // A batched delivery is ONE multi-line paste burst followed by a lone CR. The whole body must
    // submit as one turn, and the SUBMITTED marker must escape newlines so it stays one assertable line.
    [Test]
    public async Task Multi_line_paste_then_lone_cr_submits_whole_body_with_escaped_marker()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "[context]\nfirst message\nsecond message\n\n[current]\nthird message respond now";
        await runner.WriteAsync(body);
        await Task.Delay(40);
        await runner.WriteAsync("\r");

        var escaped = body.Replace("\n", "\\n");
        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:" + escaped),
            TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue(
            "the whole multi-line body must submit as one turn with an escaped marker. Raw output:\n"
            + runner.SnapshotText());

        // Wall-of-text bodies must not flood the screen: the response echo truncates at 60 chars
        // of the escaped form (single assertable line).
        var echoed = await runner.WaitForOutputAsync(
            s => s.Contains("FAKE response to: " + escaped[..60]), TimeSpan.FromSeconds(3));
        echoed.ShouldBeTrue("response echo must be the escaped body truncated to 60 chars");
        runner.SnapshotText().ShouldNotContain("FAKE response to: " + escaped[..61]);

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // Compaction is not a turn: /compact renders the pinned "Compacted (...)" screen line and NO
    // " for Ns" done pattern — the detectors must never read a compaction as a completed turn.
    [Test]
    public async Task Slash_compact_emits_compacted_screen_line_and_no_turn_end()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.SendLineAsync("/compact");

        var compacted = await runner.WaitForOutputAsync(
            s => s.Contains("Compacted (ctrl+o to see full summary)"), TimeSpan.FromSeconds(5));
        compacted.ShouldBeTrue("/compact must render the pinned Compacted screen line");

        await Task.Delay(300); // give any (wrong) turn-end output time to arrive
        runner.SnapshotText().ShouldNotContain(" for 1s",
            customMessage: "compaction must not emit the turn-end done pattern");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The fallback screen detector must trip on the fake's (pinned) Compacted line — keeps the
    // fake, the detector regex, and the canary-pinned real line locked together.
    [Test]
    public async Task Compacted_screen_line_from_fakeclaude_trips_the_detector()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.SendLineAsync("/compact");

        var detected = await new ClaudeCompactedDetector { MaxWait = TimeSpan.FromSeconds(5) }
            .WaitAsync(runner);
        detected.ShouldBeTrue("ClaudeCompactedDetector must trip on the pinned Compacted line");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Compact_after_turns_env_emits_compacted_after_nth_turn()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync(
            new Dictionary<string, string> { ["ANTIPHON_FAKE_COMPACT_AFTER_TURNS"] = "2" });

        await runner.SendLineAsync("first");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:first"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();
        runner.SnapshotText().ShouldNotContain("Compacted (",
            customMessage: "auto-compaction must not fire before the configured turn count");

        await runner.SendLineAsync("second");
        var compacted = await runner.WaitForOutputAsync(
            s => s.Contains("Compacted (ctrl+o to see full summary)"), TimeSpan.FromSeconds(5));
        compacted.ShouldBeTrue("auto-compaction must fire after the Nth turn");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Transcript_path_env_appends_user_assistant_and_boundary_lines()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-transcript-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path });

            await runner.SendLineAsync("hello transcript");
            (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:hello transcript"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            await runner.SendLineAsync("/compact");
            (await runner.WaitForOutputAsync(s => s.Contains("Compacted ("), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            await runner.KillAsync(TimeSpan.FromSeconds(2));

            var lines = (await File.ReadAllLinesAsync(path)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.ShouldBe(3);
            lines[0].ShouldContain("\"type\":\"user\"");
            lines[0].ShouldContain("hello transcript");
            lines[1].ShouldContain("\"type\":\"assistant\"");
            lines[1].ShouldContain("\"stop_reason\":\"end_turn\"");
            lines[2].ShouldContain("\"subtype\":\"compact_boundary\"");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    // Two queued messages, each submitted on its own turn — the queue flushes one message per turn-end,
    // so each must round-trip independently when delivered the correct (two-write) way.
    [Test]
    public async Task Two_separate_turns_each_submit()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.SendLineAsync("first");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:first"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();

        await runner.SendLineAsync("second");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:second"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
