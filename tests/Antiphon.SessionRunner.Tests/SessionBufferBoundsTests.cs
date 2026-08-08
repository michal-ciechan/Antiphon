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
/// </summary>
[NotInParallel("SessionLiveness")]
public class SessionBufferBoundsTests
{
    private const int Cap = 4096;

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
            // ~60KB of output - many times the 4KB cap.
            await runtime.SendInputAsync(
                sessionId,
                "for /l %i in (1,1,600) do @echo filler-%i-0123456789012345678901234567890123456789\r",
                CancellationToken.None);
            await runtime.SendInputAsync(sessionId, "echo NEWEST-MARKER\r", CancellationToken.None);
            await WaitForSnapshotAsync(runtime, sessionId, text => text.Contains("NEWEST-MARKER"));

            var raw = runtime.GetSnapshot(sessionId).RawOutput;

            // Bounded: trimming happens at 2x the cap, so that is the ceiling.
            raw.Length.ShouldBeLessThanOrEqualTo(Cap * 2);

            // ...and it is the TAIL that survives, not the head.
            raw.ShouldContain("NEWEST-MARKER");
            raw.ShouldNotContain("filler-1-0123456789");

            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
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
                "for /l %i in (1,1,600) do @echo filler-%i-0123456789012345678901234567890123456789\r",
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

            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
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
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-bufbounds-tests-{Guid.NewGuid():N}"),
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

    private static async Task WaitForSnapshotAsync(
        SessionRunnerRuntime runtime, Guid sessionId, Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(runtime.GetSnapshot(sessionId).RawOutput))
                return;
            await Task.Delay(100);
        }

        throw new System.TimeoutException("Snapshot predicate not satisfied before the deadline.");
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
