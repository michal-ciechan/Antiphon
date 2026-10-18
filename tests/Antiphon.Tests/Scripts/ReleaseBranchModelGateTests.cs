using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1311 S2, S3, and S4. <c>Test-C1311_LongLivedRcRef</c> calls the production
/// ref parser, credit kind, and candidate journal. <c>Test-C1311_PreReleaseTag</c>
/// calls tag parsing, the long-lived proposal, publish, and promote-release.
/// <c>Test-C1311_MovingTipAuthority</c> pins one long-lived ref at two SHAs.
/// A legacy stamp ref still earns <c>rc-release</c> and still publishes a final tag.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseBranchModelGateTests
{
    [Test]
    public Task C1311_LongLivedRcRef() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1311", "C1311_LongLivedRcRef", 10,
            "C1311 LongLivedRcRef rc/2026-10-15 is accepted",
            "C1311 LongLivedRcRef refs/heads/rc/2026-10-15 normalizes to rc/2026-10-15",
            "C1311 LongLivedRcRef candidate id is rc-2026-10-15-bbbbbbbbbbbb",
            "C1311 LongLivedRcRef rc/2026-13-01 refuses ref-date",
            "C1311 LongLivedRcRef rc/20261015 refuses ref-shape",
            "C1311 LongLivedRcRef legacy stamp release/rc-20260923T083000Z is accepted",
            "C1311 LongLivedRcRef credit rc/rc/rc/2026-10-15 earns rc-release",
            "C1311 LongLivedRcRef credit rc/rc/release/rc-20260923T083000Z earns rc-release",
            "C1311 LongLivedRcRef omitted Ref refuses rc-ref-required",
            "C1311 LongLivedRcRef an existing rc/2026-10-15 tip is journalled and not pushed");

    [Test]
    public Task C1311_PreReleaseTag() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1311", "C1311_PreReleaseTag", 8,
            "C1311 PreReleaseTag v2026.10.15.2-rc.1 is accepted",
            "C1311 PreReleaseTag v2026.10.15.2 is accepted",
            "C1311 PreReleaseTag v2026.10.15.2-rc.0 refuses tag-shape",
            "C1311 PreReleaseTag a long-lived proposal is vYYYY.MM.DD.N-rc.K",
            "C1311 PreReleaseTag promote creates the final tag at the peeled sha",
            "C1311 PreReleaseTag promote leaves the pre-release tag in place",
            "C1311 PreReleaseTag promote refuses a final tag at another sha",
            "C1311 PreReleaseTag no tag delete and no force push");

    [Test]
    public Task C1311_MovingTipAuthority() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1311", "C1311_MovingTipAuthority", 6,
            "C1311 MovingTipAuthority a second sha gets its own candidate root",
            "C1311 MovingTipAuthority the first authority.json still names the first sha",
            "C1311 MovingTipAuthority publish of the first sha refuses remote-candidate-moved",
            "C1311 MovingTipAuthority the first tag still peels to the first sha",
            "C1311 MovingTipAuthority promote of a moved tip refuses remote-candidate-moved",
            "C1311 MovingTipAuthority scriptHashes omit the branch tip");
}
