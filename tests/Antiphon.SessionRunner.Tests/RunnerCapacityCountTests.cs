using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0653: capacity counts live or occupied sessions. An exited record re-adopted from a
/// dead pty-host manifest must not fill the seat, and a later release must drop that record
/// so the next start does not adopt it again.
/// </summary>
[Category("Unit")]
public class RunnerCapacityCountTests
{
    [Test]
    public async Task Dead_host_adoption_does_not_occupy_a_slot_and_release_forgets_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c653-cap-" + Guid.NewGuid().ToString("N"));
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostLingerHours = 0.02 };
        var sessionId = Guid.NewGuid();
        Directory.CreateDirectory(settings.PtyHostManifestDir);
        new PtyHostManifest
        {
            SessionId = sessionId,
            PipeName = "dead-host",
            HostPid = 1,
            HostStartTimeUtc = DateTime.UtcNow.AddHours(-1),
            ExitReason = "ProcessVanished",
            ExitedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
        }.SaveAtomic(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId));

        var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        try
        {
            await runtime.AdoptOrphanedHostsAsync(new DeadProbe(), CancellationToken.None);
            runtime.List().ShouldContain(s => s.SessionId == sessionId && s.Status == "Exited");
            var adapter = new PhoneHomeRuntimeAdapter(
                runtime, new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow));
            adapter.OwnedSessionCount.ShouldBe(0, "an exited adoption must not count toward phone-home capacity");

            var probe = new AdmitProbe(adapter);
            var dispatcher = new PhoneHomeCommandDispatcher(
                probe,
                new PhoneHomeSettings
                {
                    Enabled = true,
                    AllowedCwd = "/work",
                    Capacity = 1,
                    ClaudeAuthProbeEnabled = false,
                    GrokAuthProbeEnabled = false,
                });
            var admitted = await dispatcher.DispatchAsync(
                new PhoneHomeFrame(
                    PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
                    System.Text.Json.JsonSerializer.SerializeToElement(
                        new RunnerLaunchRequest(Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24),
                        PhoneHomeFraming.Json)),
                CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result);
            admitted.ErrorCode.ShouldBeNull();
            probe.Started.ShouldBeTrue("the capacity gate runs before StartAsync and must admit");
            adapter.OwnedSessionCount.ShouldBe(0, "the exited record still must not reserve a seat");

            var manifest = PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId);
            new PtyHostManifest
            {
                SessionId = sessionId,
                PipeName = "dead-host",
                HostPid = 1,
                HostStartTimeUtc = DateTime.UtcNow.AddHours(-1),
                ExitReason = "ProcessVanished",
                ExitedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            }.SaveAtomic(manifest);
            var released = await runtime.ReleaseSlotAsync(sessionId, "operator evict", TimeSpan.FromSeconds(1), CancellationToken.None);
            released.SessionId.ShouldBe(sessionId);
            runtime.List().ShouldNotContain(s => s.SessionId == sessionId);
            File.Exists(manifest).ShouldBeFalse();
            File.ReadAllText(Path.Combine(root, "slot-releases.jsonl")).ShouldContain(sessionId.ToString("D"));

            var again = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
            try
            {
                await again.AdoptOrphanedHostsAsync(new DeadProbe(), CancellationToken.None);
                again.List().ShouldNotContain(s => s.SessionId == sessionId);
            }
            finally
            {
                await again.DisposeAsync();
            }
        }
        finally
        {
            await runtime.DisposeAsync();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class AdmitProbe(PhoneHomeRuntimeAdapter adapter) : IPhoneHomeRuntimeSurface
    {
        public bool Started { get; private set; }
        public int OwnedSessionCount => adapter.OwnedSessionCount;
        public RunnerCapabilitiesDto Capabilities() => adapter.Capabilities();
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => adapter.List();
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => adapter.GetAsync(sessionId, ct);
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            Started = true;
            return Task.FromResult(new RunnerSessionDto(request.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0));
        }
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => new(sessionId, "", "", 0, DateTime.UtcNow);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Written, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }

    private sealed class DeadProbe : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => false;
        public string? TryGetProcessName(int pid) => "Antiphon.PtyHost";
        public DateTime? TryGetStartTimeUtc(int pid) => DateTime.UtcNow.AddHours(-1);
    }
}
