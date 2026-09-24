using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 D-15 (round B1, V-18 / R-9). Each method runs the matching <c>Test-C599B_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION publisher. The release clone is a real
/// git repository whose commit holds the policy; the authority, frozen chunk plan and ledger are
/// written by the production <c>release-authority.ps1</c> functions and the chunk evidence is real TRX.
/// Recipient state (the fake remote and the file-backed GitHub store) is read by a separate observer.
///
/// <para>The decisive behaviours: the policy blob re-read at the pinned SHA - never the summary or
/// report - names the eight required suites; a stale pinned hash or blob id, a missing suite, a failed,
/// skipped or deleted chunk under a passing sibling, and a legacy candidate with no authority each refuse
/// with zero remote writes.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateAuthorityContractTests
{
    [Test]
    public Task C599B_PinnedPolicy() => RunCaseAsync("C599B_PinnedPolicy", 10,
        "C599 V-18: a valid eight-suite pinned authority publishes",
        "C599 V-18: the recipient holds a published non-draft release",
        "C599 V-18: the manifest names all eight pinned suites at the tested sha",
        "C599 V-18: a shrunken summary is not a source of required suites",
        "C599 G-224: seven-suite report cannot publish eight-suite policy",
        "C599 G-225: wrong pinned hash blocks before any remote write",
        "C599 V-18: a stale pinned blob id blocks before any remote write",
        "C599 R-9: a legacy candidate with no authority refuses",
        "C599 R-9: the legacy refusal made zero remote writes",
        "C599 R-9: no authority was synthesized for the legacy candidate");

    [Test]
    public Task C599B_AllChunks() => RunCaseAsync("C599B_AllChunks", 6,
        "C599 V-18: the frozen plan chunks one class each and omits pinned exclusions",
        "C599 V-18: a complete multi-chunk ledger publishes",
        "C599 G-226: passing sibling cannot hide failed chunk",
        "C599 G-290: deleted or extra chunk blocks authority",
        "C599 G-292: required skip unknown missing and stale rows refuse",
        "C599 V-18: a failed client roster entry blocks before any remote write");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
