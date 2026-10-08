using System.Collections.Concurrent;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1121. Process-local proofs that one exact receipt scan already ran to exhaustion without a
/// match against one exact committed session-state revision. A proof only lets the reconciler omit
/// that one SELECT; it never confirms, never moves the delivery floor and never delays a pass.
/// Reuse needs every positive condition below; anything missing or unknown keeps today's scan.
/// A restart starts empty. Every fault inside the cache, including its clock and metrics, refuses
/// reuse rather than escaping.
/// </summary>
public sealed class LandReceiptScanCache(TimeProvider clock)
{
    /// <summary>D-6 with the CARD-1121 Q2 fallback: 256 proofs of at most 16,384 UTF-16 chars (8 MiB of text).</summary>
    public const int MaxProofs = 256;
    public const int MaxExpectedTextChars = 16_384;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, (Proof Proof, LinkedListNode<Guid> Order)> _proofs = [];
    private readonly LinkedList<Guid> _order = new();
    private readonly ConcurrentDictionary<string, long> _refusals = new(StringComparer.Ordinal);
    private long _hits, _misses, _publishes;

    // Test seam: a throwing probe proves telemetry cannot change an outcome. Null changes nothing.
    internal Action<string>? MetricsProbe;

    /// <summary>The exact attempt, payload and destination a negative scan examined (D-3, D-5, A-1, A-3).</summary>
    internal sealed record Context(
        Guid NoteId,
        Guid QueueMessageId,
        Guid? ParentSessionId,
        Guid QueueDestination,
        Guid? SourceLandNotificationId,
        bool IsLegacy,
        LandNotificationKind Kind,
        LandNotificationState NoteState,
        QueuedMessageStatus QueueStatus,
        DeliveryVerdict? Verdict,
        int DeliveryAttempts,
        long BaselineSequence,
        DateTime? LastDeliveryStartedAt,
        DateTime? LastDeliveryGeneration,
        SessionStatus DestinationStatus,
        string ExpectedText);

    /// <summary>
    /// The runtime-owned committed-state read taken after catch-up (D-4 as amended by A-2), or the
    /// reason it is unknown. Only <see cref="StateStamp.TryCreate"/> interprets it.
    /// </summary>
    internal sealed record Observation(SessionStateSnapshot? Snapshot, string? UnknownReason)
    {
        public static Observation Known(SessionStateSnapshot snapshot) => new(snapshot, null);
        public static Observation Unknown(string reason) => new(null, reason);
    }

    /// <summary>D-2: the opaque committed-revision identity a proof is bound to.</summary>
    internal sealed record StateStamp(
        Guid SessionId,
        Guid ServerEpoch,
        long Revision,
        long ResetEpoch,
        DateTime AcceptedGeneration,
        long Count,
        long LastSequence)
    {
        /// <summary>
        /// W-6: a Ready snapshot with a server epoch, a positive revision, sane counts and an accepted
        /// generation equal to the destination's <c>StartedAt</c>. Anything else is no stamp.
        /// </summary>
        internal static bool TryCreate(Observation observation, DateTime? destinationStartedAt,
            out StateStamp? stamp, out string refusal)
        {
            stamp = null;
            if (observation.Snapshot is not { } s)
                return Refuse("state:Observation:" + (observation.UnknownReason ?? "unknown"), out refusal);
            if (s.Readiness != SessionStateReadiness.Ready) return Refuse("state:Readiness", out refusal);
            if (s.ServerEpoch == Guid.Empty) return Refuse("state:ServerEpoch", out refusal);
            if (s.Revision <= 0) return Refuse("state:Revision", out refusal);
            if (s.ResetEpoch < 0) return Refuse("state:ResetEpoch", out refusal);
            if (s.AcceptedGeneration is not DateTime accepted) return Refuse("state:AcceptedGeneration", out refusal);
            if (!SessionGeneration.Equal(accepted, destinationStartedAt))
                return Refuse("state:AcceptedGeneration", out refusal);
            if (s.Count < 0) return Refuse("state:Count", out refusal);
            if (s.LastSequence < s.Count) return Refuse("state:LastSequence", out refusal);
            stamp = new StateStamp(s.SessionId, s.ServerEpoch, s.Revision, s.ResetEpoch, accepted, s.Count, s.LastSequence);
            refusal = "";
            return true;
        }
    }

