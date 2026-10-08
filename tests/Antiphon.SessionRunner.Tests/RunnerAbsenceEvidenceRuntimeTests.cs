using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S2: the runtime's creation entry points against the evidence service, without a
/// real process. Design: V-5..V-8. Fixture: <c>SessionRunnerRuntime</c> over a temp root with a
/// test-only creation seam that records the order of the Attempted write against the first
/// provider effect (pty <c>LaunchDetachedAsync</c>, herdr <c>ConnectAndValidateAsync</c>,
/// adoption <c>TryAdd</c>), FakeTimeProvider, and barriers for the race.
/// </summary>
[Category("Integration")]
[NotInParallel("SessionLiveness")]
public class RunnerAbsenceEvidenceRuntimeTests
{
    /// <summary>
    /// V-5. The seam observes a durable Attempted record before its first call on every path;
    /// a seam throw leaves the record Attempted after runtime removal and release.
    /// </summary>
    [Test]
    [Arguments("start")]
    [Arguments("attach")]
    [Arguments("adoption")]
    public Task C1153_Creation_consumes_proof_before_effects(string path) =>
        Card1153Pending.Skip("S2", nameof(C1153_Creation_consumes_proof_before_effects));

    /// <summary>
    /// V-6. A failing Attempted write latches evidence unavailable before the seam runs; a
    /// later certify refuses with the latched reason; the Prepared record is not usable from
    /// memory; a healthy legacy launch on another id still completes.
    /// </summary>
    [Test]
    public Task C1153_Store_failure_disables_proof_without_stopping_work() =>
        Card1153Pending.Skip("S2", nameof(C1153_Store_failure_disables_proof_without_stopping_work));

    /// <summary>
    /// V-7. Barrier-ordered: launch-first yields a typed certify refusal; certify-first yields a
    /// typed launch refusal with zero seam calls; never two successes.
    /// </summary>
    [Test]
    [Arguments("launch-first")]
    [Arguments("certify-first")]
    public Task C1153_Certificate_and_launch_race_is_serialized(string order) =>
        Card1153Pending.Skip("S2", nameof(C1153_Certificate_and_launch_race_is_serialized));

    /// <summary>
    /// V-8. After ClosedUnused every entry point refuses with the closed-identity type and the
    /// seam count stays zero; a repeat certificate with a fresh nonce still succeeds.
    /// </summary>
    [Test]
    [Arguments("same-generation-start")]
    [Arguments("newer-generation-start")]
    [Arguments("attach")]
    [Arguments("custody-bound-start")]
    public Task C1153_Closed_identity_refuses_delayed_creation(string entry) =>
        Card1153Pending.Skip("S2", nameof(C1153_Closed_identity_refuses_delayed_creation));
}
