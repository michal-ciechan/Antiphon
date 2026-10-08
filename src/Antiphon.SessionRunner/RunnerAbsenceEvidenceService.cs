using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public enum RunnerAbsenceRuntimeEntry { None, Live, Exited }

/// <summary>
/// CARD-1153 A-10: what the certify and prepare decisions read about the runtime. The runtime
/// supplies the real implementation; tests drive it without a process. This seam changes no
/// decision: every member is a positive-absence precondition.
/// </summary>
public interface IRunnerAbsenceInspection
{
    /// <summary>Startup adoption completed in this process.</summary>
    bool AdoptionComplete { get; }

    /// <summary>Any runtime entry of any status or generation.</summary>
    RunnerAbsenceRuntimeEntry RuntimeEntry(Guid sessionId);

    /// <summary>
    /// Names of retained artifacts for the id (manifest, sidecars, custody, watermark). Throws when
    /// a probe cannot answer; the caller treats that as unknown evidence, never as absence.
    /// </summary>
    IReadOnlyList<string> RetainedArtifacts(Guid sessionId);
}

/// <summary>
/// The production artifact probe. Opens are strict: only file-not-found / directory-not-found
/// is absence; a denied open or any IO error throws. Corrupt or empty artifacts count as present.
/// </summary>
public sealed class RunnerAbsenceArtifactInspection(
    SessionRunnerSettings settings,
    Func<Guid, RunnerAbsenceRuntimeEntry> runtimeEntry,
    Func<bool> adoptionComplete,
    Func<Guid, bool> custodyReserved,
    string? launchGenerationsPath) : IRunnerAbsenceInspection
{
    public const string Manifest = "manifest";
    public const string TranscriptSidecar = "transcript-sidecar";
    public const string HerdrSidecar = "herdr-sidecar";
    public const string HerdrLastPane = "herdr-last-pane";
    public const string Custody = "custody-reservation";
    public const string Watermark = "launch-generation-watermark";

    public bool AdoptionComplete => adoptionComplete();

    public RunnerAbsenceRuntimeEntry RuntimeEntry(Guid sessionId) => runtimeEntry(sessionId);

    public IReadOnlyList<string> RetainedArtifacts(Guid sessionId)
    {
        var found = new List<string>();
        if (Present(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId))) found.Add(Manifest);
        if (Present(Antiphon.SessionRunner.TranscriptSidecar.PathFor(settings.SessionLogPath, sessionId))) found.Add(TranscriptSidecar);
        if (Present(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId))) found.Add(HerdrSidecar);
        if (Present(Antiphon.SessionRunner.HerdrLastPane.PathFor(settings.SessionLogPath, sessionId))) found.Add(HerdrLastPane);
        if (custodyReserved(sessionId)) found.Add(Custody);
        if (!string.IsNullOrWhiteSpace(launchGenerationsPath)
            && Present(Path.Combine(launchGenerationsPath, sessionId.ToString("N") + ".generation")))
            found.Add(Watermark);
        return found;
    }

    internal static bool Present(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}

/// <summary>
/// CARD-1153 D-1/D-2: the single production decision behind both transports. Callers hold the
/// per-session launch gate; an internal lock keeps record transitions atomic among themselves.
/// Acceptance is a whitelist: a same-epoch Prepared (or, for a reissue, ClosedUnused) record with
/// the exact identity, complete adoption, no latched fault, no runtime entry and no retained
/// artifact. Anything else is a typed refusal.
/// </summary>
public sealed class RunnerAbsenceEvidenceService
{
    private readonly RunnerAbsenceEvidenceStore _store;
    private readonly IRunnerAbsenceInspection _inspection;
    private readonly Func<Guid> _currentStore;
    private readonly TimeProvider _time;
    private readonly ILogger? _logger;
    private readonly object _sync = new();
    private string? _latchReason;
    private readonly DateTime _startedAtUtc;
    private readonly Dictionary<string, DateTime> _consumedNonces = new(StringComparer.Ordinal);
    private DateTime? _highWaterIssuedUtc;
    private long _observedTimestamp;
    private DateTime _observedWallUtc;
    private long? _clockStepTimestamp;

