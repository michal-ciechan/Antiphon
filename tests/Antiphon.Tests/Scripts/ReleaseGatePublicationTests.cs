using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-6. Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION candidate coordinator and the
/// production publisher, over a file-backed fake remote and a stateful GitHub fixture. A separate
/// observer read of that remote - never the script's own return value - is what proves recipient
/// state.
///
/// <para>Every remote boundary is cut two ways: fail before the write, and commit the write then
/// lose the response. After each cut the run restarts from disk and must land on exactly one
/// candidate, one tag reservation and one published release - never a second identity.</para>
///
/// <para>The decisive behaviours: master is pinned once and a resume never re-pins even after
/// master moves; a remote candidate at a different SHA is a hard refusal and nothing is moved; the
/// publication gate refuses every incomplete, diagnostic, NoReport, seam-driven, moved-candidate
/// and missing-required-suite case; a tag with no published release stays pending; and the
/// manifest carries only allowlisted fields bound to the tested SHA.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGatePublicationTests
{
    [Test]
    public Task C599_CandidateIdentity() => RunCaseAsync("C599_CandidateIdentity", 13,
        "C599 CandidateIdentity control: a candidate is cut and pushed",
        "C599 CandidateIdentity an observer read finds the candidate at the pinned sha",
        "C599 CandidateIdentity a resume keeps the original pin even after master moved",
        "C599 CandidateIdentity a resume performs no fetch",
        "C599 CandidateIdentity a remote candidate at another sha refuses",
        "C599 CandidateIdentity the conflicting remote ref was not moved",
        "C599 CandidateIdentity refuses ref release/current",
        "C599 CandidateIdentity no push used force or a plus refspec");

    [Test]
    public Task C599_CutRecovery() => RunCaseAsync("C599_CutRecovery", 12,
        "C599 CutRecovery before-push reports a failed push",
        "C599 CutRecovery before-push left the remote untouched",
        "C599 CutRecovery a lost response is reconciled from the remote",
        "C599 CutRecovery the remote holds exactly the pinned sha",
        "C599 CutRecovery before-push restart resumes the same sha",
        "C599 CutRecovery a fetch failure leaves the remote untouched");

    [Test]
    public Task C599_PublicationGate() => RunCaseAsync("C599_PublicationGate", 17,
        "C599 PublicationGate control: a complete rc green publishes",
        "C599 PublicationGate refuses a coverage false",
        "C599 PublicationGate refuses a wrong trigger",
        "C599 PublicationGate refuses a NoReport run",
        "C599 PublicationGate refuses a diagnostic run",
        "C599 PublicationGate refuses a seam-driven run",
        "C599 PublicationGate a moved remote candidate refuses",
        "C599 PublicationGate an unresolved remote candidate refuses",
        "C599 PublicationGate a missing required e2e suite refuses",
        "C599 PublicationGate a policy hash mismatch refuses",
        "C599 PublicationGate a refused publication created no release");

    [Test]
    public Task C599_TagRecovery() => RunCaseAsync("C599_TagRecovery", 12,
        "C599 TagRecovery before-push refuses",
        "C599 TagRecovery before-push created no release",
        "C599 TagRecovery a lost tag-push response is reconciled from the remote peel",
        "C599 TagRecovery before-push restart reuses the same tag, not a new sequence",
        "C599 TagRecovery before-push allocated exactly one reservation");

    [Test]
    public Task C599_DraftRecovery() => RunCaseAsync("C599_DraftRecovery", 10,
        "C599 DraftRecovery before-write refuses",
        "C599 DraftRecovery before-write stored no release",
        "C599 DraftRecovery a lost create response is reconciled by readback",
        "C599 DraftRecovery before-write the recipient sees a published, non-draft release",
        "C599 DraftRecovery before-write exactly one release exists");

    [Test]
    public Task C599_AssetRecovery() => RunCaseAsync("C599_AssetRecovery", 29,
        "C599 AssetRecovery release-manifest.json before-write does not publish",
        "C599 AssetRecovery release-manifest.json before-write leaves a pending draft, not a release",
        "C599 AssetRecovery release-manifest.json before-write both assets are present after recovery",
        "C599 AssetRecovery release-manifest.json before-write the manifest is bound to the tested sha",
        "C599 AssetRecovery notes name the tag",
        "C599 AssetRecovery the first release says there is no earlier release",
        "C599 AssetRecovery notes carry no credential-shaped text");

    [Test]
    public Task C599_PublishRecovery() => RunCaseAsync("C599_PublishRecovery", 10,
        "C599 PublishRecovery before-write stays pending",
        "C599 PublishRecovery before-write the release is still a draft",
        "C599 PublishRecovery a lost publish response is reconciled by readback",
        "C599 PublishRecovery before-write recovery reuses the same tag",
        "C599 PublishRecovery before-write exactly one published reservation with a release id");

    [Test]
    public Task C599_ManifestAllowlist() => RunCaseAsync("C599_ManifestAllowlist", 4,
        "C599 ManifestAllowlist a built manifest carries only allowlisted keys",
        "C599 ManifestAllowlist a hostname field is refused",
        "C599 ManifestAllowlist a transcript field is detected",
        "C599 ManifestAllowlist the manifest digest is a sha256");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
