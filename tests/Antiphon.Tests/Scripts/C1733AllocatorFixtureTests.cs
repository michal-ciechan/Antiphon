using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1733: the C1339 real-git fixture's own guards (scripts/fixtures/c1733-allocator.ps1), which the candidate
// coordinator's rows of scripts/test-release-gate.ps1 still use. Unit 14 of 2026-11-22-remaining-ps1-to-csharp.md moved the
// publisher's and promoter's C1339 cases in process (ReleaseTagAllocatorTests over ReleaseGateGitFixture); with them went
// C1733_ReservationFinally and C1733_SnapshotWork (the PowerShell snapshot around the promoter's reservation variants) and
// SharedSeed's clone-fresh-authority row (a publisher -WhatIf probe of a cloned seed).
[Category("Slow")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1733AllocatorFixtureTests
{
    [Test]
    public Task Native_metadata_avoids_provider_operations() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1733", "C1733_NativeMetadata", 1);

    [Test]
    public Task Boundaries_and_changed_links_are_checked_fresh() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1733", "C1733_BoundariesAndFreshLinks", 5);

    [Test]
    public Task Shared_seed_keeps_recipients_private() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1733", "C1733_SharedSeed", 2);
}