    /// <summary>F2: the replay cache never evicts a live nonce; when full, requests refuse (503).</summary>
    public const int MaxConsumedNonces = 4096;

    /// <summary>F2 round 2: a backward wall-clock step larger than this (plus slew) is a clock rollback.</summary>
    public static readonly TimeSpan ClockStepTolerance = TimeSpan.FromMilliseconds(500);

    /// <summary>A-4: random per instance, memory only, compared by equality, never derived from time.</summary>
    public Guid Epoch { get; } = Guid.NewGuid();

    public RunnerAbsenceEvidenceService(
        RunnerAbsenceEvidenceStore store,
        IRunnerAbsenceInspection inspection,
        Func<Guid> currentStore,
        TimeProvider? time = null,
        ILogger? logger = null)
    {
        _store = store;
        _inspection = inspection;
        _currentStore = currentStore;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        _startedAtUtc = _time.GetUtcNow().UtcDateTime;
        _observedTimestamp = _time.GetTimestamp();
        _observedWallUtc = _startedAtUtc;
    }

    public RunnerAbsenceEvidenceStore Store => _store;

    public string? LatchReason { get { lock (_sync) return _latchReason; } }

    /// <summary>Capability gate: adoption done, no latch, store healthy or never initialized.</summary>
    public bool Ready
    {
        get
        {
            lock (_sync)
                return Unready() is null;
        }
    }

    public RunnerAbsenceOutcome<RunnerAbsencePrepared> Prepare(RunnerAbsenceRequest request)
    {
        if (InvalidRequest(request) is { } invalid)
            return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(RunnerAbsenceRefusalCodes.InvalidRequest, 400, invalid);
        lock (_sync)
        {
            if (AdmitOnceLocked(request) is { } replay)
                return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(replay.Code, replay.Status, replay.Reason);
            if (Unready() is { } unready)
                return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(RunnerAbsenceRefusalCodes.Unavailable, 503, unready);
            if (StoreRefusal(request.RunnerStoreId) is { } storeRefusal)
                return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(storeRefusal.Code, storeRefusal.Status, storeRefusal.Reason);

            var read = _store.Read(request.SessionId);
            if (!read.IsKnown)
                return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(RunnerAbsenceRefusalCodes.Unavailable, 503,
                    RunnerAbsenceEvidenceStore.Describe(read));
            if (read.Record is { } existing)
            {
                // Repeated prepare returns the original record only for the identical identity in
                // this epoch. Attempted, ClosedUnused, foreign or prior-epoch records are never overwritten.
                if (existing.RuntimeEpoch != Epoch)
                    return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(RunnerAbsenceRefusalCodes.PriorEpoch, 409, "record from an earlier runner epoch");
                if (existing.State != RunnerAbsenceRecordState.Prepared)
                    return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(RunnerAbsenceRefusalCodes.IdentityKnown, 409, $"record is {existing.State}");
                if (!SessionGeneration.Equal(existing.AcceptedStartedAt, request.AcceptedStartedAt)
                    || existing.RunnerStoreId != request.RunnerStoreId)
                    return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(RunnerAbsenceRefusalCodes.BindingMismatch, 409, "record binding differs");
            }

            if (KnownHistory(request.SessionId) is { } history)
                return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(history.Code, history.Status, history.Reason);

            if (read.Record is null)
            {
                var record = new RunnerAbsenceRecord(RunnerAbsenceRecordState.Prepared, request.SessionId,
                    SessionGeneration.Normalize(request.AcceptedStartedAt), request.RunnerStoreId, Epoch,
                    _time.GetUtcNow().UtcDateTime);
                try { _store.Write(record); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Refuse(RunnerAbsenceRefusalCodes.Unavailable, 503, "prepare write failed");
                }
            }

            return RunnerAbsenceOutcome<RunnerAbsencePrepared>.Ok(new RunnerAbsencePrepared(
                RunnerAbsenceEvidence.Version, nameof(RunnerAbsenceRecordState.Prepared), request.SessionId,
                SessionGeneration.Normalize(read.Record?.AcceptedStartedAt ?? request.AcceptedStartedAt),
                request.RunnerStoreId, Epoch, request.RequestNonce));
        }
    }

