using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

public sealed class PhoneHomeRuntimeAdapter : IPhoneHomeRuntimeSurface
{
    internal static IReadOnlyList<string> StaticFeatures { get; } =
    [
        GrokRulesTransport.Capability,
        RunnerCapabilityFeatures.SessionGenerationV1,
        RunnerCapabilityFeatures.ConditionalMaintenanceInputV1,
        RunnerCapabilityFeatures.CompactionContinuationStopV1,
        RunnerCapabilityFeatures.TerminalSeatReleaseV1,
        RunnerCapabilityFeatures.TerminalSeatDeliveryEvidenceV1,
        RunnerCapabilityFeatures.WorkspaceRepositoryV1,
        RunnerCapabilityFeatures.WorkspacePublishV1,
        CodexCliVersionProbe.Capability,
    ];
    private readonly SessionRunnerRuntime _runtime;
    private readonly RunnerBuildDto _build;

    public PhoneHomeRuntimeAdapter(SessionRunnerRuntime runtime, RunnerBuildDto build)
    {
        _runtime = runtime;
        _build = build;
    }

    public int OwnedSessionCount => _runtime.LiveSessionCount;
    public async Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request, CancellationToken ct) =>
        _runtime.CodexCliProbe is { } probe ? await probe.ProbeAsync(request, false, ct) : null;
    public async Task<int> KillAllAsync(TimeSpan timeout, CancellationToken ct) =>
        (await _runtime.KillAllAsync(timeout, ct)).Count;

    public RunnerCapabilitiesDto Capabilities()
    {
        var decision = _runtime.BackendDecision;
        IReadOnlyList<string> backends = [SessionBackends.PtyHost];
        IReadOnlyList<string> features = StaticFeatures;
        if (_runtime.SupportsWorkspacePark)
            features = [.. features, RunnerCapabilityFeatures.WorkspaceParkV1,
                RunnerCapabilityFeatures.WorkspaceRepositoryIdentityV1];
        if (_runtime.SupportsWorkspaceSourceModes)
            features = [.. features, RunnerCapabilityFeatures.WorkspaceParkSourceModesV1];
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
        // CARD-1153 D-3: advertised only while the evidence service is ready (adoption done, no latch).
        if (_runtime.AbsenceEvidenceReady)
            features = [.. features, RunnerAbsenceEvidence.Feature];
        var cli = _runtime.CodexCliProbe?.Snapshot;
        return new RunnerCapabilitiesDto(
            decision.Backend.ToString(), decision.Requested, decision.Reason, decision.FellBack,
            SessionRunnerRuntime.SupportedTranscriptFormats, _build, backends,
            Version: _build.CommitSha ?? "unknown",
            Features: features, VerificationCustodyBackend: custody,
            RunnerStoreId: _runtime.RunnerStoreId,
            Platform: RunnerPlatformWire.FromOperatingSystem(),
            CodexCliVersion: cli?.CodexCliVersion,
            CodexCliVersionCheckedAtUtc: cli?.CodexCliVersionCheckedAtUtc,
            CodexCliVersionError: cli?.CodexCliVersionError,
            CodexCliLauncherFingerprint: cli?.CodexCliLauncherFingerprint,
            PtyBackendDeprecated: decision.Deprecated);
    }

    public string? VerificationCustodyBackend =>
        _runtime.VerificationCustodyBackend == VerificationCustodyBackends.LinuxCgroup
            ? VerificationCustodyBackends.LinuxCgroup : null;

    public Guid RunnerStoreId => _runtime.RunnerStoreId;
    public Task<RunnerAbsenceOutcome<RunnerAbsencePrepared>> PrepareAbsenceEvidenceAsync(RunnerAbsenceRequest request, CancellationToken ct) =>
        _runtime.PrepareAbsenceEvidenceAsync(request, ct);
    public Task<RunnerAbsenceOutcome<RunnerAbsenceCertificate>> CertifyAbsenceAsync(RunnerAbsenceRequest request, CancellationToken ct) =>
        _runtime.CertifyAbsenceAsync(request, ct);

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

    public Task<TerminalSeatObservation> ObserveTerminalSeatAsync(
        Guid sessionId, TerminalSeatObservationRequest request, CancellationToken ct) =>
        _runtime.ObserveTerminalSeatAsync(sessionId, request, ct);

    public Task<TerminalSeatReleaseResult> ReleaseTerminalSeatAsync(
        Guid sessionId, TerminalSeatReleaseRequest request, CancellationToken ct) =>
        _runtime.ReleaseTerminalSeatAsync(sessionId, request, TimeSpan.FromSeconds(5), ct);

    public Task<WorkspaceParkResult> ParkWorkspaceAsync(WorkspaceParkCommand request, CancellationToken ct) =>
        _runtime.ParkWorkspaceAsync(request, ct);

    public Task<WorkspaceRepositoryIdentityResult> ReadWorkspaceRepositoryIdentityAsync(WorkspaceRepositoryIdentityRequest request, CancellationToken ct) =>
        _runtime.ReadWorkspaceRepositoryIdentityAsync(request, ct);
}
