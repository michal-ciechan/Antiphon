using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1733: real Git and authority matrices; remain in explicit integration coverage.
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
    public Task Shared_seed_keeps_recipients_and_authority_private() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1733", "C1733_SharedSeed", 3);

    [Test]
    public Task Throwing_reservation_mutation_restores_the_control() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1733", "C1733_ReservationFinally", 3);

    [Test]
    public Task Snapshot_bytes_work_and_restore_failures_are_observed() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1733", "C1733_SnapshotWork", 4);
}
