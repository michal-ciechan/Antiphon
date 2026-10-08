using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S1: the evidence store and service without a process or a transport.
/// Design: docs/superpowers/plans/2026-10-08-card-1153-test-design.md (V-1..V-4, V-23).
/// Fixture: a temp state root per test, one <c>RunnerAbsenceEvidenceService</c> per runtime
/// epoch, the A-10 inspection seam for runtime entries, artifacts, adoption and the fault latch.
/// </summary>
[Category("Unit")]
public class RunnerAbsenceEvidenceTests
{
    /// <summary>
    /// V-1. Decisive assertions: the record file exists with state Prepared, the exact session
    /// id, normalized generation, store and epoch; the seam saw zero creation calls; no runtime
    /// entry or transcript exists; an identical second prepare returns the same record bytes.
    /// </summary>
    [Test]
    public Task C1153_Prepare_records_only_a_fresh_identity() =>
        Card1153Pending.Skip("S1", nameof(C1153_Prepare_records_only_a_fresh_identity));

    /// <summary>
    /// V-2. Each case seeds one artifact or record for the id and requires a typed refusal with
    /// no new or overwritten record. Malformed sidecar still refuses (a tolerant loader is not
    /// an absence probe).
    /// </summary>
    [Test]
    [Arguments("live")]
    [Arguments("exited")]
    [Arguments("attempted-record")]
    [Arguments("closed-unused-record")]
    [Arguments("transcript-sidecar")]
    [Arguments("herdr-sidecar")]
    [Arguments("manifest")]
    [Arguments("custody-or-watermark")]
    [Arguments("malformed-sidecar")]
    public Task C1153_Prepare_refuses_existing_evidence(string condition) =>
        Card1153Pending.Skip("S1", nameof(C1153_Prepare_refuses_existing_evidence));

    /// <summary>
    /// V-3. A second service instance (new epoch) over the same root, or the same epoch with a
    /// changed store: certify refuses with the typed prior-epoch/store reason, re-prepare refuses,
    /// and the original record bytes are unchanged afterwards.
    /// </summary>
    [Test]
    [Arguments("prepared-prior-epoch")]
    [Arguments("closed-prior-epoch")]
    [Arguments("changed-store")]
    public Task C1153_Restart_or_store_change_never_renews_proof(string condition) =>
        Card1153Pending.Skip("S1", nameof(C1153_Restart_or_store_change_never_renews_proof));

    /// <summary>
    /// V-4. Strict reader: never a positive certificate; corrupt, unknown-schema and denied-read
    /// surface as distinct unknown reasons, not as absence; a lost root after initialization is
    /// unknown, not a blank store.
    /// </summary>
    [Test]
    [Arguments("unprepared")]
    [Arguments("corrupt-record")]
    [Arguments("unknown-schema")]
    [Arguments("denied-read")]
    [Arguments("lost-root")]
    public Task C1153_Unknown_or_unreadable_state_is_not_absence(string condition) =>
        Card1153Pending.Skip("S1", nameof(C1153_Unknown_or_unreadable_state_is_not_absence));

    /// <summary>
    /// V-23. One-condition flip table on the certify service (the S1 lesson). Each case first
    /// admits an independently seeded pristine Prepared identity, then changes exactly one
    /// condition and requires refusal with no state transition. The companion
    /// <c>closed-same-epoch-reissue</c> admits a fresh certificate after the exclusions re-run.
    /// </summary>
    [Test]
    [Arguments("no-record")]
    [Arguments("prepared-prior-epoch")]
    [Arguments("closed-prior-epoch")]
    [Arguments("attempted")]
    [Arguments("wrong-generation")]
    [Arguments("wrong-store")]
    [Arguments("adoption-incomplete")]
    [Arguments("storage-fault-latched")]
    [Arguments("runtime-entry-live")]
    [Arguments("runtime-entry-exited")]
    [Arguments("manifest-present")]
    [Arguments("transcript-sidecar-present")]
    [Arguments("herdr-sidecar-present")]
    [Arguments("custody-reservation-present")]
    [Arguments("watermark-present")]
    [Arguments("nonce-missing")]
    [Arguments("nonce-short")]
    [Arguments("version-0")]
    [Arguments("version-2")]
    [Arguments("empty-session-id")]
    [Arguments("corrupt-record")]
    [Arguments("closed-same-epoch-reissue")]
    public Task C1153_Certify_requires_every_fact(string condition) =>
        Card1153Pending.Skip("S1", nameof(C1153_Certify_requires_every_fact));
}
