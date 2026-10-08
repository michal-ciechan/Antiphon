using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1153 S4: the dispatcher over the production runner clients. Design: V-14..V-20, V-24.
/// Fixture: the isolated PostgreSQL schema, <c>BridgeQueueHarness</c>/<c>OpenSweep</c> from the
/// S1 partial, the production <c>SessionRunnerHttpClient</c> against the production
/// <c>AbsenceEvidenceRoutes</c> and session/transcript routes hosted on a random-port Kestrel
/// with the real evidence service over a temp root and a synthetic key; for the remote case the
/// real <c>PhoneHomeRunnerClient</c> over <c>PhoneHomeTestHost</c> with a real
/// <c>PhoneHomeCommandDispatcher</c> as the scripted peer. Never a fake empty transcript.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// V-14. Refused enqueue after a successful prepare, real transcript 404, due sweeps.
    /// Decisive: Blocked, CompletedAt null, FailureReason DispatchLaunchAbsentReason, one Blocked
    /// event, brief/spill bytes identical, queued parent note with SourceTaskId and stable reason,
    /// Starts/Kills/Releases/Inputs 0, stopper empty, exactly one certify request observed on the
    /// host, record state ClosedUnused on disk.
    /// </summary>
    [Test]
    [Arguments("local-http")]
    [Arguments("remote-phone-home")]
    public Task C1153_Real_client_certificate_holds_original_input(string transport) =>
        Card1153Pending.Skip("S4", nameof(C1153_Real_client_certificate_holds_original_input));

    /// <summary>
    /// V-15. Independently known absent inventory; the evidence route answers the named shape.
    /// Decisive: Failed, CompletedAt set, zero Blocked events, bytes retained, Quiet.
    /// </summary>
    [Test]
    [Arguments("plain404")]
    [Arguments("old-runner")]
    [Arguments("evidence-unreachable")]
    [Arguments("timeout")]
    [Arguments("stale-nonce")]
    [Arguments("mismatched-generation")]
    [Arguments("incomplete")]
    [Arguments("empty-transcript")]
    public Task C1153_Real_client_bad_evidence_keeps_failure(string shape) =>
        Card1153Pending.Skip("S4", nameof(C1153_Real_client_bad_evidence_keeps_failure));

    /// <summary>
    /// V-16. The condition changes between the first evidence read and StageBlocked (fault
    /// interceptor on the FOR UPDATE). Decisive: no Blocked row or event committed; task
    /// unchanged; the existing safety withhold where applicable.
    /// </summary>
    [Test]
    [Arguments("changed-generation")]
    [Arguments("store-or-epoch-change")]
    [Arguments("expired-proof")]
    public Task C1153_Final_certificate_is_revalidated_under_lock(string condition) =>
        Card1153Pending.Skip("S4", nameof(C1153_Final_certificate_is_revalidated_under_lock));

    /// <summary>
    /// V-17. Decisive: zero prepare/certify requests on the host, task and session unchanged,
    /// no fail/hold/kill/start. unavailable-owning-inventory pairs an UnavailableDirectory with
    /// a misleading empty local list.
    /// </summary>
    [Test]
    [Arguments("working-task")]
    [Arguments("working-transcript")]
    [Arguments("unavailable-owning-inventory")]
    public Task C1153_Working_or_unknown_inventory_withholds(string shape) =>
        Card1153Pending.Skip("S4", nameof(C1153_Working_or_unknown_inventory_withholds));

    /// <summary>
    /// V-18. cold-fresh: one prepare for the new id after the claim commit (land boundary
    /// dispatch-warning-claim-committed) and before the sink's first Enqueue. warm-reuse,
    /// recovery, resume: zero prepare requests. prepare-faulted: the route throws/503, the launch
    /// still enqueues and the task stays Dispatched. remote-old-runner: capabilities without the
    /// token, zero PrepareAbsenceEvidence frames.
    /// </summary>
    [Test]
    [Arguments("cold-fresh")]
    [Arguments("warm-reuse")]
    [Arguments("recovery")]
    [Arguments("resume")]
    [Arguments("prepare-faulted")]
    [Arguments("remote-old-runner")]
    public Task C1153_Only_new_cold_dispatch_prepares_evidence(string path) =>
        Card1153Pending.Skip("S4", nameof(C1153_Only_new_cold_dispatch_prepares_evidence));

    /// <summary>
    /// V-19. Decisive: zero certify requests on the host, record still Prepared on disk, and the
    /// previous whitelist outcome (Failed) for attempted-delivery, extra-related-row and
    /// native-attempt.
    /// </summary>
    [Test]
    [Arguments("attempted-delivery")]
    [Arguments("extra-related-row")]
    [Arguments("native-attempt")]
    public Task C1153_Nonpristine_brief_never_closes_identity(string condition) =>
        Card1153Pending.Skip("S4", nameof(C1153_Nonpristine_brief_never_closes_identity));

    /// <summary>
    /// V-20. Certificate passes, BlockedSaveFault fails the hold save, a later unrelated save in
    /// the same context persists nothing of the staged task/event/note; the next due tick holds.
    /// </summary>
    [Test]
    public Task C1153_Certificate_failure_does_not_leak_a_hold_on_later_save() =>
        Card1153Pending.Skip("S4", nameof(C1153_Certificate_failure_does_not_leak_a_hold_on_later_save));

    /// <summary>
    /// V-24. FullCommandCounter over the due hold with the real HTTP client. Code measures and
    /// pins the exact totals; the pinned value must be at most 40 without a parent note and at
    /// most 48 with one, and the roster is printed for Review.
    /// </summary>
    [Test]
    [Arguments("no-parent", 40)]
    [Arguments("parent", 48)]
    public Task C1153_Due_hold_statement_ceiling(string shape, int ceiling) =>
        Card1153Pending.Skip("S4", nameof(C1153_Due_hold_statement_ceiling));
}
