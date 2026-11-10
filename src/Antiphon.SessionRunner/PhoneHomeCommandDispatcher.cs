using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public interface IPhoneHomeRuntimeSurface
{
    Task<DecisionPathInspectionResult?> InspectDecisionPathsAsync(DecisionPathInspectionRequest request, CancellationToken ct) =>
        Task.FromResult<DecisionPathInspectionResult?>(null);
    RunnerWorkspaceUseGate? WorkspaceUseGate => null;
    Task<CompletedCardWorkspaceInspectionResponse> InspectCompletedCardWorkspaceAsync(
        CompletedCardWorkspaceInspectRequest request, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation, "unsupported_operation", 501);
    Task<CompletedCardWorkspaceRemovalResponse> RemoveCompletedCardWorkspaceAsync(
        CompletedCardWorkspaceCommand command, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation, "unsupported_operation", 501);
    Task<WorkspaceRepositoryIdentityResult> ReadWorkspaceRepositoryIdentityAsync(WorkspaceRepositoryIdentityRequest request, CancellationToken ct) =>
        Task.FromResult(new WorkspaceRepositoryIdentityResult(WorkspaceRepositoryIdentityOutcome.Held, "identity_unsupported"));
    Task<WorkspaceParkResult> ParkWorkspaceAsync(WorkspaceParkCommand request, CancellationToken ct) =>
        Task.FromResult(new WorkspaceParkResult(WorkspaceParkOutcome.Held, "park_unsupported"));
    RunnerCapabilitiesDto Capabilities();

    /// <summary>
    /// CARD-1677 S-4. Retention phase A (every check, then the move into the retiring root). The dispatcher
    /// runs it inside its mutation lock. Default refuses, so a surface that has not opted in never retains.
    /// </summary>
    Task<WorkspaceRetainStep> RetainWorkspaceCheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation, "unsupported_operation", 501);

    /// <summary>CARD-1677 S-4. Retention phase B (the unforced remove). The dispatcher runs it after the lock is released, only for a moved tree.</summary>
    Task<PhoneHomeWorkspaceRetainResponse> RetainWorkspaceCompleteAsync(WorkspaceRetainStep step, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation, "unsupported_operation", 501);

    /// <summary>CARD-1677 S1. Read-only inventory of the worktree and retiring roots; runs outside the lock.</summary>
    Task<PhoneHomeWorkspaceRetentionInventoryResponse> WorkspaceRetentionInventoryAsync(CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation, "unsupported_operation", 501);

    /// <summary>CARD-1677 S2. One bounded temp sweep of the registered families; runs outside the lock. Default refuses.</summary>
    Task<PhoneHomeTempSweepResponse> TempSweepAsync(PhoneHomeTempSweepRequest request, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation, "unsupported_operation", 501);
    /// <summary>CARD-1153: default unsupported, so an adapter without the evidence service never certifies.</summary>
    Task<RunnerAbsenceOutcome<RunnerAbsencePrepared>> PrepareAbsenceEvidenceAsync(RunnerAbsenceRequest request, CancellationToken ct) =>
        Task.FromResult(RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(PhoneHomeProblemTypes.UnsupportedOperation, 501, "absence evidence unsupported"));
    Task<RunnerAbsenceOutcome<RunnerAbsenceCertificate>> CertifyAbsenceAsync(RunnerAbsenceRequest request, CancellationToken ct) =>
        Task.FromResult(RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(PhoneHomeProblemTypes.UnsupportedOperation, 501, "absence evidence unsupported"));
    Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request, CancellationToken ct) =>
        Task.FromResult<RunnerCodexCliVersionDto?>(null);
    string Health();
    IReadOnlyList<RunnerSessionDto> List();
    Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct);
    Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct);
    RunnerBufferDto GetBuffer(Guid sessionId);
    Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct);

    // CARD-1763. Fakes keep returning the full body. The production adapter overrides these.
    PtyRead<RunnerBufferDto> ReadBuffer(Guid sessionId, string? ifNoneMatch) =>
        new(false, null, GetBuffer(sessionId));

    async Task<PtyRead<RunnerSnapshotDto>> ReadSnapshotAsync(Guid sessionId, string? ifNoneMatch, CancellationToken ct) =>
        new(false, null, await GetSnapshotAsync(sessionId, ct));
    RunnerTranscriptDto GetTranscript(Guid sessionId);

    // CARD-1764. Fakes keep returning the full transcript. The production adapter overrides this.
    RunnerTranscriptDto GetTranscript(Guid sessionId, long since, string? stream) => GetTranscript(sessionId);
    Task SendInputAsync(Guid sessionId, string input, CancellationToken ct);
    Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct);
    Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct);
    Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct);
    Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct);
    int OwnedSessionCount { get; }

    Task<int> KillAllAsync(TimeSpan timeout, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
            "Retire is not supported on this runner.", 409);

    /// <summary>CARD-0653. Default refuses so an old fake cannot pretend it freed a seat.</summary>
    Task<RunnerSessionDto> ReleaseSlotAsync(Guid sessionId, string reason, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
            "Slot release is not supported on this runner.", 409);

    Task<CompactionContinuationStopResult> StopCompactionContinuationAsync(
        Guid sessionId, CompactionContinuationStopRequest request, CancellationToken ct) =>
        Task.FromResult(new CompactionContinuationStopResult(
            sessionId, request.AttemptId, false, CompactionStopOutcomes.Unsupported, null));

    Task<CompactionTailObservation> ObserveCompactionAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(CompactionTailObservation.Unsupported());

    // Old surfaces refuse without calling either destructive compatibility operation.
    Task<TerminalSeatObservation> ObserveTerminalSeatAsync(
        Guid sessionId, TerminalSeatObservationRequest request, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
            "Terminal seat observation is not supported on this runner.", 409);

    Task<TerminalSeatReleaseResult> ReleaseTerminalSeatAsync(
        Guid sessionId, TerminalSeatReleaseRequest request, CancellationToken ct) =>
        Task.FromResult(new TerminalSeatReleaseResult(
            sessionId, request.ActionId, TerminalSeatReleaseOutcome.Unsupported, null));

    // CARD-0604 D-19 (Cut B). The custody surface the phone-home lane needs: which mechanism
    // this runner actually advertises, which custody store is its own, and a read/seal of a
    // tracked execution. Defaults keep every existing fake compiling AND refusing: a fake that
    // has not opted in advertises nothing, so no binding can be admitted through it.
    string? VerificationCustodyBackend => null;
    Guid RunnerStoreId => Guid.Empty;
    Task<VerificationCustodyStatus> ReadCustodyAsync(
        VerificationExecutionBinding binding, bool seal, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
            "This runner cannot read verification custody.", 409);
}

public sealed class PhoneHomeCommandDispatcher
{
    private readonly IPhoneHomeRuntimeSurface _runtime;
    private readonly PhoneHomeSettings _settings;
    private readonly RunnerCapacityState _capacity;
    private readonly IProviderAuthProbe? _authProbe;
    private readonly IHostStatsSource? _hostStats;
    private readonly IHostLoadHistorySource? _hostLoad;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly TimeProvider _time;
    private readonly Guid _processBootId;
    private int _retireAccepted;
    private int _stopScheduled;
    private readonly object _mutationGate = new();
    private readonly ILogger _logger;
    private readonly PhoneHomeLaunchGenerationStore _launchGenerations;
    private RunnerWorkspaceService? _workspace;
    private readonly RunnerWorkspaceActivity _workspaceActivity;
    private readonly RunnerRepositoryActivity? _repositoryActivity;

