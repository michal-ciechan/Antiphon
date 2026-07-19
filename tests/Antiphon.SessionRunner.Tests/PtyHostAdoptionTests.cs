using System.Diagnostics;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// The acceptance tests for the pty-host split (docs/superpowers/specs/2026-07-19-pty-host-split.md):
/// sessions survive a runner restart. "Restart" = dispose runtime A (drops every pipe exactly like
/// a dying runner process) then adopt from a brand-new runtime B over the same state directories.
/// </summary>
[NotInParallel("SessionLiveness")]
public class PtyHostAdoptionTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Running_session_survives_runner_restart_with_buffer_and_input_intact()
    {
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        var runtimeA = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var dtoA = await StartInteractiveSessionAsync(runtimeA, sessionId);
        int? childPid = dtoA.Pid;
        try
        {
            await runtimeA.SendInputAsync(sessionId, "echo before-restart-marker\r", CancellationToken.None);
            await WaitForSnapshotAsync(runtimeA, sessionId, text => text.Contains("before-restart-marker"));
            var lastSeqBeforeRestart = runtimeA.Get(sessionId).LastSequence;

            // "Runner dies": all pipes drop, hosts keep running.
            await runtimeA.DisposeAsync();
            Process.GetProcessById(childPid!.Value).HasExited.ShouldBeFalse();

            // "Runner restarts": fresh runtime adopts from manifests.
            var runtimeB = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var events = runtimeB.Subscribe(cts.Token);
            var adopted = await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), cts.Token);

            adopted.ShouldBe(1);
            var dtoB = runtimeB.Get(sessionId);
            dtoB.Status.ShouldBe("Running");
            dtoB.Pid.ShouldBe(childPid);

            // Interpretation rebuilt: pre-restart output is still in the snapshot.
            runtimeB.GetSnapshot(sessionId).RawOutput.ShouldContain("before-restart-marker");

            // Still fully interactive after adoption.
            await runtimeB.SendInputAsync(sessionId, "echo after-adopt-marker\r", CancellationToken.None);
            await WaitForSnapshotAsync(runtimeB, sessionId, text => text.Contains("after-adopt-marker"));

            // Sequence continuity: host-assigned sequences continue past the pre-restart high-water
            // mark with no duplicates of already-consumed output.
            var seqs = new List<long>();
            while (seqs.Count == 0 || !await IsQuietAsync(events))
            {
                if (!await events.WaitToReadAsync(cts.Token))
                    break;
                while (events.TryRead(out var evt))
                {
                    if (evt.EventName != SessionRunnerEventNames.SessionOutput)
                        continue;
                    var payload = System.Text.Json.JsonDocument.Parse(evt.Json).RootElement;
                    seqs.Add(payload.GetProperty("Sequence").GetInt64());
                }
            }

            seqs.ShouldNotBeEmpty();
            seqs.ShouldAllBe(seq => seq > lastSeqBeforeRestart);
            seqs.ShouldBeInOrder();

            var killed = await runtimeB.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
            killed.Status.ShouldBe("Exited");
            await runtimeB.DisposeAsync();
        }
        finally
        {
            KillBestEffort(childPid);
        }
    }

    [Test]
    public async Task Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code()
    {
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var manifestPath = PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId);

        var runtimeA = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var batch = Path.Combine(settings.SessionLogPath, "exit-soon.cmd");
        Directory.CreateDirectory(settings.SessionLogPath);
        File.WriteAllText(batch, "@echo off\r\necho exiting-soon\r\nping -n 3 127.0.0.1 > nul\r\nexit /b 7\r\n");

        var request = new RunnerLaunchRequest(
            sessionId, Cmd, ["/d", "/c", batch], new Dictionary<string, string>(),
            Path.GetTempPath(), Cols: 100, Rows: 25);
        await runtimeA.StartAsync(request, CancellationToken.None);

        var hostPid = PtyHostManifest.TryLoad(manifestPath)!.HostPid;

        // Runner "dies" while the child is still running; the child then exits unobserved.
        await runtimeA.DisposeAsync();
        await Task.Delay(TimeSpan.FromSeconds(4));

        var runtimeB = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var events = runtimeB.Subscribe(cts.Token);
        await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), cts.Token);

        // The runner collected the REAL exit recorded by the lingering host - not ProcessVanished.
        var dto = runtimeB.Get(sessionId);
        dto.Status.ShouldBe("Exited");
        dto.ExitCode.ShouldBe(7);
        dto.ExitReason.ShouldBe("ProcessExited");

        // The missed SessionExited event was published for consumers.
        var sawExit = false;
        while (!sawExit && await events.WaitToReadAsync(cts.Token))
        {
            while (events.TryRead(out var evt))
            {
                if (evt.EventName == SessionRunnerEventNames.SessionExited && evt.Json.Contains(sessionId.ToString()))
                {
                    sawExit = true;
                    break;
                }
            }
        }

        sawExit.ShouldBeTrue();

        // Shutdown ack: the host deletes its manifest and exits.
        await WaitUntilAsync(() => !File.Exists(manifestPath), TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => !IsProcessAlive(hostPid), TimeSpan.FromSeconds(10));
        await runtimeB.DisposeAsync();
    }

    [Test]
    public async Task Dead_host_is_registered_as_exited_ProcessVanished()
    {
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var manifestPath = PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId);

        var runtimeA = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var dto = await StartInteractiveSessionAsync(runtimeA, sessionId);
        await runtimeA.DisposeAsync();

        // Kill the host cold (its ConPTY child dies with it) - the crash scenario.
        var hostPid = PtyHostManifest.TryLoad(manifestPath)!.HostPid;
        Process.GetProcessById(hostPid).Kill(entireProcessTree: true);
        await WaitUntilAsync(() => !IsProcessAlive(hostPid), TimeSpan.FromSeconds(10));

        var runtimeB = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var adopted = await runtimeB.AdoptOrphanedHostsAsync(
            new SystemProcessLivenessProbe(), CancellationToken.None);

        adopted.ShouldBe(0);
        var after = runtimeB.Get(sessionId);
        after.Status.ShouldBe("Exited");
        after.ExitReason.ShouldBe("ProcessVanished");
        File.Exists(manifestPath).ShouldBeFalse();

        await runtimeB.DisposeAsync();
        KillBestEffort(dto.Pid);
    }

    // ---------- helpers ----------

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-adoption-tests-{Guid.NewGuid():N}"),
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
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(runtime.GetSnapshot(sessionId).RawOutput))
                return;
            await Task.Delay(100);
        }

        throw new System.TimeoutException(
            $"Snapshot predicate not satisfied. Snapshot: {runtime.GetSnapshot(sessionId).RawOutput}");
    }

    private static async Task<bool> IsQuietAsync(
        System.Threading.Channels.ChannelReader<RunnerServerSentEvent> events)
    {
        // "Quiet" = no further event arrives within a short window.
        var quiet = Task.Delay(750);
        var read = events.WaitToReadAsync().AsTask();
        return await Task.WhenAny(quiet, read) == quiet;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(200);
        }

        throw new System.TimeoutException("Condition not reached within timeout.");
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void KillBestEffort(int? pid)
    {
        if (pid is not int id)
            return;
        try
        {
            Process.GetProcessById(id).Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }
}
