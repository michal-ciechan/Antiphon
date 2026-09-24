using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 D-15 full V-12 matrix (round B1; ordinary contract is
/// <see cref="ReleaseGateAuthorityContractTests"/>). Each method runs exactly one <c>Test-C599A_*</c>
/// case of <c>scripts/test-release-gate.ps1</c> as its own pwsh child under the harness's unchanged
/// 120-second limit, against the PRODUCTION <c>release-authority.ps1</c> guards and publisher. The
/// matrix is split so each child stays near 30 s on server2 (review bf928242 finding 3: Windows under
/// load ran the former multi-hundred-variant children past 120 s).
///
/// <para>One real-git B1 fixture per child. Every variant restores that valid control byte for byte,
/// changes exactly one field, row or handshake and must be refused with the NAMED reason token, so a
/// removed guard cannot hide behind another refusal. Gate variants run the publisher's own pre-write
/// composition; one publisher variant per guard also proves zero remote writes through the observer.</para>
///
/// <para>The decisive behaviours: the pinned blob - never the summary, private copy or working tree -
/// names the eight suites; every authority field refuses absent, null, mistyped, array-wrapped and
/// foreign; repository binds to the checkout origin and the publication destination and
/// coordinatorVersion to the supported coordinator; every frozen chunk and required expanded UID must
/// pass exactly once in its own chunk; exits, counts, evidence containment, identities, digests,
/// timestamps (never after the injected publisher clock), whole-run markers and credit flags are
/// checked independently; only a typed prepublication receipt from the bound release card, correlated
/// to this run, is accepted (PC-306), and only when the card read back through GET /api/cards/{id}
/// is at exactly the receipt's revision and its body carries this run's correlation (an unavailable
/// readback refuses); the clock and remote seams are admitted only in explicit test mode, which the
/// publisher refuses whenever a real remote would be reached; and <c>-WhatIf</c> writes no publication
/// authority and no tag reservation.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGatePublicationAuthorityTests
{
    [Test]
    public Task C599A_PinnedPolicy() => RunCaseAsync("C599A_PinnedPolicy", 4,
        "C599 V-12: the valid eight-suite control passes the publication gate",
        "C599 G-224: each of the eight pinned suites missing from an agreeing report refuses with zero remote writes",
        "C599 G-225: a wrong, absent, null or mistyped pinned hash blocks before any remote write",
        "C599 G-285: an edited private copy or working-tree policy cannot replace the pinned blob");

    [Test]
    public Task C599A_PinnedPolicySchema() => RunCaseAsync("C599A_PinnedPolicySchema", 6,
        "C599 G-286: re-pinning the candidate to a new commit with a valid blob is accepted (repin control)",
        "C599 G-286: an unknown schema, missing or empty rc profile and each absent suite in the pinned blob refuse",
        "C599 V-18: -WhatIf passes the gate but writes no publication authority and nothing remote",
        "C599 V-18: -WhatIf proposes the next tag without reserving it and leaves publications.json byte-identical",
        "C599 G-285: a changed working tree and moved HEAD cannot alter the frozen suite authority",
        "C599 V-18: the frozen publication authority digest is journalled and carried in the manifest");

    [Test]
    public Task C599A_PinnedPolicyFields() => RunCaseAsync("C599A_PinnedPolicyFields", 2,
        "C599 V-12: the valid authority control passes before the field matrix",
        "C599 V-12: the frozen schemaVersion, kind, repository, profile and coordinatorVersion fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public Task C599A_PinnedPolicyIdentityFields() => RunCaseAsync("C599A_PinnedPolicyIdentityFields", 2,
        "C599 V-12: the valid authority control passes before the field matrix",
        "C599 V-12: the frozen sha, candidateId, candidateRef, intentId and releaseCardId fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public Task C599A_PinnedPolicyBlobFields() => RunCaseAsync("C599A_PinnedPolicyBlobFields", 2,
        "C599 V-12: the valid authority control passes before the field matrix",
        "C599 V-12: the frozen policyPath, policyBlobId, policyRawSha256 and policyHash fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public Task C599A_PinnedPolicySuiteFields() => RunCaseAsync("C599A_PinnedPolicySuiteFields", 2,
        "C599 V-12: the valid authority control passes before the field matrix",
        "C599 V-12: the frozen requiredSuites, exclusions, scriptHashes and createdAt fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public Task C599A_PinnedPolicyBindings() => RunCaseAsync("C599A_PinnedPolicyBindings", 3,
        "C599 V-12: the valid authority control passes before the binding matrix",
        "C599 V-12: requiredSuites, exclusions and script digests refuse joined strings, unknown names and array-wrapped scalars",
        "C599 V-12: repository binds to the checkout origin and the publication destination, coordinatorVersion to the supported coordinator");

    [Test]
    public Task C599A_ExecutionLedger() => RunCaseAsync("C599A_ExecutionLedger", 5,
        "C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate",
        "C599 G-226: a passing sibling chunk never hides a failed chunk in any suite",
        "C599 G-287: outside, absolute and reparse-escaped evidence refuses before publication",
        "C599 G-288: each failed, absent, duplicated or mistyped required prerequisite blocks remote writes",
        "C599 G-289: a nonzero or non-numeric child exit or zero executions blocks despite a passing TRX");

    [Test]
    public Task C599A_ExecutionLedgerChunks() => RunCaseAsync("C599A_ExecutionLedgerChunks", 5,
        "C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate",
        "C599 G-290: every deleted, extra or duplicated frozen chunk blocks authority",
        "C599 G-291: a duplicated UID in a sibling chunk, the same chunk or the plan refuses",
        "C599 G-292: each required expanded UID removed from its chunk evidence refuses",
        "C599 G-292: skipped, unknown-outcome, unknown-UID, stale and missing evidence refuse");

    [Test]
    public Task C599A_ExecutionLedgerCounts() => RunCaseAsync("C599A_ExecutionLedgerCounts", 4,
        "C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate",
        "C599 G-293: a zero-required suite or empty declared roster cannot publish",
        "C599 G-294: inflated or mistyped ledger counts refuse against the recomputed TRX",
        "C599 G-295: a post-result exclusion edit cannot remove a failed UID");

    [Test]
    public Task C599A_ExecutionLedgerJoins() => RunCaseAsync("C599A_ExecutionLedgerJoins", 8,
        "C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate",
        "C599 G-296: foreign intent evidence on the plan, ledger, a chunk or a roster refuses",
        "C599 G-297: foreign candidate evidence on the plan, ledger, a chunk or a roster refuses",
        "C599 G-298: foreign full SHA evidence on the plan, ledger, a chunk or a roster refuses",
        "C599 G-299: foreign native RunId evidence on the plan, ledger, a chunk or a roster refuses",
        "C599 G-300: a modified script, authority, plan or discovery digest blocks publication",
        "C599 G-301: out-of-order or stale evidence timestamps refuse",
        "C599 G-301: evidence dated after the injected publisher clock refuses");

    [Test]
    public Task C599A_ExecutionLedgerMarkers() => RunCaseAsync("C599A_ExecutionLedgerMarkers", 10,
        "C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate",
        "C599 G-302: a missing, false, string or numeric teardown cannot publish",
        "C599 G-303: a seam-driven ledger or run creates no release",
        "C599 G-304: a NoReport ledger or run creates no release",
        "C599 G-305: subset and diagnostic ledgers or runs create no release",
        "C599 G-387: a string or non-true coverageComplete flag cannot publish",
        "C599 G-388: a string or non-true testsPassed flag cannot publish",
        "C599 G-389: a string or non-true reportDelivered flag cannot publish",
        "C599 V-12: client and script evidence validate against their own declared rosters",
        "C599 V-12: the restored control ledger publishes with suite counts recomputed from the evidence");

    [Test]
    public Task C599A_ExecutionLedgerReceipt() => RunCaseAsync("C599A_ExecutionLedgerReceipt", 5,
        "C599 V-12: the valid control ledger with a correlated prepublication receipt passes",
        "C599 G-306: a final or foreign receipt kind where the prepublication receipt is required refuses",
        "C599 V-12: a missing, boolean, string or array-wrapped receipt cannot publish and the legacy reportAccepted flag confers nothing",
        "C599 V-12: a receipt from any recipient but the release card bound to the authority refuses",
        "C599 V-12: a receipt not correlated to this intent, candidate, SHA and native run refuses");

    [Test]
    public Task C599A_ReceiptReadback() => RunCaseAsync("C599A_ReceiptReadback", 6,
        "C599 V-12: the valid control reads the bound release card back once through GET /api/cards/{id}",
        "C599 V-12: an unavailable, failed, unknown or non-object release card readback refuses (fail closed)",
        "C599 V-12: a receipt revision other than the release card current revision refuses",
        "C599 V-12: a release card body without exactly this run correlation refuses",
        "C599 V-12: a readback of any card but the bound release card refuses",
        "C599 V-12: the restored control publishes after only read-only GETs of the bound release card");

    [Test]
    public Task C599A_PublisherTestMode() => RunCaseAsync("C599A_PublisherTestMode", 4,
        "C599 V-18: in test mode the fake remote and the injected clock publish the valid control",
        "C599 V-18: outside test mode a seams file is refused before it is loaded, the clock is never read and nothing remote is written",
        "C599 V-18: the release-gate clock seam is refused outside test mode and honoured inside it",
        "C599 V-18: test mode refuses a real remote: no seams file, or one without the Git or GitHub adapter");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
