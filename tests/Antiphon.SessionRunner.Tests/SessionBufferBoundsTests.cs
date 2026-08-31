using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// The runner's mirror of a session must stay bounded, and it must never read a whole ANSI log to
/// serve one.
///
/// Both were unbounded once, and it cost real memory: the mirror grew for the life of the session,
/// and /buffer read the entire log from disk on every call - which the server polls every 50ms while
/// waiting for a session to go quiet. On a live box that churned the LOH hard enough to strand
/// multi-GB ArrayPool buckets for the life of the process (~2.7GB resident, 93% of the GC heap on
/// the LOH). The pty-host had always bounded its own ring to the same cap; only the runner's copies
/// were unbounded.
///
/// These tests use a deliberately tiny ReplayBufferMaxChars so a few KB of output crosses the cap.
///
/// The cap and the filler volume are both deliberately small, and the volume is SIZED, not picked.
/// What these tests have to prove is a ratio — output well past the cap, head evicted, tail kept —
/// and the cheapest output that proves it is the right amount. 600 lines was not: cmd's `for /l`
/// rewrites the window title on every iteration, so the pty carries ~3x the bytes of the text and
/// this measured 7-14 lines/sec on a loaded box. The 30s WaitForSnapshotAsync deadline then
/// expired mid-loop and Live_buffer_stays_bounded_and_keeps_the_newest_output failed with the
/// mirror sitting at filler-431 of 600 — nothing wrong with the buffer, the shell was simply
/// still typing. 100 lines (~5 KB) is 2.5x the live buffer's 2x-cap trim ceiling, 5x the /buffer
/// cap and 1.3x the on-disk log floor, and finishes in ~14s at the worst rate measured. Raising
/// the deadline instead would have kept the cost and hidden the next real regression behind it.
/// </summary>
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionBufferBoundsTests
{
    private const int Cap = 1024;
    private const int FillerLines = 100;

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Live_buffer_stays_bounded_and_keeps_the_newest_output()
    {
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var runtime = new SessionRunnerRuntime(
            Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);

        var dto = await StartInteractiveSessionAsync(runtime, sessionId);
        try
        {
            // ~5KB of output - many times the 1KB cap, and well past the 2x-cap trim ceiling.
            await runtime.SendInputAsync(
                sessionId,
                $"for /l %i in (1,1,{FillerLines}) do @echo filler-%i-0123456789012345678901234567890123456789\r",
                CancellationToken.None);
            await runtime.SendInputAsync(sessionId, "echo NEWEST-MARKER\r", CancellationToken.None);
            await WaitForSnapshotAsync(runtime, sessionId, text => text.Contains("NEWEST-MARKER"));

            var raw = runtime.GetSnapshot(sessionId).RawOutput;

            // Bounded: trimming happens at 2x the cap, so that is the ceiling.
            raw.Length.ShouldBeLessThanOrEqualTo(Cap * 2);

            // ...and it is the TAIL that survives, not the head.
            raw.ShouldContain("NEWEST-MARKER");
            raw.ShouldNotContain("filler-1-0123456789");

            await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, sessionId, dto.HostPid);
            await runtime.DisposeAsync();
        }
        finally
        {
            KillBestEffort(dto.Pid);
        }
    }

    [Test]
    public async Task Buffer_endpoint_returns_only_the_tail_of_a_large_ansi_log()
    {
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var runtime = new SessionRunnerRuntime(
            Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);

        var dto = await StartInteractiveSessionAsync(runtime, sessionId);
        try
        {
            await runtime.SendInputAsync(
                sessionId,
                $"for /l %i in (1,1,{FillerLines}) do @echo filler-%i-0123456789012345678901234567890123456789\r",
                CancellationToken.None);
            await runtime.SendInputAsync(sessionId, "echo NEWEST-MARKER\r", CancellationToken.None);
            await WaitForSnapshotAsync(runtime, sessionId, text => text.Contains("NEWEST-MARKER"));

            // The on-disk log is much bigger than the cap - that is the point of the test.
            var ansiLog = Path.Combine(settings.SessionLogPath, $"{sessionId:N}.ansi.log");
            await WaitUntilAsync(
                () => File.Exists(ansiLog) && new FileInfo(ansiLog).Length > Cap * 4L,
                TimeSpan.FromSeconds(15));

            var buffer = runtime.GetBuffer(sessionId).Buffer;

            buffer.Length.ShouldBeLessThanOrEqualTo(Cap);
            buffer.ShouldContain("NEWEST-MARKER");
            buffer.ShouldNotContain("filler-1-0123456789");

            await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, sessionId, dto.HostPid);
            await runtime.DisposeAsync();
        }
        finally
        {
            KillBestEffort(dto.Pid);
        }
    }

    // ---------- helpers ----------

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = TestSessionLogRoot.Create("bufbounds-tests"),
        ReplayBufferMaxChars = Cap,
        PtyHostLingerHours = 0.02,
    };

    private static async Task<RunnerSessionDto> StartInteractiveSessionAsync(
        SessionRunnerRuntime runtime, Guid sessionId)
    {
        var request = new RunnerLaunchRequest(
            sessionId,
            Cmd,
            ["/d", "/q", "/k", "@echo off & prompt $G"],
            new Dictionary<string, string>(),
            Path.GetTempPath(),
            Cols: 100,
            Rows: 25);
        var dto = await runtime.StartAsync(request, CancellationToken.None);

        for (var attempt = 0; attempt < 20 && dto.Status != "Running"; attempt++)
        {
            await Task.Delay(100);
            dto = runtime.Get(sessionId);
        }

        dto.Status.ShouldBe("Running");
        return dto;
    }

    /// <summary>
    /// Waits on PROGRESS, not on a wall clock.
    ///
    /// A fixed 30s deadline here could not tell the two things apart that it needed to: a mirror
    /// that is STUCK, which is the defect these tests exist to catch, and a shell that is merely
    /// SLOW, which says nothing about the buffer at all. Measured on this box, cmd pushed anywhere
    /// from ~1 to over 100 filler lines a second through the pty depending on what else was
    /// running — the title escape it rewrites every iteration costs more than the text — so no
    /// choice of volume or deadline is safe on both. The failure it produced was a plain timeout
    /// with the mirror sitting mid-loop at filler-431 of 600, and the test was green on its own.
    ///
    /// So the stall clock resets whenever the mirror's CONTENT changes. Length cannot be the
    /// signal: the buffer under test is trimmed at 2x the cap, so it plateaus while output is
    /// still flowing. The hard ceiling stays as a backstop for a session that dies outright.
    /// </summary>
    private static async Task WaitForSnapshotAsync(
        SessionRunnerRuntime runtime, Guid sessionId, Func<string, bool> predicate)
    {
        var hardDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        var stallLimit = TimeSpan.FromSeconds(20);
        var lastChange = DateTime.UtcNow;
        string? previous = null;
        var stalled = false;

        while (DateTime.UtcNow < hardDeadline)
        {
            var raw = runtime.GetSnapshot(sessionId).RawOutput;
            if (predicate(raw))
                return;

            if (!string.Equals(raw, previous, StringComparison.Ordinal))
            {
                previous = raw;
                lastChange = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastChange > stallLimit)
            {
                stalled = true;
                break;
            }

            await Task.Delay(100);
        }

        // Say WHAT the mirror held, and which of the two failures this was.
        var final = runtime.GetSnapshot(sessionId).RawOutput;
        var tail = final.Length <= 400 ? final : final[^400..];
        throw new System.TimeoutException(
            (stalled
                ? $"Mirror stopped changing for {stallLimit.TotalSeconds:N0}s before the predicate was satisfied. "
                : "Predicate never satisfied inside the hard ceiling. ")
            + $"RawOutput was {final.Length} chars; tail: {tail}");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;
            await Task.Delay(100);
        }

        throw new System.TimeoutException("Condition not satisfied before the deadline.");
    }

    private static void KillBestEffort(int? pid)
    {
        if (pid is null)
            return;
        try
        {
            System.Diagnostics.Process.GetProcessById(pid.Value).Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }
}
