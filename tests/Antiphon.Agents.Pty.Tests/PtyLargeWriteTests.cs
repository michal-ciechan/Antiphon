using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Pins that OUR write path does not lose bytes — the half of the 2026-08-10 investigation that
/// can be tested in CI.
///
/// <para><b>What happened.</b> A 5 203-character delegation brief reached its delegate as 1 091
/// characters: the first 1 018 and the last 73, welded together mid-word with nothing to say
/// anything was gone. Aligning the delivered text against the queued body byte-for-byte put the
/// seam on 1024-byte boundaries of the written payload — chunk 0 landed, chunks 1-4 were dropped,
/// the final partial chunk landed. The same shape hit a 5 368-character delegate REPORT going the
/// other way (task 0b0f558c), where the dropped span was 5 120 bytes = 5 x 1024. Seven real
/// deliveries put the cliff between 4 262 characters (intact) and 5 185 (mangled).</para>
///
/// <para><b>What these tests can and cannot show.</b> They drive the fake Claude — a real console
/// program on the real ConPTY path — and it receives 43 KB in a single write without losing a
/// byte, at any drain rate (<c>ANTIPHON_FAKE_STDIN_READ_DELAY_MS</c>). So the loss is NOT in
/// <see cref="PtyAgentRunner"/>, the pty-host pipe, or the runner: those are lossless, and these
/// tests are what keeps them that way. The loss is downstream, in the input path the real Claude
/// TUI reads through, which no CI-runnable peer reproduces. The fix therefore does not try to
/// out-write a lossy transport — it stops handing it bodies that big
/// (<c>DelegationSettings.PtyInlineSafeChars</c>, and the spill-to-file paths that keep under it).</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
public class PtyLargeWriteTests
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

    private static async Task<PtyAgentRunner> LaunchReadyFakeAsync(int readDelayMs)
    {
        var runner = new PtyAgentRunner();
        await runner.StartAsync(
            FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30,
            env: new Dictionary<string, string>
            {
                ["ANTIPHON_FAKE_STDIN_READ_DELAY_MS"] = readDelayMs.ToString(),
            });
        var ready = await runner.WaitForOutputAsync(
            s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15));
        ready.ShouldBeTrue("fake Claude should print its readiness banner");
        runner.ClearLiveBuffer();
        return runner;
    }

    // Every line carries its own index, so a gap names exactly which span was lost rather than
    // just "lengths differ" — that is what turned the live miss from "looks garbled" into a
    // byte-offset.
    private static string BuildMarkedBody(int lines) =>
        string.Join("\n", Enumerable.Range(0, lines).Select(i => $"L{i:D4} {new string('x', 20)}"));

    private static async Task AssertArrivesWholeAsync(PtyAgentRunner runner, int lines)
    {
        var body = BuildMarkedBody(lines);
        await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
        await Task.Delay(200);
        await runner.WriteAsync("\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:"), TimeSpan.FromSeconds(60));
        submitted.ShouldBeTrue($"a {body.Length}-char body should submit");

        var output = runner.SnapshotText();
        var missing = Enumerable.Range(0, lines).Where(i => !output.Contains($"L{i:D4} ")).ToList();

        missing.ShouldBeEmpty(
            $"every line of a {body.Length}-char body must reach the child, but {missing.Count}/{lines} "
            + $"were dropped (first missing: {(missing.Count > 0 ? missing[0] : -1)}, "
            + $"last: {(missing.Count > 0 ? missing[^1] : -1)}) — a silent mid-body splice");
    }

    /// <summary>
    /// 5.4 KB is the live-miss size; 21 KB is well past it. Both must arrive whole through our
    /// stack — if this ever goes red, the loss HAS moved into code we own.
    /// </summary>
    [Test]
    [Arguments(200)]
    [Arguments(800)]
    public async Task Body_of_any_size_reaches_the_child_whole(int lines)
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync(readDelayMs: 0);

        await AssertArrivesWholeAsync(runner, lines);

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// A real TUI renders between reads rather than sitting in a tight read loop, so it drains the
    /// input pipe in bursts. Slow drain is the condition under which a lossy transport loses — our
    /// path holds because a pipe write blocks rather than discarding.
    /// </summary>
    [Test]
    public async Task Slow_draining_child_still_receives_every_byte()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync(readDelayMs: 25);

        await AssertArrivesWholeAsync(runner, lines: 400);

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// The head-and-tail signature specifically: the live failure ALWAYS preserved both ends, which
    /// is what let it past a check that matches on either one. Asserting on the middle is the
    /// assertion that would have caught it.
    /// </summary>
    [Test]
    public async Task Middle_of_a_large_body_is_not_silently_dropped()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync(readDelayMs: 0);

        const int lines = 200;
        var body = BuildMarkedBody(lines);
        await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
        await Task.Delay(200);
        await runner.WriteAsync("\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:"), TimeSpan.FromSeconds(60));
        submitted.ShouldBeTrue("the body should submit");

        var output = runner.SnapshotText();
        // Head and tail survived even in the live miss — the middle is the discriminating check.
        output.ShouldContain("L0000 ", customMessage: "head must arrive");
        output.ShouldContain($"L{lines - 1:D4} ", customMessage: "tail must arrive");
        output.ShouldContain($"L{lines / 2:D4} ", customMessage: "the MIDDLE must arrive too");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
