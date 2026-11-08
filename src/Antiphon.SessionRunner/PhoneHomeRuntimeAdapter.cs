using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

public sealed class PhoneHomeRuntimeAdapter : IPhoneHomeRuntimeSurface
{
    internal static IReadOnlyList<string> StaticFeatures { get; } =
    [
        RunnerCapabilityFeatures.DecisionPathInspectionV1,
        GrokRulesTransport.Capability,
        RunnerCapabilityFeatures.SessionGenerationV1,
        PhoneHomeEventWindowOptions.Capability,
        RunnerCapabilityFeatures.ConditionalMaintenanceInputV1,
        ProviderDialogObservation.Capability,
        RunnerCapabilityFeatures.CompactionContinuationStopV1,
        RunnerCapabilityFeatures.TerminalSeatReleaseV1,
        RunnerCapabilityFeatures.TerminalSeatDeliveryEvidenceV1,
        RunnerCapabilityFeatures.WorkspaceRepositoryV1,
        RunnerCapabilityFeatures.WorkspacePublishV1,
        RunnerCapabilityFeatures.WorkspaceCardBranchV1,
        CodexCliVersionProbe.Capability,
        RunnerCapabilityFeatures.PtyConditionalReadV1,
    ];
    private readonly SessionRunnerRuntime _runtime;
    private readonly RunnerBuildDto _build;
    private readonly RunnerWorkspaceRetention? _retention;
    private readonly RunnerTempSweep? _tempSweep;

    public PhoneHomeRuntimeAdapter(SessionRunnerRuntime runtime, RunnerBuildDto build,
        RunnerWorkspaceRetention? retention = null, RunnerTempSweep? tempSweep = null)
    {
        _runtime = runtime;
        _build = build;
        _retention = retention;
        _tempSweep = tempSweep;
    }

    /// <summary>CARD-1677 S2: the temp sweep, like retention, is advertised only by a Linux runner.</summary>
    internal static IReadOnlyList<string> TempSweepFeatures(bool isLinux) =>
        isLinux ? [RunnerCapabilityFeatures.TempSweepV1] : [];

    /// <summary>
    /// CARD-1677 S2 (review 12ba9a25 D-2): the OS the temp sweep branches on, the real OS unless a test forces it, so
    /// the off-Linux branch (not advertised, refused) is witnessed on the real adapter.
    /// </summary>
    internal bool TempSweepIsLinux { get; init; } = OperatingSystem.IsLinux();

    public Task<PhoneHomeTempSweepResponse> TempSweepAsync(PhoneHomeTempSweepRequest request, CancellationToken ct) =>
        _tempSweep is not null && TempSweepIsLinux
            ? Task.Run(() => _tempSweep.SweepAsync(request, ct), ct)
            : throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
                "Temp sweep is not supported on this runner.", 501);

    /// <summary>CARD-1677 D-11: retention is advertised only by a Linux runner (the desktop never retains).</summary>
    internal static IReadOnlyList<string> RetentionFeatures(bool isLinux) =>
        isLinux ? [RunnerCapabilityFeatures.WorkspaceRetentionV1] : [];

    public Task<WorkspaceRetainStep> RetainWorkspaceCheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct) =>
        Retention().CheckAndMoveAsync(request, ct);

    public Task<PhoneHomeWorkspaceRetainResponse> RetainWorkspaceCompleteAsync(WorkspaceRetainStep step, CancellationToken ct) =>
        Retention().CompleteAsync(step, ct);

    public Task<PhoneHomeWorkspaceRetentionInventoryResponse> WorkspaceRetentionInventoryAsync(CancellationToken ct) =>
        Retention().InventoryAsync(ct);

    private RunnerWorkspaceRetention Retention() =>
        _retention is not null && OperatingSystem.IsLinux()
            ? _retention
            : throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
                "Workspace retention is not supported on this runner.", 501);

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
        if (_runtime.SupportsCompletedCardWorkspace)
            features = [.. features, RunnerCapabilityFeatures.CompletedCardWorkspaceV1];
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
        // CARD-0824 S2: every runner can delete its own refs for a landed delegate branch (portable git).
        features = [.. features, RunnerCapabilityFeatures.WorkspaceBranchRefsV1];
        if (_retention is not null)
            features = [.. features, .. RetentionFeatures(OperatingSystem.IsLinux())];
        if (_tempSweep is not null)
            features = [.. features, .. TempSweepFeatures(TempSweepIsLinux)];
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
    public Task<DecisionPathInspectionResult?> InspectDecisionPathsAsync(DecisionPathInspectionRequest request, CancellationToken ct) =>
        _runtime.InspectDecisionPathsAsync(request, ct);
    public RunnerWorkspaceUseGate? WorkspaceUseGate => _runtime.WorkspaceUseGate;
    public Task<CompletedCardWorkspaceInspectionResponse> InspectCompletedCardWorkspaceAsync(
        CompletedCardWorkspaceInspectRequest request, CancellationToken ct) =>
        _runtime.InspectCompletedCardWorkspaceAsync(request, ct);
    public Task<CompletedCardWorkspaceRemovalResponse> RemoveCompletedCardWorkspaceAsync(
        CompletedCardWorkspaceCommand command, CancellationToken ct) =>
        _runtime.RemoveCompletedCardWorkspaceAsync(command, ct);
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
    public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => _runtime.GetSnapshotAsync(sessionId, ct);
    public PtyRead<RunnerBufferDto> ReadBuffer(Guid sessionId, string? ifNoneMatch) =>
        _runtime.ReadBuffer(sessionId, ifNoneMatch);
    public Task<PtyRead<RunnerSnapshotDto>> ReadSnapshotAsync(Guid sessionId, string? ifNoneMatch, CancellationToken ct) =>
        _runtime.ReadSnapshotAsync(sessionId, ifNoneMatch, ct);
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