    public RunnerAbsenceOutcome<RunnerAbsenceCertificate> Certify(RunnerAbsenceRequest request)
    {
        if (InvalidRequest(request) is { } invalid)
            return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.InvalidRequest, 400, invalid);
        lock (_sync)
        {
            if (AdmitOnceLocked(request) is { } replay)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(replay.Code, replay.Status, replay.Reason);
            if (Unready() is { } unready)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.Unavailable, 503, unready);
            var read = _store.Read(request.SessionId);
            if (read.Kind == RunnerAbsenceReadKind.NoRecord)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.NotPrepared, 404, "never prepared");
            if (read.Record is not { } record)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.Unavailable, 503,
                    RunnerAbsenceEvidenceStore.Describe(read));
            if (record.RuntimeEpoch != Epoch)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.PriorEpoch, 409, "record from an earlier runner epoch");
            if (record.State is not (RunnerAbsenceRecordState.Prepared or RunnerAbsenceRecordState.ClosedUnused))
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.IdentityKnown, 409, $"record is {record.State}");
            if (!SessionGeneration.Equal(record.AcceptedStartedAt, request.AcceptedStartedAt)
                || record.RunnerStoreId is not { } recordStore || recordStore != request.RunnerStoreId)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.BindingMismatch, 409, "record binding differs");
            if (StoreRefusal(request.RunnerStoreId) is { } storeRefusal)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(storeRefusal.Code, storeRefusal.Status, storeRefusal.Reason);
            if (KnownHistory(request.SessionId) is { } history)
                return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(history.Code, history.Status, history.Reason);

            if (record.State == RunnerAbsenceRecordState.Prepared)
            {
                // D-2: close the identity durably BEFORE replying, so a delayed create is refused.
                try
                {
                    _store.Write(record with
                    {
                        State = RunnerAbsenceRecordState.ClosedUnused, UpdatedAtUtc = _time.GetUtcNow().UtcDateTime,
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LatchLocked("close write failed: " + ex.GetType().Name);
                    return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Refuse(RunnerAbsenceRefusalCodes.Unavailable, 503, "close write failed");
                }
            }

            return RunnerAbsenceOutcome<RunnerAbsenceCertificate>.Ok(new RunnerAbsenceCertificate(
                RunnerAbsenceEvidence.Version, RunnerAbsenceEvidence.NeverCreated, request.SessionId,
                SessionGeneration.Normalize(record.AcceptedStartedAt!.Value), recordStore, Epoch, request.RequestNonce,
                Complete: true, CreationObserved: false, NativeTranscriptPresent: false,
                SidecarTranscriptPresent: false, ProcessPresent: false, IdentityClosed: true,
                ObservedAtUtc: _time.GetUtcNow().UtcDateTime));
        }
    }

    /// <summary>
    /// D-1/D-2: every creation or attach entry calls this under the launch gate before its first
    /// effect. Admission is a whitelist (CARD-1153 F1): only a store that is healthy or never
    /// initialized, answering no record or a Prepared/Attempted record, admits. A ClosedUnused
    /// record (any epoch, any generation) refuses with <see cref="SessionIdentityClosedException"/>,
    /// and so does every unknown read (corrupt, truncated or missing closed record, denied or
    /// failed read, lost root, changed or missing store header or anchor): a closure must survive
    /// storage failure, so unknown evidence is never read as an open identity. Only the optional
    /// Attempted write may fail open after a positive read: it latches evidence unavailable for
    /// this runtime and lets the launch proceed, because the read already proved no closure.
    /// </summary>
    public void RecordCreationAttempt(Guid sessionId, DateTime? acceptedStartedAt)
    {
        lock (_sync)
        {
            var read = AdmitLocked(sessionId, "attempt");
            if (read.Record is { State: RunnerAbsenceRecordState.Attempted })
                return;

            try
            {
                Guid? store = read.Record?.RunnerStoreId;
                if (store is null)
                {
                    try { store = _currentStore(); }
                    catch (Exception) { store = null; }
                }

                _store.Write(new RunnerAbsenceRecord(RunnerAbsenceRecordState.Attempted, sessionId,
                    acceptedStartedAt is { } g ? SessionGeneration.Normalize(g) : null,
                    store is { } s && s != Guid.Empty ? s : null, Epoch, _time.GetUtcNow().UtcDateTime));
            }
            catch (RunnerAbsenceStoreUnknownException ex)
            {
                // The store turned unknown between the read and the write: no positive read remains.
                LatchLocked("attempt write found the store unknown");
                throw new SessionIdentityClosedException(sessionId, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LatchLocked("attempt write failed: " + ex.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Read-only admission fence with the same whitelist as <see cref="RecordCreationAttempt"/>:
    /// refuses a ClosedUnused id or any unknown read with <see cref="SessionIdentityClosedException"/>
    /// and writes nothing, so a creation that later fails validation keeps today's no-disk-effect
    /// refusal. An unknown read also latches.
    /// </summary>
    public void RequireOpenIdentity(Guid sessionId)
    {
        lock (_sync)
            AdmitLocked(sessionId, "fence");
    }

    private RunnerAbsenceRead AdmitLocked(Guid sessionId, string seam)
    {
        var read = _store.Read(sessionId);
        if (read.Record is { State: RunnerAbsenceRecordState.ClosedUnused })
            throw new SessionIdentityClosedException(sessionId);
        if (!read.IsKnown)
        {
            var problem = RunnerAbsenceEvidenceStore.Describe(read);
            LatchLocked(seam + " read unknown: " + problem);
            throw new SessionIdentityClosedException(sessionId, problem);
        }

        return read;
    }

    /// <summary>Latch evidence unavailable for the rest of this runtime.</summary>
    public void Latch(string reason)
    {
        lock (_sync) LatchLocked(reason);
    }

    private void LatchLocked(string reason)
    {
        _latchReason ??= reason;
        _logger?.LogWarning("Absence evidence latched unavailable for this runner epoch: {Reason}", reason);
    }

    /// <summary>
    /// CARD-1153 F2: each well-formed request is admitted at most once per runner epoch, whatever
    /// its outcome or operation. Fresh means issued within <see cref="RunnerAbsenceEvidence.RequestFreshness"/>
    /// of this clock and after this epoch's start plus that window: the cache is memory only, so a
    /// request captured before a restart is refused as stale instead of meeting an empty cache.
    /// <para>
    /// Round 2 (Review a086fe80 F2), admission is monotone and never depends on the wall clock
    /// moving forward. A high-water mark holds the latest issuedAtUtc that passed the freshness
    /// checks in this epoch (it can never exceed this clock plus the window); a request issued
    /// more than the window before it is refused for good, and a consumed nonce is forgotten only
    /// once its request is below that mark, so eviction never revives a replay whatever the wall
    /// clock does. The wall clock is also checked against the monotonic clock at every request: a
    /// backward step beyond <see cref="ClockStepTolerance"/> (plus 0.1% of the monotonic gap, for
    /// NTP slew) refuses every evidence request until a full window of monotonic time has passed.
    /// </para>
    /// </summary>
    private RunnerAbsenceRefusal? AdmitOnceLocked(RunnerAbsenceRequest request)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var timestamp = _time.GetTimestamp();
        var window = RunnerAbsenceEvidence.RequestFreshness;
        var gap = _time.GetElapsedTime(_observedTimestamp, timestamp);
        if (now < _observedWallUtc + gap - ClockStepTolerance - gap / 1000)
            _clockStepTimestamp = timestamp;
        _observedTimestamp = timestamp;
        _observedWallUtc = now;
        if (_clockStepTimestamp is { } step && _time.GetElapsedTime(step, timestamp) < window)
            return new(RunnerAbsenceRefusalCodes.Unavailable, 503, "runner clock stepped backwards; evidence requests refuse for one freshness window");

        var issued = request.IssuedAtUtc!.Value;
        if (issued < now - window || issued > now + window)
            return new(RunnerAbsenceRefusalCodes.StaleRequest, 401, "request issued outside the freshness window");
        if (issued <= _startedAtUtc + window)
            return new(RunnerAbsenceRefusalCodes.StaleRequest, 401, "request issued before this runner epoch excludes replays");
        if (_highWaterIssuedUtc is { } mark && issued < mark - window)
            return new(RunnerAbsenceRefusalCodes.StaleRequest, 401, "request issued a window before a request already seen");
        if (_highWaterIssuedUtc is null || issued > _highWaterIssuedUtc)
            _highWaterIssuedUtc = issued;
        var retired = _highWaterIssuedUtc.Value - window;
        foreach (var expired in _consumedNonces.Where(e => e.Value < retired).Select(e => e.Key).ToList())
            _consumedNonces.Remove(expired);
        if (_consumedNonces.ContainsKey(request.RequestNonce))
            return new(RunnerAbsenceRefusalCodes.Replayed, 409, "request nonce already presented");
        if (_consumedNonces.Count >= MaxConsumedNonces)
            return new(RunnerAbsenceRefusalCodes.Unavailable, 503, "replay cache full");
        _consumedNonces[request.RequestNonce] = issued;
        return null;
    }

    private string? Unready()
    {
        if (_latchReason is { } latched) return "evidence latched: " + latched;
        if (!SafeAdoptionComplete()) return "adoption incomplete";
        if (_store.UnknownReason is { } unknown) return "evidence store unknown: " + unknown;
        return null;
    }

    private bool SafeAdoptionComplete()
    {
        try { return _inspection.AdoptionComplete; }
        catch (Exception) { return false; }
    }

    private RunnerAbsenceRefusal? StoreRefusal(Guid requested)
    {
        Guid current;
        try { current = _currentStore(); }
        catch (Exception) { return new(RunnerAbsenceRefusalCodes.Unavailable, 503, "runner store unknown"); }
        return current != Guid.Empty && current == requested
            ? null
            : new(RunnerAbsenceRefusalCodes.BindingMismatch, 409, "runner store differs");
    }

    private RunnerAbsenceRefusal? KnownHistory(Guid sessionId)
    {
        RunnerAbsenceRuntimeEntry entry;
        IReadOnlyList<string> artifacts;
        try
        {
            entry = _inspection.RuntimeEntry(sessionId);
            artifacts = _inspection.RetainedArtifacts(sessionId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(RunnerAbsenceRefusalCodes.Unavailable, 503, "inspection failed: " + ex.GetType().Name);
        }

        if (entry != RunnerAbsenceRuntimeEntry.None)
            return new(RunnerAbsenceRefusalCodes.IdentityKnown, 409, $"runtime entry {entry}");
        if (artifacts.Count > 0)
            return new(RunnerAbsenceRefusalCodes.IdentityKnown, 409, "retained " + string.Join(",", artifacts));
        return null;
    }

    private static string? InvalidRequest(RunnerAbsenceRequest? request)
    {
        if (request is null) return "missing body";
        if (request.Version != RunnerAbsenceEvidence.Version) return "unsupported version";
        if (request.SessionId == Guid.Empty) return "empty session id";
        if (request.RunnerStoreId == Guid.Empty) return "empty runner store";
        if (request.AcceptedStartedAt == default) return "missing generation";
        if (!RunnerAbsenceEvidence.IsValidNonce(request.RequestNonce)) return "nonce must be 32 random bytes";
        if (request.IssuedAtUtc is not { Kind: DateTimeKind.Utc }) return "missing issuedAtUtc";
        return null;
    }
}
