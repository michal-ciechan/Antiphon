using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// End-to-end coverage of the CPU spin watchdog against a REAL pty-host session (cmd.exe as the
/// guinea-pig child) with a fabricated idle transcript and a scripted CPU probe. Pins the live
/// incident 2026-08-08: a session whose transcript said the turn ended, with claude.exe pegging a
/// core at the prompt, must be killed with exit reason CpuSpinKilled — while working sessions and
/// fresh sessions are never touched.
/// </summary>
[NotInParallel("ClaudeConfigDirEnv")] // mutates the process-wide CLAUDE_CONFIG_DIR variable
public class SessionCpuWatchdogTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Idle_session_burning_a_core_is_killed_with_CpuSpinKilled()
    {
        var fixture = await StartSessionWithTranscriptAsync(
            IdleTranscriptLines, IdleTranscriptEntryCount, minUptimeSeconds: 0);
        try
        {
            // Transcript proves idle; three sweeps at 100% of a core cover the 10s window:
            // baseline, hot (5s), hot (10s => kill).
            await fixture.SweepAsync(cpuSecondsDelta: 0);
            await fixture.SweepAsync(cpuSecondsDelta: 5);
            fixture.Runtime.Get(fixture.SessionId).Status.ShouldBe(
                "Running", "the sustained window is not met yet");
            await fixture.SweepAsync(cpuSecondsDelta: 5);

            var dto = fixture.Runtime.Get(fixture.SessionId);
            dto.Status.ShouldBe("Exited");
            dto.ExitReason.ShouldBe(RunnerExitReasons.CpuSpinKilled);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Test]
    public async Task Working_session_is_never_killed_no_matter_how_hot()
    {
        // The transcript has activity but NO completed turn — the watchdog cannot prove idle.
        var fixture = await StartSessionWithTranscriptAsync(
            WorkingTranscriptLines, WorkingTranscriptEntryCount, minUptimeSeconds: 0);
        try
        {
            for (var i = 0; i < 5; i++)
                await fixture.SweepAsync(cpuSecondsDelta: 5);

            fixture.Runtime.Get(fixture.SessionId).Status.ShouldBe("Running");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Test]
    public async Task Session_within_the_min_uptime_grace_is_left_alone()
    {
        // Idle transcript, pegged CPU — but the session is younger than the grace (startup and
        // --resume history loads are legitimately hot).
        var fixture = await StartSessionWithTranscriptAsync(
            IdleTranscriptLines, IdleTranscriptEntryCount, minUptimeSeconds: 3600);
        try
        {
            for (var i = 0; i < 5; i++)
                await fixture.SweepAsync(cpuSecondsDelta: 5);

            fixture.Runtime.Get(fixture.SessionId).Status.ShouldBe("Running");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    // ---------- fixture ----------

    // UserPrompt + AssistantText + TurnEnd (the assistant line yields two entries).
    private const int IdleTranscriptEntryCount = 3;

    private const int WorkingTranscriptEntryCount = 1;

    private static string[] IdleTranscriptLines(Guid sessionId, string cwd) =>
    [
        UserLine("u1", cwd, "do the thing"),
        AssistantEndTurnLine("a1", cwd, "done"),
    ];

    private static string[] WorkingTranscriptLines(Guid sessionId, string cwd) =>
    [
        UserLine("u1", cwd, "do the thing"),
    ];

    private static async Task<WatchdogFixture> StartSessionWithTranscriptAsync(
        Func<Guid, string, string[]> transcriptLines, int expectedEntries, int minUptimeSeconds)
    {
        var logRoot = TestSessionLogRoot.Create("cpu-watchdog-tests");
        var configDir = Path.Combine(logRoot, "claude-config");
        var projectDir = Path.Combine(configDir, "projects", "some-encoded-cwd");
        var cwd = Path.Combine(logRoot, "agent-cwd");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(cwd);

        var sessionId = Guid.NewGuid();
        var lines = transcriptLines(sessionId, cwd);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, sessionId.ToString("D") + ".jsonl"),
            string.Join("\n", lines) + "\n");

        var settings = new SessionRunnerSettings
        {
            SessionLogPath = logRoot,
            PtyHostLingerHours = 0.02,
            CpuWatchdogHotCpuPercent = 50,
            CpuWatchdogSustainedSeconds = 10,
            CpuWatchdogMinUptimeSeconds = minUptimeSeconds,
        };
        var runtime = new SessionRunnerRuntime(
            Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);

        // The tailer resolves its projects root from CLAUDE_CONFIG_DIR when it starts — set it
        // BEFORE launching the session and restore it on dispose.
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
        RunnerSessionDto dto;
        try
        {
            dto = await runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId,
                    Cmd,
                    ["/d", "/q", "/k", "@echo off & prompt $G"],
                    new Dictionary<string, string>(),
                    cwd,
                    Cols: 100,
                    Rows: 25,
                    TranscriptEnabled: true),
                CancellationToken.None);

            for (var attempt = 0; attempt < 20 && dto.Status != "Running"; attempt++)
            {
                await Task.Delay(100);
                dto = runtime.Get(sessionId);
            }
            dto.Status.ShouldBe("Running");

            // Wait for the tailer to ingest the WHOLE pre-written transcript before any sweep
            // judges it (a partially ingested assistant line would be missing its TurnEnd).
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline
                && runtime.GetTranscript(sessionId).Entries.Count < expectedEntries)
            {
                await Task.Delay(200);
            }
            runtime.GetTranscript(sessionId).Entries.Count.ShouldBeGreaterThanOrEqualTo(
                expectedEntries, "the tailer must ingest the fabricated transcript before the test sweeps");
        }
        catch
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
            await runtime.KillAllAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await runtime.DisposeAsync();
            throw;
        }

        var probe = new ScriptedCpuProbe();
        var time = new FakeTime();
        var watchdog = new SessionCpuWatchdogService(
            runtime, probe, Options.Create(settings), time,
            NullLogger<SessionCpuWatchdogService>.Instance);

        return new WatchdogFixture(runtime, watchdog, probe, time, sessionId, dto.Pid, dto.HostPid, logRoot);
    }

    private sealed class WatchdogFixture(
        SessionRunnerRuntime runtime,
        SessionCpuWatchdogService watchdog,
        ScriptedCpuProbe probe,
        FakeTime time,
        Guid sessionId,
        int? childPid,
        int? hostPid,
        string logRoot) : IAsyncDisposable
    {
        public SessionRunnerRuntime Runtime => runtime;
        public Guid SessionId => sessionId;

        /// <summary>Advances fake time by 5s, adds the given CPU seconds to the probe, sweeps once.</summary>
        public async Task SweepAsync(double cpuSecondsDelta)
        {
            probe.Current += TimeSpan.FromSeconds(cpuSecondsDelta);
            await watchdog.SweepOnceAsync(CancellationToken.None);
            time.Now += TimeSpan.FromSeconds(5);
        }

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
            await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, sessionId, hostPid);
            await runtime.DisposeAsync();
            if (childPid is int pid)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone — fine.
                }
            }

            try { Directory.Delete(logRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class ScriptedCpuProbe : IProcessCpuProbe
    {
        public TimeSpan Current { get; set; }

        public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => Current;
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string UserLine(string uuid, string cwd, string text) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "user",
            uuid,
            cwd,
            message = new { role = "user", content = text },
        });

    private static string AssistantEndTurnLine(string uuid, string cwd, string text) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "assistant",
            uuid,
            cwd,
            message = new
            {
                id = "msg_test_1",
                role = "assistant",
                stop_reason = "end_turn",
                content = new object[] { new { type = "text", text } },
            },
        });
}
