using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1311 S2, S3, and S4. C1311_LongLivedRcRef (the ref parser, credit kind and candidate journal) runs in process
/// since unit 13 of 2026-11-22-remaining-ps1-to-csharp.md: ReleaseCandidateTests. <c>Test-C1311_PreReleaseTag</c>
/// calls publish and promote-release (its tag parsing and long-lived proposal rows run in process: ReleaseGateLibraryTests).
/// <c>Test-C1311_MovingTipAuthority</c> pins one long-lived ref at two SHAs.
/// A legacy stamp ref still earns <c>rc-release</c> and still publishes a final tag.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseBranchModelGateTests
{
    [Test]
    public Task C1311_PreReleaseTag() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1311", "C1311_PreReleaseTag", 4,
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
