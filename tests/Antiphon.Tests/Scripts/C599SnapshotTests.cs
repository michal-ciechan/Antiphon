using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1731: the evidence-path cases (G-287 containment on the production resolver, ReleaseAuthority.ResolveEvidencePath)
// of scripts/test-release-gate.ps1. Unit 14 of 2026-11-22-remaining-ps1-to-csharp.md moved the C599A matrices in process
// (ReleaseGateFixture's Save/Restore), so the PowerShell snapshot helpers and their two cases (C1731_SnapshotIsolation,
// C1731_SnapshotWork, scripts/fixtures/c1731-snapshot.ps1) are gone with them.
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C599SnapshotTests
{
    [Test]
    public Task Evidence_path_refusals_match_the_seeded_set() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1731", "C1731_EvidenceRefusals", 14,
            "C1731 ordinary relative evidence resolves under the candidate root",
            "C1731 missing evidence resolves when no component is a reparse point",
            "C1731 blank and empty evidence refuses",
            "C1731 absolute evidence refuses",
            "C1731 parent-relative escape refuses",
            "C1731 a path that normalizes inside the root resolves to that file",
            "C1731 a linked directory pointing outside refuses",
            "C1731 a linked directory whose target stays inside the root still refuses",
            "C1731 a link in a middle path component refuses",
            "C1731 a linked file refuses",
            "C1731 a broken link refuses",
            "C1731 a hard link resolves",
            "C1731 seeded evidence results match the recorded set",
            "C1731 an accepted path refuses after that component becomes a link");

    [Test]
    public Task Evidence_path_walk_finishes_within_fifteen_seconds() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1731", "C1731_EvidenceWalk", 1,
            "C1731 six hundred eleven ordinary resolutions finish within fifteen seconds");
}