    private sealed record Proof(Context Context, StateStamp Stamp, long ScanCompletedAt);

    /// <summary>
    /// W-1..W-4: binds the scan context only for an enumerated note state and kind, a terminal
    /// destination (A-1) and an ordinary keyed-row body that is exactly the expected text. Every
    /// other shape keeps today's scan; nothing here inspects a diagnostic or grants eligibility.
    /// </summary>
    internal static bool TryBuildContext(AgentTaskLandNotification note, SessionQueuedMessage row,
        SessionStatus destinationStatus, string expectedText, out Context? context, out string refusal)
    {
        context = null;
        if (!AdmitsState(note.State)) return Refuse("eligibility:NoteState", out refusal);
        if (!AdmitsKind(note.Kind)) return Refuse("eligibility:Kind", out refusal);
        if (note.CompletionSnapshotJson is not null) return Refuse("eligibility:CompletionSnapshotJson", out refusal);
        if (note.CompletionDeliveryJson is not null) return Refuse("eligibility:CompletionDeliveryJson", out refusal);
        if (destinationStatus is not (SessionStatus.Stopped or SessionStatus.Failed))
            return Refuse("eligibility:DestinationStatus", out refusal);
        if (!AdmitsQueueStatus(row.Status)) return Refuse("eligibility:QueueStatus", out refusal);
        if (row.DeliveryVerdict is { } verdict && !AdmitsVerdict(verdict)) return Refuse("eligibility:DeliveryVerdict", out refusal);
        if (row.DeliveryAttempts <= 0) return Refuse("eligibility:DeliveryAttempts", out refusal);
        if (row.LastDeliveryBaselineSequence is not long baseline || baseline < 0)
            return Refuse("eligibility:LastDeliveryBaselineSequence", out refusal);
        if (row.RemoteSpillBody is not null) return Refuse("eligibility:RemoteSpillBody", out refusal);
        if (!string.Equals(row.Body, note.Body, StringComparison.Ordinal)) return Refuse("eligibility:QueueBody", out refusal);
        if (row.Body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal))
            return Refuse("eligibility:PointerHeadline", out refusal);
        if (!string.Equals(expectedText, note.Body, StringComparison.Ordinal)) return Refuse("eligibility:ExpectedText", out refusal);
        if (expectedText.Length > MaxExpectedTextChars) return Refuse("eligibility:MaxExpectedTextChars", out refusal);
        context = new Context(note.Id, row.Id, note.ParentSessionId, row.AgentSessionId, row.SourceLandNotificationId,
            note.IsLegacy, note.Kind, note.State, row.Status, row.DeliveryVerdict, row.DeliveryAttempts, baseline,
            row.LastDeliveryStartedAt, row.LastDeliveryGeneration, destinationStatus, expectedText);
        refusal = "";
        return true;
    }

    // Enumerated, not Enum.IsDefined: a future value stays on today's scan until it is reviewed here.
    private static bool AdmitsState(LandNotificationState state) => state is LandNotificationState.Queued
        or LandNotificationState.RetryPending or LandNotificationState.AwaitingReceipt
        or LandNotificationState.DestinationUnavailable or LandNotificationState.Canceled;

    private static bool AdmitsKind(LandNotificationKind kind) => kind is LandNotificationKind.Held
        or LandNotificationKind.Aged or LandNotificationKind.Conflict or LandNotificationKind.Outcome
        or LandNotificationKind.DispatchBase or LandNotificationKind.DeliveryFailure or LandNotificationKind.TaskCompletion;

    private static bool AdmitsQueueStatus(QueuedMessageStatus status) => status is QueuedMessageStatus.Pending
        or QueuedMessageStatus.Sent or QueuedMessageStatus.Canceled;

    private static bool AdmitsVerdict(DeliveryVerdict verdict) => verdict is DeliveryVerdict.Delivered
        or DeliveryVerdict.NoComposerEvidence or DeliveryVerdict.NoSubmitOutput or DeliveryVerdict.NoTranscriptRecord
        or DeliveryVerdict.Truncated or DeliveryVerdict.ForbiddenBody or DeliveryVerdict.LocalCommandNotAccepted
        or DeliveryVerdict.BackendUnreachable or DeliveryVerdict.LateConfirmed or DeliveryVerdict.ModalBlocked
        or DeliveryVerdict.SpillBodyMissing;

    /// <summary>The cache clock's monotonic timestamp, or null when the clock faults.</summary>
    internal long? TryGetTimestamp()
    {
        try { return clock.GetTimestamp(); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// W-8: retains a proof only for a completed no-match enumeration whose before and after stamps
    /// are equal and whose final save succeeded. Returns whether a proof was retained.
    /// </summary>
    internal bool Publish(Context context, StateStamp? before, StateStamp? after, bool matchObserved,
        bool saveCompleted, long? scanCompletedAt)
    {
        try
        {
            string? refusal = null;
            if (matchObserved) refusal = "certificate:MatchObserved";
            else if (!saveCompleted) refusal = "certificate:SaveCompleted";
            else if (before is null || after is null || before != after) refusal = "certificate:BeforeAfter";
            else if (scanCompletedAt is not long completedAt) refusal = "certificate:Timestamp";
            else if (context.ExpectedText.Length > MaxExpectedTextChars) refusal = "eligibility:MaxExpectedTextChars";
            if (refusal is not null)
            {
                CountRefusal(refusal);
                return false;
            }

            var proof = new Proof(context, after!, scanCompletedAt!.Value);
            lock (_gate)
            {
                // The whole proof is replaced: a superseded publication can only cause a miss,
                // never pair this context with another publication's stamp.
                if (_proofs.Remove(context.NoteId, out var previous))
                    _order.Remove(previous.Order);
                while (_proofs.Count >= MaxProofs && _order.First is { } oldest)
                {
                    _proofs.Remove(oldest.Value);
                    _order.RemoveFirst();
                }
                _proofs[context.NoteId] = (proof, _order.AddLast(context.NoteId));
            }
            Count(ref _publishes, "publish");
            return true;
        }
        catch (Exception)
        {
            CountRefusal("cache-fault");
            return false;
        }
    }

    /// <summary>
    /// W-7: true only for an unexpired proof whose context and stamp equal the current ones member
    /// for member. Null equals only null. A hit never extends the proof's lifetime.
    /// </summary>
    internal bool TryReuse(Context context, StateStamp stamp, out string refusal)
    {
        try
        {
            Proof? proof;
            lock (_gate) proof = _proofs.TryGetValue(context.NoteId, out var held) ? held.Proof : null;
            refusal = proof is null ? "no-proof" : ContextRefusal(proof.Context, context) ?? StampRefusal(proof.Stamp, stamp) ?? "";
            if (refusal.Length == 0)
            {
                var elapsed = clock.GetElapsedTime(proof!.ScanCompletedAt, clock.GetTimestamp());
                if (elapsed < TimeSpan.Zero) refusal = "certificate:Elapsed";
                else if (elapsed >= Lifetime) refusal = "certificate:Lifetime";
            }
        }
        catch (Exception)
        {
            refusal = "cache-fault";
        }

        if (refusal.Length == 0)
        {
            Count(ref _hits, "hit");
            return true;
        }
        Count(ref _misses, "miss");
        CountRefusal(refusal);
        return false;
    }

    /// <summary>D-6: a confirmed or terminal note drops its proof.</summary>
    internal void Invalidate(Guid noteId)
    {
        try
        {
            lock (_gate)
                if (_proofs.Remove(noteId, out var removed))
                    _order.Remove(removed.Order);
        }
        catch (Exception)
        {
            CountRefusal("cache-fault");
        }
    }

    /// <summary>Records a policy refusal the caller observed before reaching the cache.</summary>
    internal void RecordRefusal(string reason) => CountRefusal(reason);

    internal int ProofCount { get { lock (_gate) return _proofs.Count; } }

    internal sealed record Metrics(int Proofs, long Hits, long Misses, long Publishes, IReadOnlyDictionary<string, long> Refusals);

    internal Metrics GetMetrics() => new(ProofCount, Interlocked.Read(ref _hits), Interlocked.Read(ref _misses),
        Interlocked.Read(ref _publishes), new Dictionary<string, long>(_refusals, StringComparer.Ordinal));

    // NoteId is the proof's key, so a different note finds no proof at all.
    private static string? ContextRefusal(Context proof, Context current)
    {
        if (proof.QueueMessageId != current.QueueMessageId) return "context:QueueMessageId";
        if (proof.ParentSessionId != current.ParentSessionId) return "context:ParentSessionId";
        if (proof.QueueDestination != current.QueueDestination) return "context:QueueDestination";
        if (proof.SourceLandNotificationId != current.SourceLandNotificationId) return "context:SourceLandNotificationId";
        if (proof.IsLegacy != current.IsLegacy) return "context:IsLegacy";
        if (proof.Kind != current.Kind) return "context:Kind";
        if (proof.NoteState != current.NoteState) return "context:NoteState";
        if (proof.QueueStatus != current.QueueStatus) return "context:QueueStatus";
        if (proof.Verdict != current.Verdict) return "context:Verdict";
        if (proof.DeliveryAttempts != current.DeliveryAttempts) return "context:DeliveryAttempts";
        if (proof.BaselineSequence != current.BaselineSequence) return "context:BaselineSequence";
        if (proof.LastDeliveryStartedAt != current.LastDeliveryStartedAt) return "context:LastDeliveryStartedAt";
        if (proof.LastDeliveryGeneration != current.LastDeliveryGeneration) return "context:LastDeliveryGeneration";
        if (proof.DestinationStatus != current.DestinationStatus) return "context:DestinationStatus";
        if (!string.Equals(proof.ExpectedText, current.ExpectedText, StringComparison.Ordinal)) return "context:ExpectedText";
        return null;
    }

    private static string? StampRefusal(StateStamp proof, StateStamp current)
    {
        if (proof.SessionId != current.SessionId) return "stamp:SessionId";
        if (proof.ServerEpoch != current.ServerEpoch) return "stamp:ServerEpoch";
        if (proof.Revision != current.Revision) return "stamp:Revision";
        if (proof.ResetEpoch != current.ResetEpoch) return "stamp:ResetEpoch";
        if (proof.AcceptedGeneration != current.AcceptedGeneration) return "stamp:AcceptedGeneration";
        if (proof.Count != current.Count) return "stamp:Count";
        if (proof.LastSequence != current.LastSequence) return "stamp:LastSequence";
        return null;
    }

    private static bool Refuse(string reason, out string refusal)
    {
        refusal = reason;
        return false;
    }

    // Best effort: a telemetry fault is swallowed here so it can never turn a decision around.
    private void Count(ref long counter, string name)
    {
        try
        {
            MetricsProbe?.Invoke(name);
            Interlocked.Increment(ref counter);
        }
        catch (Exception) { }
    }

    private void CountRefusal(string reason)
    {
        try
        {
            MetricsProbe?.Invoke(reason);
            _refusals.AddOrUpdate(reason, 1, (_, n) => n + 1);
        }
        catch (Exception) { }
    }
}
