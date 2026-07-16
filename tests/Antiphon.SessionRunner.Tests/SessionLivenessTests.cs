using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// Pins the runner-side liveness backstop: a session whose OS process is gone must not stay
/// "Running". The real incident: a session sat Running on a dead PID for a week because the exit
/// was never observed, keeping its agent badged Working in the UI with no process behind it.
/// </summary>
[NotInParallel("SessionLiveness")]
public class SessionLivenessTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Sweep_marks_running_session_with_vanished_process_as_exited_and_publishes_the_missed_event()
    {
        var (runtime, _) = BuildRuntime();
        var sessionId = Guid.NewGuid();
        var dto = await StartLongLivedSessionAsync(runtime, sessionId);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var events = runtime.Subscribe(cts.Token);

            // The probe says the process is gone (regardless of the real one still running) —
            // exactly the state after a missed exit event.
            var marked = runtime.SweepVanishedSessions(new StubProbe(alive: false));

            marked.ShouldBe([sessionId]);
            var after = runtime.Get(sessionId);
            after.Status.ShouldBe("Exited");
            after.ExitReason.ShouldBe("ProcessVanished");

            var sawExitEvent = false;
            while (await events.WaitToReadAsync(cts.Token))
            {
                if (!events.TryRead(out var evt))
                    continue;
                if (evt.EventName == SessionRunnerEventNames.SessionExited
                    && evt.Json.Contains(sessionId.ToString()))
                {
                    sawExitEvent = true;
                    break;
                }
            }

            sawExitEvent.ShouldBeTrue("the sweep must emit the SessionExited event the observer missed");
        }
        finally
        {
            KillProcessBestEffort(dto.Pid);
        }
    }

    [Test]
    public async Task Sweep_leaves_sessions_with_live_processes_alone()
    {
        var (runtime, _) = BuildRuntime();
        var sessionId = Guid.NewGuid();
        var dto = await StartLongLivedSessionAsync(runtime, sessionId);
        try
        {
            var marked = runtime.SweepVanishedSessions(new StubProbe(alive: true));

            marked.ShouldBeEmpty();
            runtime.Get(sessionId).Status.ShouldBe("Running");
        }
        finally
        {
            KillProcessBestEffort(dto.Pid);
        }
    }

    [Test]
    public async Task Sweep_is_idempotent()
    {
        var (runtime, _) = BuildRuntime();
        var sessionId = Guid.NewGuid();
        var dto = await StartLongLivedSessionAsync(runtime, sessionId);
        try
        {
            runtime.SweepVanishedSessions(new StubProbe(alive: false)).ShouldBe([sessionId]);
            runtime.SweepVanishedSessions(new StubProbe(alive: false)).ShouldBeEmpty();
        }
        finally
        {
            KillProcessBestEffort(dto.Pid);
        }
    }

    [Test]
    public void System_probe_reports_the_current_process_alive()
    {
        using var current = Process.GetCurrentProcess();
        var probe = new SystemProcessLivenessProbe();

        probe.IsAlive(current.Id, current.StartTime.ToUniversalTime()).ShouldBeTrue();
    }

    [Test]
    public async Task System_probe_reports_an_exited_process_dead()
    {
        var psi = new ProcessStartInfo(Cmd, "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        var pid = process.Id;
        var startedAt = process.StartTime.ToUniversalTime();
        await process.WaitForExitAsync();

        new SystemProcessLivenessProbe().IsAlive(pid, startedAt).ShouldBeFalse();
    }

    [Test]
    public void System_probe_treats_a_recycled_pid_as_dead()
    {
        // The current process is alive under its PID — but if the session claims its process
        // started long BEFORE this one did, the PID has been recycled and the probe must say dead.
        using var current = Process.GetCurrentProcess();
        var probe = new SystemProcessLivenessProbe();

        var sessionStartedLongAgo = current.StartTime.ToUniversalTime().AddHours(-6);
        probe.IsAlive(current.Id, sessionStartedLongAgo).ShouldBeFalse();
    }

    // ---------- helpers ----------

    private static (SessionRunnerRuntime Runtime, string LogRoot) BuildRuntime()
    {
        var logRoot = Path.Combine(Path.GetTempPath(), $"antiphon-liveness-tests-{Guid.NewGuid():N}");
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = logRoot }),
            NullLogger<SessionRunnerRuntime>.Instance);
        return (runtime, logRoot);
    }

    private static async Task<RunnerSessionDto> StartLongLivedSessionAsync(
        SessionRunnerRuntime runtime, Guid sessionId)
    {
        // An interactive cmd prompt (the repo's canonical long-lived PTY guinea pig — see
        // AgentSessionServiceIntegrationTests): stays Running until the test kills it.
        var request = new RunnerLaunchRequest(
            sessionId,
            Cmd,
            ["/d", "/q", "/k", "@echo off & prompt $G"],
            new Dictionary<string, string>(),
            Path.GetTempPath(),
            Cols: 100,
            Rows: 25);
        var dto = await runtime.StartAsync(request, CancellationToken.None);

        // The spawn is real ConPTY; give the very first bootstrap a beat if needed.
        for (var attempt = 0; attempt < 20 && dto.Status != "Running"; attempt++)
        {
            await Task.Delay(100);
            dto = runtime.Get(sessionId);
        }

        dto.Status.ShouldBe(
            "Running",
            customMessage: $"session dto: {dto}; buffer: {runtime.GetBuffer(sessionId).Buffer}");
        return dto;
    }

    private static void KillProcessBestEffort(int? pid)
    {
        if (pid is not int id)
            return;
        try
        {
            using var process = Process.GetProcessById(id);
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone — fine.
        }
    }

    private sealed class StubProbe(bool alive) : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => alive;
    }
}
