using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RunnerSessionGenerationTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task C502_V27_fast_exit_before_Start_returns_carries_the_generation()
    {
        var logRoot = TestSessionLogRoot.Create("c502-v27");
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02, CpuWatchdogEnabled = false }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = runtime.Subscribe(cts.Token);
        var dto = await runtime.StartAsync(new RunnerLaunchRequest(
            sessionId, Cmd, ["/d", "/c", "exit 3"], new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, AcceptedStartedAt: generation), cts.Token);

        dto.AcceptedStartedAt.ShouldBe(generation);
        RunnerSessionExitedEvent? payload = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (payload is null && DateTime.UtcNow < deadline)
        {
            while (events.TryRead(out var evt))
            {
                if (evt.EventName != SessionRunnerEventNames.SessionExited)
                    continue;
                payload = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(
                    evt.Json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            if (payload is null)
                await Task.Delay(50, cts.Token);
        }

        payload.ShouldNotBeNull();
        payload!.AcceptedStartedAt.ShouldBe(generation);
        payload.ExitCode.ShouldBe(3);
        runtime.Get(sessionId).AcceptedStartedAt.ShouldBe(generation);
    }

    [Test]
    public async Task C502_V26_kill_and_relaunch_publish_their_own_generations()
    {
        var logRoot = TestSessionLogRoot.Create("c502-v26");
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02, CpuWatchdogEnabled = false }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow);
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow.AddMinutes(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var events = runtime.Subscribe(cts.Token);

        await runtime.StartAsync(new RunnerLaunchRequest(
            sessionId, Cmd, ["/d", "/q", "/k", "@echo off & prompt $G"], new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, AcceptedStartedAt: generationA), cts.Token);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), cts.Token);

        var payloadA = await ReadExitAsync(events, cts.Token);
        payloadA.AcceptedStartedAt.ShouldBe(generationA);

        await runtime.StartAsync(new RunnerLaunchRequest(
            sessionId, Cmd, ["/d", "/q", "/k", "@echo off & prompt $G"], new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, AcceptedStartedAt: generationB), cts.Token);
        runtime.Get(sessionId).AcceptedStartedAt.ShouldBe(generationB);

        runtime.SweepVanishedSessions(new StubProbe(false));
        var payloadB = await ReadExitAsync(events, cts.Token);
        payloadB.AcceptedStartedAt.ShouldBe(generationB);
        KillBestEffort(runtime.Get(sessionId).Pid);
    }

    private static async Task<RunnerSessionExitedEvent> ReadExitAsync(
        System.Threading.Channels.ChannelReader<RunnerServerSentEvent> events, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            while (events.TryRead(out var evt))
            {
                if (evt.EventName != SessionRunnerEventNames.SessionExited)
                    continue;
                var payload = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(
                    evt.Json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (payload is not null)
                    return payload;
            }

            await Task.Delay(50, ct);
        }

        throw new TimeoutException("no SessionExited payload");
    }

    private static void KillBestEffort(int? pid)
    {
        if (pid is not int live)
            return;
        try { Process.GetProcessById(live).Kill(entireProcessTree: true); } catch { /* already gone */ }
    }

    private sealed class StubProbe(bool alive) : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => alive;
        public string? TryGetProcessName(int pid) => "cmd";
        public DateTime? TryGetStartTimeUtc(int pid) => DateTime.UtcNow.AddMinutes(-1);
    }
}