    public PhoneHomeCommandDispatcher(
        IPhoneHomeRuntimeSurface runtime, PhoneHomeSettings settings, IProviderAuthProbe? authProbe = null,
        ILogger<PhoneHomeCommandDispatcher>? logger = null, IHostStatsSource? hostStats = null,
        IHostApplicationLifetime? lifetime = null, TimeProvider? time = null,
        PhoneHomeProcessIdentity? identity = null, RunnerCapacityState? capacity = null,
        IHostLoadHistorySource? hostLoad = null, RunnerRepositoryActivity? activity = null)
    {
        _runtime = runtime;
        _settings = settings;
        _capacity = capacity ?? new RunnerCapacityState(settings);
        _authProbe = authProbe;
        _hostStats = hostStats;
        _hostLoad = hostLoad;
        _lifetime = lifetime;
        _time = time ?? TimeProvider.System;
        _processBootId = identity?.BootId ?? Guid.NewGuid();
        _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _launchGenerations = new PhoneHomeLaunchGenerationStore(settings.LaunchGenerationsPath);
        _workspaceActivity = new RunnerWorkspaceActivity(_time);
        _repositoryActivity = activity;
    }

    /// <summary>
    /// CARD-0604: the same DTO the Capabilities operation answers with, exposed so registration
    /// can carry it. One source, so a registration can never disagree with a later probe.
    /// </summary>
    public RunnerCapabilitiesDto Capabilities()
    {
        var capabilities = WithSlotFeature(_runtime.Capabilities());
        var features = (capabilities.Features ?? []).Where(f => f != RunnerCapabilityFeatures.HostLoadHistoryV1).ToList();
        if (_hostStats is not null && !features.Contains(RunnerCapabilityFeatures.HostStatsV1)) features.Add(RunnerCapabilityFeatures.HostStatsV1);
        if (_hostLoad is not null) features.Add(RunnerCapabilityFeatures.HostLoadHistoryV1);
        return capabilities with { Features = features };
    }

    /// <summary>CARD-0824 S1: <c>workspaceSlotV1</c> is advertised only while this runner's slot flag is on.</summary>
    private RunnerCapabilitiesDto WithSlotFeature(RunnerCapabilitiesDto capabilities) =>
        !_settings.WorkspaceSlots.Enabled || capabilities.Features?.Contains(RunnerCapabilityFeatures.WorkspaceSlotV1) == true
            ? capabilities
            : capabilities with { Features = [.. capabilities.Features ?? [], RunnerCapabilityFeatures.WorkspaceSlotV1] };

    /// <summary>The socket writer calls this only after an accepted Retire reply was sent.</summary>
    public void NotifyRetireReplyWritten()
    {
        if (Volatile.Read(ref _retireAccepted) == 0 || Interlocked.Exchange(ref _stopScheduled, 1) != 0)
            return;
        _ = StopAfterReplyAsync();
    }

