using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

public sealed class PhoneHomeRuntimeAdapter : IPhoneHomeRuntimeSurface
{
    private readonly SessionRunnerRuntime _runtime;
    private readonly RunnerBuildDto _build;

    public PhoneHomeRuntimeAdapter(SessionRunnerRuntime runtime, RunnerBuildDto build)
    {
        _runtime = runtime;
        _build = build;
    }

    public int OwnedSessionCount => _runtime.LiveSessionCount;

    public RunnerCapabilitiesDto Capabilities()
    {
        var decision = PtyBackendPolicy.Resolve();
        IReadOnlyList<string> backends = [SessionBackends.PtyHost];
        IReadOnlyList<string> features =
        [
            GrokRulesTransport.Capability,
            RunnerCapabilityFeatures.SessionGenerationV1,
            RunnerCapabilityFeatures.ConditionalMaintenanceInputV1,
            RunnerCapabilityFeatures.CompactionContinuationStopV1,
        ];
        // CARD-0604 D-17 (Cut B). Phone-home advertises the LINUX custody backend, and only
        // when the runner's live probe passed. It never advertises windows-job-v1: a Windows
        // answer reaching the server over this lane is precisely how a Linux execution would be
        // admitted with a receipt method it could not honestly produce (R-5, G-28). Cut A had
        // nothing to advertise at all, so this read `null` unconditionally.
        var custody = _runtime.VerificationCustodyBackend == VerificationCustodyBackends.LinuxCgroup
            ? VerificationCustodyBackends.LinuxCgroup : null;
        if (custody is not null)
            features = [.. features, RunnerCapabilityFeatures.VerificationCustodyV1];
        features = [.. features, RunnerCapabilityFeatures.RequiredPlatformV1];
        return new RunnerCapabilitiesDto(
            decision.Backend.ToString(), decision.Requested, decision.Reason, decision.FellBack,
            SessionRunnerRuntime.SupportedTranscriptFormats, _build, backends,
            Version: _build.CommitSha ?? "unknown",
            Features: features, VerificationCustodyBackend: custody,
            RunnerStoreId: _runtime.RunnerStoreId,
            Platform: RunnerPlatformWire.FromOperatingSystem());
    }

    public string? VerificationCustodyBackend =>
        _runtime.VerificationCustodyBackend == VerificationCustodyBackends.LinuxCgroup
            ? VerificationCustodyBackends.LinuxCgroup : null;

    public Guid RunnerStoreId => _runtime.RunnerStoreId;

    public Task<VerificationCustodyStatus> ReadCustodyAsync(
        VerificationExecutionBinding binding, bool seal, CancellationToken ct) =>
        _runtime.ReadCustodyAsync(binding, seal, ct);

    public string Health() => "Healthy";
    public IReadOnlyList<RunnerSessionDto> List() => _runtime.List();
    public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => _runtime.GetAsync(sessionId, ct);
    public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) =>
        _runtime.StartAsync(request, ct);
    public RunnerBufferDto GetBuffer(Guid sessionId) => _runtime.GetBuffer(sessionId);
    public RunnerSnapshotDto GetSnapshot(Guid sessionId) => _runtime.GetSnapshot(sessionId);
    public RunnerTranscriptDto GetTranscript(Guid sessionId) => _runtime.GetTranscript(sessionId);
    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _runtime.SendInputAsync(sessionId, input, ct);
    public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
        _runtime.SendConditionalInputAsync(sessionId, request, ct);
    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        _runtime.ClearLiveBufferAsync(sessionId, ct);
    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        _runtime.ResizeAsync(sessionId, cols, rows, ct);
    public Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
        _runtime.KillGenerationAsync(sessionId, expectedAcceptedStartedAt, TimeSpan.FromSeconds(15), ct);

    public Task<RunnerSessionDto> ReleaseSlotAsync(Guid sessionId, string reason, CancellationToken ct) =>
        _runtime.ReleaseSlotAsync(sessionId, reason, TimeSpan.FromSeconds(5), ct);

    public Task<CompactionContinuationStopResult> StopCompactionContinuationAsync(
        Guid sessionId, CompactionContinuationStopRequest request, CancellationToken ct) =>
        _runtime.StopCompactionContinuationAsync(sessionId, request, TimeSpan.FromSeconds(5), ct);

    public Task<CompactionTailObservation> ObserveCompactionAsync(Guid sessionId, CancellationToken ct) =>
        _runtime.ObserveCompactionAsync(sessionId, ct);
}
