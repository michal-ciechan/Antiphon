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

    private static async Task<PtyAgentRunner> LaunchReadyFakeAsync()
    {
        var runner = new PtyAgentRunner();
        await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30);
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
        var raw = runner.SnapshotText();
        raw.ShouldContain(" for 1s", customMessage: "done pattern (\" for Ns\") must be emitted at turn end");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
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