    private async Task StopAfterReplyAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250), _time, CancellationToken.None);
        _lifetime?.StopApplication();
    }

    private RunnerWorkspaceService Workspace()
    {
        _workspace ??= new RunnerWorkspaceService(_settings.RepositoryPolicy(), _settings.AllowedCwd);
        _workspace.WorkspaceUseGate = _runtime.WorkspaceUseGate;
        _workspace.Activity = _repositoryActivity;
        // CARD-0824 S1: the pool exists only when the runner flag is on; off keeps task-<id> mirrors.
        if (_settings.WorkspaceSlots.Enabled && _workspace.SlotPool is null)
            _workspace.SlotPool = new RunnerWorkspaceSlotPool(
                _workspace, _settings.WorkspaceSlots, _capacity.Capacity, _time, activity: _repositoryActivity);
        return _workspace;
    }

    /// <summary>CARD-0824 S1 test seam: a scratch workspace service (and its pool) for wire tests.</summary>
    internal void UseWorkspace(RunnerWorkspaceService workspace) => _workspace = workspace;

    /// <summary>CARD-1818. The workspace <see cref="Workspace"/> builds, including its repository activity.</summary>
    internal RunnerWorkspaceService WorkspaceForTest() => Workspace();

    private RunnerWorkspaceSlotPool SlotPool() =>
        Workspace().SlotPool ?? throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.UnsupportedTarget, WorkspaceSlotCodes.Disabled, 409);

    private DateTimeOffset? _slotsMaintainedAt;

    /// <summary>
    /// CARD-0824 S1 pool upkeep, run under the mutation lock before a slot operation: crash recovery
    /// once per process, then maintenance (schedule marks, deferred releases, idle retirement) at
    /// most every 15 minutes.
    /// </summary>
    private async Task<RunnerWorkspaceSlotPool> MaintainedSlotPoolAsync(CancellationToken ct)
    {
        var pool = SlotPool();
        var now = _time.GetUtcNow();
        if (_slotsMaintainedAt is null)
            await pool.StartAsync(ct);
        else if (now - _slotsMaintainedAt >= TimeSpan.FromMinutes(15))
            await pool.MaintainAsync(ct);
        else
            return pool;
        _slotsMaintainedAt = now;
        return pool;
    }

    // CARD-1032: an unbound mirror still uses this historical fallback, including the
    // custody ledger guid when StoreIdPath cannot be parsed. A bound mirror never does.
    private Guid RecipientStoreId()
    {
        var path = _settings.StoreIdPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                if (Guid.TryParse(File.ReadAllText(path).Trim(), out var stored) && stored != Guid.Empty)
                    return stored;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return _runtime.RunnerStoreId;
    }

    // A bound mirror may use only a guid read from StoreIdPath. Missing, empty,
    // whitespace, garbled, wrong-length, directory, and unreadable paths
    // are recipient_store_unreadable. The custody ledger id is not a substitute.
    private bool TryReadRegistrationStore(out Guid store)
    {
        store = Guid.Empty;
        var path = _settings.StoreIdPath;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            if (Directory.Exists(path) || !File.Exists(path))
                return false;
            return Guid.TryParse(File.ReadAllText(path).Trim(), out store) && store != Guid.Empty;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void RequireCompletedCardCapability()
    {
        if (_runtime.Capabilities().Features?.Contains(RunnerCapabilityFeatures.CompletedCardWorkspaceV1,
            StringComparer.Ordinal) != true)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation, "unsupported_operation", 501);
    }

    private static void RequireCompletedCardBody(CompletedCardWorkspaceValidation validation)
    {
        if (!validation.IsValid)
            throw new PhoneHomeAdmissionException("invalid_completed_card_workspace", validation.Reason!, 400);
    }

    private void RequireCompletedCardRecipient(CompletedCardWorkspaceBinding binding)
    {
        if (binding.RunnerId != _settings.RunnerId)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "runner_store_mismatch", 409);
        Guid store;
        try { store = RunnerWorkspaceNoFollowDelete.ReadStoreGuid(_settings.StoreIdPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "recipient_store_unreadable", 409);
        }
        if (!Guid.TryParse(binding.RunnerStoreId, out var requested) || requested == Guid.Empty || store != requested)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "runner_store_mismatch", 409);
    }

    private async Task WriteSpillIfPresentAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        if (request.Payload is not { } payload
            || payload.ValueKind != System.Text.Json.JsonValueKind.Object
            || !payload.TryGetProperty("spill", out var raw)
            || raw.ValueKind != System.Text.Json.JsonValueKind.Object)
            return;
        var spill = raw.Deserialize<PhoneHomeInputSpill>(PhoneHomeFraming.Json);
        if (spill is null || string.IsNullOrWhiteSpace(spill.RelativePath))
            return;
        try
        {
            if (!payload.TryGetProperty("runnerCwd", out var cwd)
                || cwd.ValueKind != System.Text.Json.JsonValueKind.String)
                throw new PhoneHomeAdmissionException(
                    PhoneHomeProblemTypes.UnsupportedTarget, "A spill requires the session's runner cwd.", 409);
            await Workspace().WriteSpillAsync(cwd.GetString()!, spill, ct);
        }
        catch (Exception ex) when (ex is PhoneHomeAdmissionException or IOException
            or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // This code is emitted only before SendInputAsync starts. Do not include a host path.
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.SpillWriteFailedBeforeInput,
                "The runner could not write this input spill before sending input.", 409);
        }
    }

    public async Task<PhoneHomeFrame> DispatchAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        var trace = new DispatchTrace();
        var reply = await DispatchCoreAsync(request, trace, ct);
        LogSessionMutation(request, reply, trace);
        return reply;
    }

    /// <summary>
    /// CARD-0679 D-4: one Information line per Launch, KillGeneration and ReleaseSlot, so the
    /// runner's default log answers whether a launch or kill arrived and what became of it. Ids,
    /// the executable's base name and generations only: never argv, env, cwd or prompt text.
    /// </summary>
    private void LogSessionMutation(PhoneHomeFrame request, PhoneHomeFrame reply, DispatchTrace trace)
    {
        if (request.Operation is not (PhoneHomeOperation.Launch or PhoneHomeOperation.KillGeneration or PhoneHomeOperation.ReleaseSlot))
            return;
        var payload = request.Payload is { ValueKind: JsonValueKind.Object } body ? body : (JsonElement?)null;
        var sessionId = ReadString(payload, "sessionId") ?? "unknown";
        var outcome = reply.Kind == PhoneHomeFrameKind.Error
            ? $"refused:{reply.ErrorCode}"
            : trace.Outcome ?? ReadString(reply.Payload, "outcome") ?? ReadString(reply.Payload, "status") ?? "ok";
        switch (request.Operation)
        {
            case PhoneHomeOperation.Launch:
                var exe = ReadString(payload, "exe");
                _logger.LogInformation(
                    "Phone-home Launch {RequestId} session={SessionId} exe={Exe} acceptedStartedAt={AcceptedStartedAt} outcome={Outcome}",
                    request.RequestId, sessionId, exe is null ? "unknown" : Path.GetFileName(exe),
                    ReadString(payload, "acceptedStartedAt") ?? "none", outcome);
                break;
            case PhoneHomeOperation.KillGeneration:
                _logger.LogInformation(
                    "Phone-home KillGeneration {RequestId} session={SessionId} expectedGeneration={ExpectedGeneration} outcome={Outcome}",
                    request.RequestId, sessionId, ReadString(payload, "expectedAcceptedStartedAt") ?? "none", outcome);
                break;
            default:
                _logger.LogInformation(
                    "Phone-home ReleaseSlot {RequestId} session={SessionId} outcome={Outcome}",
                    request.RequestId, sessionId, outcome);
                break;
        }
    }

    private static string? ReadString(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } obj
        && obj.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed class DispatchTrace
    {
        public string? Outcome { get; set; }
    }

    private async Task<PhoneHomeFrame> DispatchCoreAsync(PhoneHomeFrame request, DispatchTrace trace, CancellationToken ct)
    {
        if (request.Kind != PhoneHomeFrameKind.Request || request.Operation is null)
            return Error(request, PhoneHomeProblemTypes.UnsupportedOperation, "Frame is not a request.", 400);

        try
        {
            return request.Operation.Value switch
            {
                PhoneHomeOperation.Capabilities => Result(request, Capabilities()),
                PhoneHomeOperation.DecisionPathInspect => Result(request, await _runtime.InspectDecisionPathsAsync(
                    request.Payload?.Deserialize<DecisionPathInspectionRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Decision paths are required."), ct)),
                PhoneHomeOperation.CompletedCardWorkspaceInspect => await MutateAsync(request, async () =>
                {
                    RequireCompletedCardCapability();
                    var raw = request.Payload?.GetRawText() ?? "";
                    RequireCompletedCardBody(CompletedCardWorkspaceContract.ValidateInspect(raw));
                    var body = request.Payload!.Value.Deserialize<CompletedCardWorkspaceInspectRequest>(PhoneHomeFraming.Json)!;
                    RequireCompletedCardRecipient(body.Binding);
                    return Result(request, await _runtime.InspectCompletedCardWorkspaceAsync(body, ct));
                }),
                PhoneHomeOperation.CompletedCardWorkspaceRemove => await MutateAsync(request, async () =>
                {
                    RequireCompletedCardCapability();
                    var raw = request.Payload?.GetRawText() ?? "";
                    RequireCompletedCardBody(CompletedCardWorkspaceContract.Validate(raw));
                    var body = request.Payload!.Value.Deserialize<CompletedCardWorkspaceCommand>(PhoneHomeFraming.Json)!;
                    RequireCompletedCardRecipient(body.Binding);
                    return Result(request, await _runtime.RemoveCompletedCardWorkspaceAsync(body, ct));
                }),
                PhoneHomeOperation.CodexCliVersion => Result(request, await _runtime.GetCodexCliVersionAsync(
                    request.Payload?.Deserialize<RunnerCodexCliProbeRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Version descriptor is required."), ct)),
                PhoneHomeOperation.Health => Result(request, new { status = _runtime.Health() }),
                PhoneHomeOperation.List => Result(request, _runtime.List()),
                PhoneHomeOperation.Get => Result(request, await _runtime.GetAsync(ReadSessionId(request), ct)),
                PhoneHomeOperation.Launch => await LaunchAsync(request, trace, ct),
                PhoneHomeOperation.LaunchPlatformConstrained => await LaunchPlatformConstrainedAsync(request, trace, ct),
                PhoneHomeOperation.Retire => await RetireAsync(request, ct),
                PhoneHomeOperation.ProviderAuth => Result(request, await ProviderAuthAsync(request, ct)),
                PhoneHomeOperation.Buffer => ConditionalFrame(request, _runtime.ReadBuffer(ReadSessionId(request), ReadIfNoneMatch(request)), RunnerReadJson.PhoneBuffer),
                PhoneHomeOperation.Snapshot => ConditionalFrame(request, await _runtime.ReadSnapshotAsync(ReadSessionId(request), ReadIfNoneMatch(request), ct), RunnerReadJson.PhoneSnapshot),
                PhoneHomeOperation.Transcript => Result(request, _runtime.GetTranscript(ReadSessionId(request), ReadSince(request), ReadStream(request)), RunnerReadJson.PhoneTranscript),
                PhoneHomeOperation.Input => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerInputRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Input body is required.");
                    // CARD-0604 G-21: a remote session's spilled body travels with the input and
                    // is written HERE, inside the session's own cwd. The desktop never writes it:
                    // its Cwd is a Windows path this process cannot see.
                    await WriteSpillIfPresentAsync(request, ct);
                    await _runtime.SendInputAsync(ReadSessionId(request), body.Input, ct);
                    return Result(request, new { ok = true });
                }),
                PhoneHomeOperation.WorkspaceMirror => await MutateAsync(request, async () =>
                {
                    var raw = request.Payload?.GetRawText();
                    if (string.IsNullOrWhiteSpace(raw))
                        throw new ArgumentException("Workspace mirror body is required.");
                    var validation = PhoneHomeWorkspaceMirrorContract.ValidateRequest(raw);
                    if (!validation.IsValid)
                        throw new PhoneHomeAdmissionException(
                            PhoneHomeProblemTypes.UnsupportedTarget,
                            "Mirror request was refused: " + validation.Reason, 409);
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace mirror body is required.");
                    var workspace = Workspace();
                    if (body.Binding is { } requested)
                    {
                        // A foreign runner id stays runner_store_mismatch when the registration
                        // file is absent. C1032_StoreMismatchRefused dispatches that shape.
                        if (!string.Equals(requested.RunnerId, _settings.RunnerId, StringComparison.Ordinal))
                            throw new PhoneHomeAdmissionException(
                                PhoneHomeProblemTypes.UnsupportedTarget, "runner_store_mismatch", 409);
                        if (!TryReadRegistrationStore(out var recipientStore))
                            throw new PhoneHomeAdmissionException(
                                PhoneHomeProblemTypes.UnsupportedTarget, "recipient_store_unreadable", 409);
                        workspace.BindRecipient(_settings.RunnerId, recipientStore);
                        if (!Guid.TryParse(requested.RunnerStoreId, out var requestedStore)
                            || requestedStore == Guid.Empty
                            || requestedStore != recipientStore)
                            throw new PhoneHomeAdmissionException(
                                PhoneHomeProblemTypes.UnsupportedTarget, "runner_store_mismatch", 409);
                    }
                    else
                    {
                        // The authenticated server request decides binding presence. Omission
                        // deliberately selects legacy path-only creation, without registration
                        // admission, a binding receipt, a preparation ledger or an owner claim.
                        // No later slice in the CARD-1032 plan removes this compatibility path.
                        workspace.BindRecipient(_settings.RunnerId, RecipientStoreId());
                    }
                    if (body.Slot == true && workspace.SlotPool is not null)
                        await MaintainedSlotPoolAsync(ct);
                    // CARD-1677 D-2: stamped before the mirror runs, so a retain that follows it keeps the tree.
                    if (body.Slot != true && !string.IsNullOrWhiteSpace(body.Name))
                        _workspaceActivity.RecordMirror(workspace.WorktreeRoot + "/" + body.Name);
                    return Result(request, await workspace.MirrorAsync(body, ct));
                }),
                PhoneHomeOperation.WorkspaceSlotRelease => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceSlotReleaseRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace slot release body is required.");
                    return Result(request, await (await MaintainedSlotPoolAsync(ct)).ReleaseAsync(body, ct));
                }),
                PhoneHomeOperation.WorkspaceSlotColdReset => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceSlotColdResetRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace slot cold-reset body is required.");
                    return Result(request, await (await MaintainedSlotPoolAsync(ct)).ColdResetAsync(body, ct));
                }),
                PhoneHomeOperation.WorkspaceSlotQuarantineRelease => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceSlotQuarantineReleaseRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace slot quarantine release body is required.");
                    return Result(request, await (await MaintainedSlotPoolAsync(ct)).ReleaseQuarantineAsync(body, ct));
                }),
                PhoneHomeOperation.WorkspaceRemove => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceRemoveRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace remove body is required.");
                    return Result(request, await Workspace().RemoveAsync(body, ct));
                }),
                // CARD-0824 S2: under the same lock as Mirror, so a mirror being created cannot race the ref delete.
                PhoneHomeOperation.WorkspaceBranchRefsDelete => await MutateAsync(request, async () =>
                {
                    if (_runtime.Capabilities().Features?.Contains(RunnerCapabilityFeatures.WorkspaceBranchRefsV1, StringComparer.Ordinal) != true)
                        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
                            "Workspace branch ref deletion is not supported on this runner.", 501);
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceBranchRefsDeleteRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace branch refs delete body is required.");
                    return Result(request, await Workspace().DeleteBranchRefsAsync(body, ct));
                }),
                PhoneHomeOperation.WorkspacePublish => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspacePublishRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace publish body is required.");
                    return Result(request, await Workspace().PublishAsync(body, ct));
                }),
                // CARD-0604 D-19 (Cut B). Custody and the verification snapshot for a Mutation
                // bound to this runner. Each is a typed body; nothing here takes a shell string.
                PhoneHomeOperation.ReadCustody => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeReadCustodyRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Read-custody body is required.");
                    return Result(request, await _runtime.ReadCustodyAsync(body.Binding, body.Seal, ct));
                }),
                PhoneHomeOperation.VerificationWorkspaceCreate => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeVerificationCreateRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification create body is required.");
                    return Result(request, await Workspace().CreateVerificationAsync(body, ct));
                }),
                PhoneHomeOperation.VerificationWorkspaceValidate => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeVerificationValidateRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification validate body is required.");
                    return Result(request, await Workspace().ValidateVerificationAsync(body, ct));
                }),
                PhoneHomeOperation.VerificationWorkspaceInspect => Result(request,
                    await Workspace().InspectVerificationAsync(
                        request.Payload?.Deserialize<PhoneHomeVerificationInspectRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification inspect body is required."), ct)),
                PhoneHomeOperation.VerificationWorkspaceReadRestoration => Result(request,
                    await Workspace().ReadVerificationRestorationAsync(
                        request.Payload?.Deserialize<PhoneHomeVerificationReadRestorationRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification restoration body is required."), ct)),
                PhoneHomeOperation.VerificationWorkspaceRemove => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeVerificationRemoveRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification remove body is required.");
                    return Result(request, await Workspace().RemoveVerificationAsync(body, ct));
                }),
                PhoneHomeOperation.ConditionalInput => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerConditionalInputRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Conditional input body is required.");
                    await WriteSpillIfPresentAsync(request, ct);
                    return Result(request, await _runtime.SendConditionalInputAsync(ReadSessionId(request), body, ct));
                }),
                PhoneHomeOperation.ClearBuffer => await MutateAsync(request, async () =>
                {
                    await _runtime.ClearLiveBufferAsync(ReadSessionId(request), ct);
                    return Result(request, new { ok = true });
                }),
                PhoneHomeOperation.Resize => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerResizeRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Resize body is required.");
                    await _runtime.ResizeAsync(ReadSessionId(request), body.Cols, body.Rows, ct);
                    return Result(request, new { ok = true });
                }),
                PhoneHomeOperation.KillGeneration => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerKillGenerationRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Kill-generation body is required.");
                    return Result(request, await _runtime.KillGenerationAsync(ReadSessionId(request), body.ExpectedAcceptedStartedAt, ct));
                }),
                PhoneHomeOperation.ObserveTerminalSeat => Result(request, await ObserveTerminalSeatAsync(request, ct)),
                PhoneHomeOperation.WorkspaceRepositoryIdentity => await ReadWorkspaceRepositoryIdentityAsync(request, ct),
                PhoneHomeOperation.WorkspacePark => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<WorkspaceParkCommand>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace park body is required.");
                    if (body.Version != 1 || !WorkspaceParkCommand.Supported(_runtime.Capabilities()))
                        return Result(request, new WorkspaceParkResult(WorkspaceParkOutcome.Held, "park_unsupported"));
                    return Result(request, await _runtime.ParkWorkspaceAsync(body, ct));
                }),
                PhoneHomeOperation.ReleaseTerminalSeat => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeTerminalSeatReleaseRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Terminal seat release body is required.");
                    ArgumentNullException.ThrowIfNull(body.Release);
                    ArgumentNullException.ThrowIfNull(body.Release.Observation);
                    if (!body.Release.Supported(_runtime.Capabilities()))
                        return Result(request, new TerminalSeatReleaseResult(body.SessionId, body.Release.ActionId,
                            TerminalSeatReleaseOutcome.Unsupported, null));
                    return Result(request, await _runtime.ReleaseTerminalSeatAsync(body.SessionId, body.Release, ct));
                }),
                PhoneHomeOperation.ReleaseSlot => await MutateAsync(request, async () =>
                {
                    var reason = request.Payload is { } payload
                        && payload.TryGetProperty("reason", out var raw)
                        && raw.ValueKind == JsonValueKind.String
                        ? raw.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(reason))
                        throw new PhoneHomeAdmissionException(
                            PhoneHomeProblemTypes.UnsupportedTarget, "A release reason is required.", 400);
                    return Result(request, await _runtime.ReleaseSlotAsync(ReadSessionId(request), reason, ct));
                }),
                PhoneHomeOperation.StopCompactionContinuation => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<CompactionContinuationStopRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Compaction stop body is required.");
                    return Result(request, await _runtime.StopCompactionContinuationAsync(ReadSessionId(request), body, ct));
                }),
                PhoneHomeOperation.ObserveCompaction => Result(
                    request, await _runtime.ObserveCompactionAsync(ReadSessionId(request), ct)),
                PhoneHomeOperation.HostStats => HostStatsResult(request),
                PhoneHomeOperation.HostLoadHistory => HostLoadHistoryResult(request),
                PhoneHomeOperation.HostStatsSeries => HostStatsSeriesResult(request),
                // CARD-1153 D-3: the authenticated, runner/store-bound connection is the authentication.
                PhoneHomeOperation.PrepareAbsenceEvidence => await MutateAsync(request, async () =>
                    AbsenceFrame(request, await _runtime.PrepareAbsenceEvidenceAsync(ReadAbsenceRequest(request), ct))),
                PhoneHomeOperation.CertifyAbsence => await MutateAsync(request, async () =>
                    AbsenceFrame(request, await _runtime.CertifyAbsenceAsync(ReadAbsenceRequest(request), ct))),
                PhoneHomeOperation.WorkspaceRetentionInventory => await RetentionInventoryAsync(request, ct),
                PhoneHomeOperation.WorkspaceRetain => await RetainAsync(request, ct),
                PhoneHomeOperation.TempSweep => await TempSweepAsync(request, ct),
                PhoneHomeOperation.SetCapacity => await MutateAsync(request, () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeSetCapacityRequest>(PhoneHomeFraming.Json)
                        ?? throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.Capacity,
                            "Capacity body is required.", 400);
                    if (body.Capacity < 1 || string.IsNullOrWhiteSpace(body.Reason))
                        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.Capacity,
                            "Capacity must be at least one and a reason is required.", 409);
                    _capacity.Apply(body.Capacity);
                    return Task.FromResult(Result(request, new PhoneHomeSetCapacityResponse(
                        _capacity.Capacity, true, _capacity.StatePath)));
                }),
                _ => Error(request, PhoneHomeProblemTypes.UnsupportedOperation, $"Operation '{request.Operation}' is not supported.", 400),
            };
        }
        catch (PhoneHomeAdmissionException ex)
        {
            return Error(request, ex.Code, ex.Message, ex.StatusCode);
        }
        catch (KeyNotFoundException ex)
        {
            return Error(request, "not_found", ex.Message, 404);
        }
        catch (VerificationCustodyException ex)
        {
            return Error(request, ex.Code, ex.Message, 409);
        }
        catch (GrokRulesLaunchException ex)
        {
            return Error(request, "grok_rules", ex.Message, 409);
        }
        catch (HerdrLaunchException ex)
        {
            return Error(request, PhoneHomeProblemTypes.UnsupportedTarget, ex.Message, 409);
        }
        catch (UnixPtyArgvException ex)
        {
            return Error(request, ex.Code, ex.Message, 409);
        }
        catch (SessionIdentityClosedException ex)
        {
            // CARD-1153 A-3: terminal refusal for a certified never-created id; the desktop never retries it.
            return Error(request, RunnerAbsenceEvidence.PhoneHomeClosedIdentityErrorType, ex.Message, 409);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Error(request, PhoneHomeProblemTypes.UnsupportedTarget, ex.Message, 400);
        }
        catch (HerdrBackendUnavailableException ex)
        {
            // CARD-1365 D-3: the phone-home lane answers the same 503 herdr_unreachable as HTTP, so
            // the server's deferral class is reached instead of an Internal frame read as a conflict.
            return Error(request, HerdrProblemTypes.Unreachable, ex.Message, 503);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // CARD-0631 D-2: any other handler fault (a git start failure, IO, JSON) still answers
            // this request. Cancellation stays cancellation; the receive pump decides what it means.
            return PhoneHomeErrorFrames.Internal(request, ex, _settings.Limits.MaxMessageUtf8Bytes);
        }
    }

    private async Task<TerminalSeatObservation> ObserveTerminalSeatAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        var body = request.Payload?.Deserialize<PhoneHomeTerminalSeatObservationRequest>(PhoneHomeFraming.Json)
            ?? throw new ArgumentException("Terminal seat observation body is required.");
        ArgumentNullException.ThrowIfNull(body.Observation);
        return await _runtime.ObserveTerminalSeatAsync(body.SessionId, body.Observation, ct);
    }

    private async Task<PhoneHomeFrame> RetireAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        var body = request.Payload?.Deserialize<RunnerRetireRequest>(PhoneHomeFraming.Json)
            ?? throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Retire body is required.", 400);
        if (string.IsNullOrWhiteSpace(body.Reason))
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Retire reason is required.", 400);
        var owned = _runtime.OwnedSessionCount;
        if (!body.Force && owned > 0)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.RunnerBusy,
                $"Runner still owns {owned} session(s).", 409);
        var killed = body.Force ? await _runtime.KillAllAsync(TimeSpan.FromSeconds(5), ct) : 0;
        Volatile.Write(ref _retireAccepted, 1);
        return Result(request, new RunnerRetireResult(_processBootId, killed,
            _time.GetUtcNow().UtcDateTime.AddMilliseconds(250)));
    }

    private async Task<PhoneHomeFrame> LaunchPlatformConstrainedAsync(
        PhoneHomeFrame request, DispatchTrace trace, CancellationToken ct)
    {
        var launch = request.Payload?.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)
            ?? throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Launch body is required.", 400);
        if (!RunnerPlatformWire.IsSpecific(launch.RequiredPlatform))
            throw new PhoneHomeAdmissionException(
                RunnerPlatformLaunchGuard.Invalid,
                "A platform-constrained launch requires requiredPlatform windows or linux.",
                409);
        return await LaunchAsync(request, trace, ct);
    }

    private async Task<PhoneHomeFrame> LaunchAsync(PhoneHomeFrame request, DispatchTrace trace, CancellationToken ct)
    {
        var launch = request.Payload?.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)
            ?? throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Launch body is required.", 400);
        RejectUnsupportedLaunch(launch);
        // A runner that names its own codex (native Windows) must not be pinned to the image path.
        if (!_settings.Executables.ContainsKey("codex"))
            launch = WithImageCodexPath(launch);
        await RejectSignedOutClaudeAsync(launch, ct);
        await RejectSignedOutGrokAsync(launch, ct);
        await RejectSignedOutCodexAsync(launch, ct);
        launch = WithConfiguredExecutable(launch, _settings.Executables);
        // Capacity has to be rechecked inside the mutation lock. A check before StartAsync
        // lets two launches both pass while neither has recorded a session yet.
        return await MutateAsync(request, async () =>
        {
            // CARD-0679 D-9: a Launch retried after its ack was lost in a reconnect. The same
            // generation is the same launch: answer with the session already running, and do it
            // before capacity, which that very session occupies. Any other generation is a typed
            // refusal the desktop can match on, not the runtime's untyped "already running".
            var sessions = _runtime.List().Where(session => session.SessionId == launch.SessionId).ToList();
            var existing = sessions.FirstOrDefault(session => session.Status != "Exited");
            // R5 repair (review 137c1631): a pre-ack re-send whose first Launch landed, ran and exited
            // before the re-send arrived. Relaunching it would run the same generation twice, so it
            // is answered with the exited session instead. A new generation (a resume) relaunches.
            var exited = sessions.FirstOrDefault(session => session.Status == "Exited"
                && SessionGeneration.Equal(session.AcceptedStartedAt, launch.AcceptedStartedAt));
            if (existing is null && exited is not null)
                return AlreadyExited(request, exited);
            if (existing is not null)
            {
                if (SessionGeneration.Equal(existing.AcceptedStartedAt, launch.AcceptedStartedAt))
                {
                    trace.Outcome = "duplicate-ack";
                    return Result(request, existing);
                }

                throw new PhoneHomeAdmissionException(
                    PhoneHomeProblemTypes.SessionAlreadyRunning,
                    $"Session '{launch.SessionId}' is already running under another generation.",
                    409);
            }

            // R5 repair 2 (review 18f52a40): the session table forgets a session on ReleaseSlot and on a
            // runner restart, either of which can land inside the desktop's pre-ack retry window. The
            // persisted watermark does not: a generation at or below the one already accepted for this
            // id is refused, so it cannot start twice. A newer generation (a resume) passes.
            if (launch.AcceptedStartedAt is { } generation
                && _launchGenerations.Read(launch.SessionId) is { } accepted
                && SessionGeneration.Compare(generation, accepted) <= 0)
            {
                throw new PhoneHomeAdmissionException(
                    PhoneHomeProblemTypes.SessionGenerationAlreadyAccepted,
                    $"Session '{launch.SessionId}' generation {SessionGeneration.Normalize(generation):O} is not newer than "
                    + $"the generation {accepted:O} runner '{_settings.RunnerId}' already accepted for it, and the runner "
                    + "no longer holds that session; it is not started again.",
                    409);
            }

            if (_runtime.OwnedSessionCount >= _capacity.Capacity)
                throw new PhoneHomeAdmissionException(
                    PhoneHomeProblemTypes.Capacity,
                    $"Phone-home capacity is {_capacity.Capacity} session(s).",
                    409);
            // Durable before the process exists: a crash between the two can only refuse a re-send.
            if (launch.AcceptedStartedAt is { } accepting)
                _launchGenerations.Record(launch.SessionId, accepting);
            // CARD-1677 D-2: retention keeps a tree one of this runner's sessions was launched in.
            _workspaceActivity.RecordLaunch(launch.SessionId, launch.Cwd);
            var started = await _runtime.StartAsync(launch, ct);
            trace.Outcome = "started";
            return Result(request, started);
        });
    }

    /// <summary>
    /// CARD-0628 D-7 / CARD-0647. Read-only: the probe's DTO for a provider this runner can measure.
    /// An unknown provider is <see cref="PhoneHomeProblemTypes.UnsupportedTarget"/>; a runner built
    /// without a probe answers "cannot tell" rather than guessing.
    /// </summary>
    private async Task<RunnerProviderAuthDto> ProviderAuthAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        var body = request.Payload?.Deserialize<PhoneHomeProviderAuthRequest>(PhoneHomeFraming.Json);
        var provider = CanonicalAuthProvider(body?.Provider);
        if (provider is null)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"Provider '{body?.Provider}' has no auth probe on this runner; only "
                + $"'{ClaudeAuthProbe.ProviderName}', '{GrokAuthProbe.ProviderName}' and '{CodexAuthProbe.ProviderName}' are measured.",
                400);
        if (_authProbe is null)
            return new RunnerProviderAuthDto(provider, null, null, null, DateTimeOffset.UtcNow, "probe_unavailable");
        return await _authProbe.ProbeAsync(provider, ct);
    }

    private static string? CanonicalAuthProvider(string? provider)
    {
        if (string.Equals(provider, ClaudeAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return ClaudeAuthProbe.ProviderName;
        if (string.Equals(provider, GrokAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return GrokAuthProbe.ProviderName;
        if (string.Equals(provider, CodexAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return CodexAuthProbe.ProviderName;
        return null;
    }

    /// <summary>
    /// CARD-0628 D-7 backstop. The server's pre-flight should already have failed a task bound to
    /// a signed-out runner; this is the runner refusing to start Claude onto its sign-in screen
    /// when that pre-flight did not run (an older server, the setting off, a named agent). Only a
    /// definite "signed out" refuses: an unknown answer, or no probe, admits.
    /// </summary>
    private async Task RejectSignedOutClaudeAsync(RunnerLaunchRequest launch, CancellationToken ct)
    {
        if (!_settings.ClaudeAuthProbeEnabled || _authProbe is null || !IsClaudeExe(launch.Exe))
            return;
        var answer = await _authProbe.ProbeAsync(ClaudeAuthProbe.ProviderName, ct);
        if (answer.LoggedIn != false)
            return;
        throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.ProviderSignInRequired,
            $"Claude Code is not signed in on runner '{_settings.RunnerId}' (CLAUDE_CONFIG_DIR={_settings.ClaudeHome}). "
            + $"Provision {ClaudeAuthProbe.OAuthTokenVariable} (from `claude setup-token`) in the runner's environment, "
            + $"or run `claude auth login` as uid 1654 with CLAUDE_CONFIG_DIR={_settings.ClaudeHome}, then re-dispatch.",
            409);
    }

    /// <summary>
    /// CARD-0647. Same backstop for Grok: only a definite "signed out" (no <c>auth.json</c>) refuses.
    /// The probe never opens the file, so this message never carries its contents.
    /// </summary>
    private async Task RejectSignedOutGrokAsync(RunnerLaunchRequest launch, CancellationToken ct)
    {
        if (!_settings.GrokAuthProbeEnabled || _authProbe is null || !IsGrokExe(launch.Exe))
            return;
        var answer = await _authProbe.ProbeAsync(GrokAuthProbe.ProviderName, ct);
        if (answer.LoggedIn != false)
            return;
        throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.ProviderSignInRequired,
            $"Grok is not signed in on runner '{_settings.RunnerId}' (GROK_HOME={_settings.GrokHome}). "
            + $"Run `grok login` as uid 1654 with GROK_HOME={_settings.GrokHome}, then re-dispatch.",
            409);
    }

    /// <summary>
    /// CARD-0660 D-7/D-9. Same backstop for Codex: only a definite "signed out" (no
    /// <c>CODEX_HOME/auth.json</c>) refuses; unknown, no probe, or the setting off admits. The
    /// probe reads metadata only, so this message never carries the file's contents.
    /// </summary>
    private async Task RejectSignedOutCodexAsync(RunnerLaunchRequest launch, CancellationToken ct)
    {
        if (!_settings.CodexAuthProbeEnabled || _authProbe is null || !IsCodexExe(launch.Exe))
            return;
        var answer = await _authProbe.ProbeAsync(CodexAuthProbe.ProviderName, ct);
        if (answer.LoggedIn != false)
            return;
        throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.ProviderSignInRequired,
            $"Codex is not signed in on runner '{_settings.RunnerId}' (CODEX_HOME={_settings.CodexHome}). "
            + $"Run `codex login --device-auth` as uid 1654 with CODEX_HOME={_settings.CodexHome}, then re-dispatch.",
            409);
    }

    /// <summary>
    /// CARD-0640. The image's own <c>grok</c>, by bare name or the exact path the runner
    /// Dockerfile installs it to. Any other path, even one ending in <c>/grok</c>
    /// (<c>/opt/evil/grok</c>, a <c>..</c> escape, a relative <c>./grok</c>), is not this image's.
    /// </summary>
    internal static bool IsGrokExe(string? exe) =>
        string.Equals(exe, "grok", StringComparison.Ordinal)
        || string.Equals(exe, ImageGrokPath, StringComparison.Ordinal);

    /// <summary>Where <c>docker/session-runner-grok/Dockerfile</c> installs the Grok CLI.</summary>
    internal const string ImageGrokPath = "/usr/local/bin/grok";

    /// <summary>
    /// CARD-0628 D-5: the image's own <c>claude</c>, by bare name or the exact path the runner
    /// Dockerfile installs it to. Any other path, even one ending in <c>/claude</c>
    /// (<c>/opt/evil/claude</c>, a <c>..</c> escape, a relative <c>./claude</c>), is not this image's.
    /// </summary>
    internal static bool IsClaudeExe(string? exe) =>
        string.Equals(exe, "claude", StringComparison.Ordinal)
        || string.Equals(exe, ImageClaudePath, StringComparison.Ordinal);

    /// <summary>Where <c>docker/session-runner-grok/Dockerfile</c> installs Claude Code.</summary>
    internal const string ImageClaudePath = "/usr/local/bin/claude";

    /// <summary>
    /// CARD-0660 D-7: the image's own native <c>codex</c>, by bare name or the exact link the runner
    /// Dockerfile installs. The desktop's <c>codex.cmd</c>, another directory's <c>codex</c>, a
    /// relative <c>./codex</c> or a <c>..</c> escape is not this image's.
    /// </summary>
    internal static bool IsCodexExe(string? exe) =>
        string.Equals(exe, "codex", StringComparison.Ordinal)
        || string.Equals(exe, ImageCodexPath, StringComparison.Ordinal);

    /// <summary>Where <c>docker/session-runner-grok/Dockerfile</c> links the native Codex CLI.</summary>
    internal const string ImageCodexPath = "/usr/local/bin/codex";

    /// <summary>
    /// CARD-0660 D-7 (review of ff170389): an admitted bare <c>codex</c> launches as
    /// <see cref="ImageCodexPath"/>, so which binary runs never depends on a PATH lookup in the
    /// child's environment. Every other field, the launch's own PATH included, is unchanged.
    /// </summary>
    internal static RunnerLaunchRequest WithImageCodexPath(RunnerLaunchRequest launch) =>
        string.Equals(launch.Exe, "codex", StringComparison.Ordinal) ? launch with { Exe = ImageCodexPath } : launch;

    /// <summary>
    /// A native Windows runner: an admitted bare <c>claude</c>, <c>codex</c> or <c>grok</c> starts the
    /// executable this host configured under <see cref="PhoneHomeSettings.Executables"/>, so the
    /// process never depends on a PATH lookup in the child's environment. Runs after admission and
    /// the sign-in probes, which are keyed on the bare name. Anything else is unchanged.
    /// </summary>
    internal static RunnerLaunchRequest WithConfiguredExecutable(
        RunnerLaunchRequest launch, IReadOnlyDictionary<string, string>? executables) =>
        executables is not null && launch.Exe is { } exe
            && PhoneHomeSettings.KnownExecutableNames.Contains(exe)
            && executables.TryGetValue(exe, out var path) && !string.IsNullOrWhiteSpace(path)
            ? launch with { Exe = path }
            : launch;

    internal void RejectUnsupportedLaunch(RunnerLaunchRequest launch)
    {
        // CARD-0604 D-2 / CARD-0628 D-5 / CARD-0660 D-7: grok, claude, codex, or an image-owned executable from the allow list.
        // The runner keeps its own copy of that list: it is the image's contract, and the server telling it to run
        // some other path is exactly what this refusal exists for.
        var isGrok = IsGrokExe(launch.Exe);
        var isAllowedRaw = !string.IsNullOrWhiteSpace(launch.Exe)
            && _settings.RawExeAllowList.Any(allowed => string.Equals(allowed, launch.Exe, StringComparison.Ordinal));
        var isClaude = IsClaudeExe(launch.Exe);
        var isCodex = IsCodexExe(launch.Exe);
        if (!isGrok && !isClaude && !isCodex && !isAllowedRaw)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Only an image-owned executable may launch.", 409);

        // CARD-0604 D-15: the workspace root itself, or a mirror worktree directly under it. A
        // path that merely starts with the same characters ("/workspace") is not under "/work",
        // and "/work/worktrees/../.." must not escape it.
        // CARD-1695: one standing checkout directly under <AllowedCwd>/agents/<segment>.
        if (!IsAdmittedCwd(launch.Cwd))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"cwd must be '{_settings.AllowedCwd}' or a worktree directly under '{_settings.AllowedCwd}/worktrees/' or a standing checkout directly under '{_settings.AllowedCwd}/agents/<segment>'.",
                409);
        if (!string.Equals(launch.Backend, SessionBackends.PtyHost, StringComparison.OrdinalIgnoreCase)
            && launch.Backend is not null)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Herdr and unknown backends are refused.", 409);
        if (launch.Herdr is not null)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Herdr launches are refused.", 409);
        // CARD-0604 D-19 / G-37 (Cut B). A verification binding is admitted only when this
        // runner actually advertises a custody backend AND the binding names that exact backend
        // and this runner's own store. Cut A refused every binding outright because the runner
        // had no containment at all; refusing on capability rather than on principle is what
        // makes a Linux SourceLanding possible without ever fabricating a Windows receipt.
        if (launch.VerificationBinding is { } binding)
        {
            if (_runtime.VerificationCustodyBackend is not { } advertised)
                throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                    "This runner advertises no verification custody backend.", 409);
            if (!VerificationCustodyBackends.IsSupported(binding.Backend) || binding.Backend != advertised)
                throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                    "verification_custody_invalid_binding", 409);
            if (binding.RunnerStoreId != _runtime.RunnerStoreId)
                throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.StoreMismatch,
                    "verification_custody_invalid_binding", 409);
        }
        if (launch.MemoryLimitMb != 0)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Memory limit must be zero.", 409);
        // CARD-0628 D-5 / CARD-0660: the three agents this image carries, each with a runtime tailer
        // (Codex's CodexTranscriptTailer reads the launch's CODEX_HOME). Anything else has none here.
        if (launch.TranscriptFormat is not null
            && !string.Equals(launch.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(launch.TranscriptFormat, TranscriptFormats.Claude, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(launch.TranscriptFormat, TranscriptFormats.Codex, StringComparison.OrdinalIgnoreCase))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Only the Grok, Claude and Codex transcript formats are admitted.", 409);
    }

    /// <summary>
    /// CARD-0604 D-15/G-22. The allowed cwd, or a single-segment mirror directly under its
    /// <c>worktrees/</c> directory. Traversal, absolute escapes and sibling prefixes are refused.
    /// </summary>
    private async Task<PhoneHomeFrame> ReadWorkspaceRepositoryIdentityAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        var body = request.Payload?.Deserialize<WorkspaceRepositoryIdentityRequest>(PhoneHomeFraming.Json)
            ?? throw new ArgumentException("Workspace repository identity body is required.");
        if (body.Version != 1 || !WorkspaceRepositoryIdentityRequest.Supported(_runtime.Capabilities()))
            return Result(request, new WorkspaceRepositoryIdentityResult(WorkspaceRepositoryIdentityOutcome.Held, "identity_unsupported"));
        return Result(request, await _runtime.ReadWorkspaceRepositoryIdentityAsync(body, ct));
    }

    /// <summary>
    /// CARD-1677 D-6. Phase A (the recent-use guard of D-2, every check, then <c>git worktree move</c>) runs under the mutation lock;
    /// phase B (the unforced <c>git worktree remove</c> of a multi-GiB tree) runs after the lock is
    /// released, so it never stalls a session's Input. Phase B runs only for a moved tree.
    /// </summary>
    private async Task<PhoneHomeFrame> RetainAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        RequireRetentionCapability();
        var body = request.Payload?.Deserialize<PhoneHomeWorkspaceRetainRequest>(PhoneHomeFraming.Json)
            ?? throw new ArgumentException("Workspace retain body is required.");
        WorkspaceRetainStep? step = null;
        var locked = await MutateAsync(request, async () =>
        {
            // D-2: under the same lock as Mirror and Launch, so a re-dispatch after the server's reread is seen here.
            var live = _runtime.List().Where(session => session.Status != "Exited").Select(session => session.SessionId);
            if (_workspaceActivity.Covers(body.Path ?? "", TimeSpan.FromMinutes(_settings.RetentionRecentUseMinutes), live) is { } use)
                return Result(request, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, use.Reason, Detail: use.Detail));
            step = await _runtime.RetainWorkspaceCheckAndMoveAsync(body, ct);
            return Result(request, step.Final ?? new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept));
        });
        if (step is not { Moved: true })
            return locked;
        return Result(request, await _runtime.RetainWorkspaceCompleteAsync(step, ct));
    }

    private async Task<PhoneHomeFrame> RetentionInventoryAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        RequireRetentionCapability();
        return Result(request, await _runtime.WorkspaceRetentionInventoryAsync(ct));
    }

    /// <summary>
    /// CARD-1677 S2. Not a workspace mutation (it never touches the worktree root), so it never takes the mutation
    /// lock: a long sweep cannot stall a session's Input. Only a surface that advertises tempSweepV1 reaches it.
    /// </summary>
    private async Task<PhoneHomeFrame> TempSweepAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        if (_runtime.Capabilities().Features?.Contains(RunnerCapabilityFeatures.TempSweepV1, StringComparer.Ordinal) != true)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
                "Temp sweep is not supported on this runner.", 501);
        var body = request.Payload?.Deserialize<PhoneHomeTempSweepRequest>(PhoneHomeFraming.Json)
            ?? throw new ArgumentException("Temp sweep body is required.");
        return Result(request, await _runtime.TempSweepAsync(body, ct));
    }

    // A surface that does not advertise retention never reaches its (refusing) members or any engine.
    private void RequireRetentionCapability()
    {
        if (_runtime.Capabilities().Features?.Contains(RunnerCapabilityFeatures.WorkspaceRetentionV1, StringComparer.Ordinal) != true)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedOperation,
                "Workspace retention is not supported on this runner.", 501);
    }

    internal bool IsAdmittedCwd(string? cwd) =>
        RunnerWorkspaceCwd.IsAdmitted(cwd, _settings.AllowedCwd, OperatingSystem.IsWindows());

    private async Task<PhoneHomeFrame> MutateAsync(PhoneHomeFrame request, Func<Task<PhoneHomeFrame>> action)
    {
        lock (_mutationGate)
        {
            // Serialize mutating commands for the single session without blocking the receive pump:
            // the caller awaits this task off the socket loop.
        }

        await MutationLock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            MutationLock.Release();
        }
    }

    // Strict version 1 body (the HTTP route's parser) bound to this runner's store; a foreign store
    // is refused before the runtime is entered.
    private RunnerAbsenceRequest ReadAbsenceRequest(PhoneHomeFrame request)
    {
        var parsed = request.Payload is { ValueKind: JsonValueKind.Object } payload
            ? AbsenceEvidenceRoutes.ParseRequest(System.Text.Encoding.UTF8.GetBytes(payload.GetRawText()))
            : null;
        if (parsed is null)
            throw new PhoneHomeAdmissionException(RunnerAbsenceRefusalCodes.InvalidRequest, "Malformed absence evidence body.", 400);
        var store = _runtime.RunnerStoreId;
        if (store == Guid.Empty || parsed.RunnerStoreId != store)
            throw new PhoneHomeAdmissionException(RunnerAbsenceRefusalCodes.BindingMismatch,
                "Absence evidence names another runner store.", 409);
        return parsed;
    }

    private static PhoneHomeFrame AbsenceFrame<T>(PhoneHomeFrame request, RunnerAbsenceOutcome<T> outcome) where T : class =>
        outcome.Value is { } value
            ? Result(request, value)
            : Error(request, outcome.Refusal!.Code, outcome.Refusal.Reason, outcome.Refusal.Status);

    // CARD-1764. A missing or malformed cursor is zero: the full transcript.
    private static long ReadSince(PhoneHomeFrame request) =>
        request.Payload is { ValueKind: JsonValueKind.Object } payload
        && payload.TryGetProperty(TranscriptSince.SinceName, out var since)
        && since.ValueKind == JsonValueKind.Number
        && since.TryGetInt64(out var value)
        && value > 0
            ? value
            : 0;

    private static string? ReadStream(PhoneHomeFrame request) =>
        request.Payload is { ValueKind: JsonValueKind.Object } payload
        && payload.TryGetProperty(TranscriptSince.StreamName, out var stream)
        && stream.ValueKind == JsonValueKind.String
            ? stream.GetString()
            : null;

    private static string? ReadIfNoneMatch(PhoneHomeFrame request)
    {
        if (request.Payload is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("ifNoneMatch", out var match)
            || match.ValueKind != JsonValueKind.String)
            return null;
        var text = match.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static PhoneHomeFrame ConditionalFrame<T>(PhoneHomeFrame request, PtyRead<T> read, JsonTypeInfo<T> type)
        where T : class
    {
        if (read.NotModified)
        {
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                JsonSerializer.SerializeToElement(new PtyNotModified(true), PhoneHomeFraming.Json),
                Match: read.ETag);
        }

        var frame = Result(request, read.Body!, type);
        return string.IsNullOrEmpty(read.ETag) ? frame : frame with { Match = read.ETag };
    }

    private sealed record PtyNotModified(bool NotModified);

    private static Guid ReadSessionId(PhoneHomeFrame request)
    {
        if (request.Payload is { } payload && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("sessionId", out var id))
            return id.GetGuid();
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "sessionId is required.", 400);
    }

    private PhoneHomeFrame HostStatsResult(PhoneHomeFrame request)
    {
        if (_hostStats is null)
            return Error(request, PhoneHomeProblemTypes.UnsupportedOperation, "Host stats are not supported on this runner.", 400);
        return Result(request, _hostStats.Snapshot());
    }

    private PhoneHomeFrame HostLoadHistoryResult(PhoneHomeFrame request)
    {
        if (_hostLoad is null)
            return Error(request, PhoneHomeProblemTypes.UnsupportedOperation, "Host load history is unsupported.", 400);
        var body = request.Payload?.Deserialize<PhoneHomeHostLoadHistoryRequest>(PhoneHomeFraming.Json)
            ?? new PhoneHomeHostLoadHistoryRequest();
        try { return Result(request, _hostLoad.ReadHistory(body.After, body.Limit)); }
        catch (ArgumentException) { return Error(request, "invalid_host_load_query", "Invalid history cursor or limit.", 400); }
        catch (IOException) { return Error(request, "host_load_history_unavailable", "History is unavailable.", 503); }
    }

    private PhoneHomeFrame HostStatsSeriesResult(PhoneHomeFrame request)
    {
        if (_hostStats is null)
            return Error(request, PhoneHomeProblemTypes.UnsupportedOperation, "Host stats are not supported on this runner.", 400);
        var body = request.Payload?.Deserialize<PhoneHomeHostSeriesRequest>(PhoneHomeFraming.Json)
            ?? throw new ArgumentException("Host stats series body is required.");
        try
        {
            return Result(request, _hostStats.Series(body.Metric, body.Window));
        }
        catch (ArgumentException ex)
        {
            return Error(request, HostStatsRoutes.InvalidQueryCode, ex.Message, 400);
        }
    }

    private static PhoneHomeFrame Result(PhoneHomeFrame request, object payload) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    private static PhoneHomeFrame Result<T>(PhoneHomeFrame request, T payload, JsonTypeInfo<T> type) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, RunnerReadJson.Use(type)));

    private static PhoneHomeFrame AlreadyExited(PhoneHomeFrame request, RunnerSessionDto exited) =>
        new(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(exited, PhoneHomeFraming.Json),
            ErrorCode: PhoneHomeProblemTypes.SessionAlreadyExited,
            ErrorDetail: $"Session '{exited.SessionId}' already ran under this generation and exited "
                + $"(exit code {exited.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}, reason {exited.ExitReason}).",
            StatusCode: 409);

    private static PhoneHomeFrame Error(PhoneHomeFrame request, string code, string detail, int status) =>
        new(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId, request.Operation,
            ErrorCode: code, ErrorDetail: detail, StatusCode: status);

    private readonly SemaphoreSlim MutationLock = new(1, 1);
}

public sealed class PhoneHomeAdmissionException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
