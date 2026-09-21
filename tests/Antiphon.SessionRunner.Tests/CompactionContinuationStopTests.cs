using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[NotInParallel("ClaudeConfigDirEnv")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CompactionContinuationStopTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Late_output_prevents_conditional_stop()
    {
        await using var world = await StopWorld.StartAsync();
        world.Runtime.CompactionStopBeforeFinalCheck = _ => AppendAsync(world.TranscriptPath, AssistantLine());
        var result = await world.Runtime.StopCompactionContinuationAsync(
            world.SessionId, world.Request(), TimeSpan.FromSeconds(5), CancellationToken.None);
        result.ConfirmsExit.ShouldBeFalse();
        world.Runtime.CompactionStopSignals.Count.ShouldBe(0);
        world.Runtime.Get(world.SessionId).Status.ShouldNotBe("Exited");
    }

    [Test]
    public async Task Replacement_generation_is_never_stopped()
    {
        await using var world = await StopWorld.StartAsync();
        var stale = world.Request() with { ExpectedAcceptedStartedAt = world.Generation.AddMinutes(-5) };
        var result = await world.Runtime.StopCompactionContinuationAsync(
            world.SessionId, stale, TimeSpan.FromSeconds(5), CancellationToken.None);
        result.Outcome.ShouldBe(CompactionStopOutcomes.Mismatch);
        world.Runtime.CompactionStopSignals.Count.ShouldBe(0);
        world.Runtime.Get(world.SessionId).Status.ShouldNotBe("Exited");
    }

    [Test]
    public async Task Observed_output_wins_the_termination_race()
    {
        await using var world = await StopWorld.StartAsync();
        world.Runtime.CompactionStopBeforeSignal = _ => AppendAsync(world.TranscriptPath, AssistantLine());
        var result = await world.Runtime.StopCompactionContinuationAsync(
            world.SessionId, world.Request(), TimeSpan.FromSeconds(5), CancellationToken.None);
        result.ConfirmsExit.ShouldBeFalse();
        world.Runtime.CompactionStopSignals.Count.ShouldBe(0);
    }

    [Test]
    public async Task Late_transcript_revision_prevents_stop()
    {
        await using var world = await StopWorld.StartAsync();
        world.Runtime.CompactionStopBeforeFinalCheck = _ => AppendAsync(
            world.TranscriptPath, """{"type":"ai-title","uuid":"title-1","aiTitle":"harmless"}""");
        var result = await world.Runtime.StopCompactionContinuationAsync(
            world.SessionId, world.Request(), TimeSpan.FromSeconds(5), CancellationToken.None);
        result.ConfirmsExit.ShouldBeFalse();
        world.Runtime.CompactionStopSignals.Count.ShouldBe(0);
    }

    [Test]
    public async Task Late_binding_revision_prevents_stop()
    {
        await using var world = await StopWorld.StartAsync();
        world.Runtime.CompactionStopBeforeFinalCheck = async _ =>
        {
            var tailer = (TranscriptTailer)world.Runtime.TailerFor(world.SessionId)!;
            var before = tailer.BindingIdentity;
            tailer.NotifyClaimRevoked(world.TranscriptPath, Guid.NewGuid());
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (tailer.BindingIdentity == before && DateTime.UtcNow < deadline)
                await Task.Delay(50);
        };
        var result = await world.Runtime.StopCompactionContinuationAsync(
            world.SessionId, world.Request(), TimeSpan.FromSeconds(5), CancellationToken.None);
        result.ConfirmsExit.ShouldBeFalse();
        world.Runtime.CompactionStopSignals.Count.ShouldBe(0);
        world.Runtime.Get(world.SessionId).Status.ShouldNotBe("Exited");
    }

    [Test]
    public async Task Duplicate_stop_attempt_has_one_effect()
    {
        await using var world = await StopWorld.StartAsync();
        var request = world.Request();
        var first = await world.Runtime.StopCompactionContinuationAsync(
            world.SessionId, request, TimeSpan.FromSeconds(5), CancellationToken.None);
        first.ConfirmsExit.ShouldBeTrue();
        var second = await world.Runtime.StopCompactionContinuationAsync(
            world.SessionId, request, TimeSpan.FromSeconds(5), CancellationToken.None);
        second.ConfirmsExit.ShouldBeTrue();
        world.Runtime.CompactionStopSignals.Count.ShouldBe(1);
    }

    private static Task AppendAsync(string path, string jsonLine) =>
        File.AppendAllTextAsync(path, jsonLine + "\n");

    private static string AssistantLine() =>
        """{"type":"assistant","uuid":"asst-1","message":{"role":"assistant","content":[{"type":"text","text":"progress"}]}}""";

    private sealed class StopWorld : IAsyncDisposable
    {
        public SessionRunnerRuntime Runtime { get; }
        public Guid SessionId { get; }
        public DateTime Generation { get; }
        public string TranscriptPath { get; }
        private readonly string _configDir;
        private readonly string? _previousConfig;
        private readonly CompactionTailObservation _observation;

        private StopWorld(
            SessionRunnerRuntime runtime, Guid sessionId, DateTime generation, string path,
            string configDir, string? previous, CompactionTailObservation observation)
        {
            Runtime = runtime;
            SessionId = sessionId;
            Generation = generation;
            TranscriptPath = path;
            _configDir = configDir;
            _previousConfig = previous;
            _observation = observation;
        }

        public CompactionContinuationStopRequest Request() => new(
            Guid.NewGuid(),
            Generation,
            _observation.NativeBoundaryId!,
            _observation.NativeContinuationId!,
            10,
            _observation.BindingIdentity!,
            _observation.TranscriptRevision,
            _observation.OutputRevision);

        public static async Task<StopWorld> StartAsync()
        {
            var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            var configDir = Path.Combine(Path.GetTempPath(), $"antiphon-c79-stop-{Guid.NewGuid():N}");
            var projectDir = Path.Combine(configDir, "projects", "cwd");
            Directory.CreateDirectory(projectDir);
            var sessionId = Guid.NewGuid();
            var path = Path.Combine(projectDir, sessionId.ToString("D") + ".jsonl");
            await File.WriteAllTextAsync(path, string.Join('\n', new[]
            {
                """{"type":"user","uuid":"prompt-1","message":{"role":"user","content":"check the board"}}""",
                """{"type":"system","subtype":"compact_boundary","uuid":"boundary-1","compactMetadata":{"trigger":"auto"}}""",
                "{\"type\":\"user\",\"uuid\":\"cont-1\",\"message\":{\"role\":\"user\",\"content\":\"" + TranscriptKinds.CompactionContinuationPromptPrefix + " summary\"}}",
            }) + "\n");
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
            var logRoot = TestSessionLogRoot.Create("c79-stop");
            var runtime = new SessionRunnerRuntime(
                Options.Create(new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02, CpuWatchdogEnabled = false }),
                NullLogger<SessionRunnerRuntime>.Instance);
            var generation = SessionGeneration.Normalize(DateTime.UtcNow);
            await runtime.StartAsync(new RunnerLaunchRequest(
                sessionId, Cmd, ["/d", "/q", "/k", "@echo off & prompt $G"], new Dictionary<string, string>(),
                Path.GetTempPath(), 80, 24, TranscriptEnabled: true, TranscriptFormat: TranscriptFormats.Claude,
                AcceptedStartedAt: generation), CancellationToken.None);
            CompactionTailObservation? observation = null;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                observation = await runtime.ObserveCompactionAsync(sessionId, CancellationToken.None);
                if (observation.IsSuccessful && observation.NativeBoundaryId == "boundary-1")
                    break;
                await Task.Delay(50);
            }

            observation.ShouldNotBeNull();
            observation!.IsSuccessful.ShouldBeTrue();
            return new StopWorld(runtime, sessionId, generation, path, configDir, previous, observation);
        }

        public async ValueTask DisposeAsync()
        {
            try { await Runtime.KillAsync(SessionId, TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch { /* already stopped */ }
            await Runtime.DisposeAsync();
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _previousConfig);
            try { Directory.Delete(_configDir, true); } catch { /* temp */ }
        }
    }
}
