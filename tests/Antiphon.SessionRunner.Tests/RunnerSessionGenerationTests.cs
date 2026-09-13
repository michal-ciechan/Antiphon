using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.PtyHost.Protocol;
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

    [Test]
    public async Task C502_V26_exited_manifest_adoption_for_A_publishes_A_while_B_is_registered()
    {
        var logRoot = TestSessionLogRoot.Create("c502-v26-manifest");
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02, CpuWatchdogEnabled = false }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-5));
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var events = runtime.Subscribe(cts.Token);
        await runtime.StartAsync(new RunnerLaunchRequest(
            sessionId, Cmd, ["/d", "/q", "/k", "@echo off & prompt $G"], new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, AcceptedStartedAt: generationB), cts.Token);

        var settings = new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02 };
        Directory.CreateDirectory(settings.PtyHostManifestDir);
        new PtyHostManifest
        {
            SessionId = sessionId,
            PipeName = "gone",
            HostPid = 1,
            HostStartTimeUtc = generationA,
            CreatedAtUtc = generationA,
            ExitCode = 1,
            ExitReason = "KilledByRequest",
            ExitedAtUtc = DateTime.UtcNow,
            AcceptedStartedAt = generationA,
        }.SaveAtomic(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId));

        await runtime.AdoptOrphanedHostsAsync(new StubProbe(true), cts.Token);
        var payload = await ReadExitAsync(events, cts.Token);
        payload.AcceptedStartedAt.ShouldBe(generationA);
        runtime.Get(sessionId).Status.ShouldBe("Running");
        runtime.Get(sessionId).AcceptedStartedAt.ShouldBe(generationB);
        KillBestEffort(runtime.Get(sessionId).Pid);
    }

    [Test]
    public async Task C502_V26_pending_and_terminal_herdr_objects_publish_their_own_generation()
    {
        var logRoot = TestSessionLogRoot.Create("c502-v26-herdr");
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02, CpuWatchdogEnabled = false }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-5));
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await runtime.StartAsync(new RunnerLaunchRequest(
            sessionId, Cmd, ["/d", "/q", "/k", "@echo off & prompt $G"], new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, AcceptedStartedAt: generationB), cts.Token);

        var sidecar = new HerdrPaneSidecar
        {
            SessionId = sessionId,
            WorkspaceKey = "none",
            WorkspaceId = "w",
            TabId = "t",
            PaneId = "p",
            ChildPid = 4242,
            LaunchedAtUtc = generationA,
            UpdatedAtUtc = generationA,
            AcceptedStartedAt = generationA,
        };
        var hub = new SessionRunnerEventHub();
        var producerEvents = hub.Subscribe(cts.Token);
        var producerSettings = new SessionRunnerSettings { SessionLogPath = logRoot };

        _ = SessionRunnerRuntime.RunnerSession.CreateAdoptedHerdrExited(
            sidecar, producerSettings, hub, NullLogger<SessionRunnerRuntime>.Instance,
            HerdrExitReasons.RestartPresumedDead);
        (await ReadExitAsync(producerEvents, cts.Token)).AcceptedStartedAt.ShouldBe(generationA);

        var pending = SessionRunnerRuntime.RunnerSession.CreatePendingHerdr(
            sidecar, producerSettings, hub, NullLogger<SessionRunnerRuntime>.Instance, new StubProbe(true));
        pending.CompletePendingAsExited(HerdrExitReasons.RestartPresumedDead, 1);
        (await ReadExitAsync(producerEvents, cts.Token)).AcceptedStartedAt.ShouldBe(generationA);

        runtime.Get(sessionId).AcceptedStartedAt.ShouldBe(generationB);
        runtime.Get(sessionId).Status.ShouldBe("Running");
        KillBestEffort(runtime.Get(sessionId).Pid);
    }

    [Test]
    public async Task C502_V25_real_runner_exe_advertises_the_capability_echoes_and_refuses_a_mismatched_kill()
    {
        var logRoot = TestSessionLogRoot.Create("c502-v25");
        var settings = new SessionRunnerSettings
        {
            SessionLogPath = logRoot,
            PtyHostLingerHours = 0.02,
            CpuWatchdogEnabled = false,
        };
        await using var runner = await LocalHttpRunner.StartAsync(settings);
        var capabilities = (await runner.Http.GetFromJsonAsync<RunnerCapabilitiesDto>("/capabilities"))!;
        capabilities.Features.ShouldContain(RunnerCapabilityFeatures.SessionGenerationV1);

        var sessionId = Guid.NewGuid();
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow);
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow.AddMinutes(1));
        using var created = await runner.Http.PostAsJsonAsync("/sessions", new RunnerLaunchRequest(
            sessionId, Cmd, ["/d", "/q", "/k", "@echo off & prompt $G"], new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, AcceptedStartedAt: generationA));
        created.IsSuccessStatusCode.ShouldBeTrue(await created.Content.ReadAsStringAsync());
        var dto = (await created.Content.ReadFromJsonAsync<RunnerSessionDto>())!;
        dto.AcceptedStartedAt.ShouldBe(generationA);
        dto.Pid.ShouldNotBeNull();

        using var mismatch = await runner.Http.PostAsJsonAsync(
            $"/sessions/{sessionId:D}/kill-generation",
            new RunnerKillGenerationRequest(generationB));
        mismatch.IsSuccessStatusCode.ShouldBeTrue();
        var mismatchResult = (await mismatch.Content.ReadFromJsonAsync<RunnerKillGenerationResult>())!;
        mismatchResult.Killed.ShouldBeFalse();
        mismatchResult.Outcome.ShouldBe(KillGenerationOutcomes.Mismatch);
        Process.GetProcessById(dto.Pid.Value).HasExited.ShouldBeFalse();

        using var sse = new HttpClient { BaseAddress = runner.Address, Timeout = TimeSpan.FromSeconds(20) };
        using var events = await sse.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead);
        events.EnsureSuccessStatusCode();
        using var match = await runner.Http.PostAsJsonAsync(
            $"/sessions/{sessionId:D}/kill-generation",
            new RunnerKillGenerationRequest(generationA));
        var killed = (await match.Content.ReadFromJsonAsync<RunnerKillGenerationResult>())!;
        killed.Killed.ShouldBeTrue();
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync());
        string? name = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        RunnerSessionExitedEvent? payload = null;
        while (payload is null && DateTime.UtcNow < deadline)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            if (line.StartsWith("event: ", StringComparison.Ordinal))
                name = line["event: ".Length..].Trim();
            else if (line.StartsWith("data: ", StringComparison.Ordinal)
                     && name == SessionRunnerEventNames.SessionExited)
            {
                payload = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(
                    line["data: ".Length..], new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
        }

        payload.ShouldNotBeNull();
        payload!.AcceptedStartedAt.ShouldBe(generationA);
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
