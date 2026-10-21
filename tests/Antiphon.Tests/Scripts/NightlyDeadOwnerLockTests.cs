using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1386. Each method runs one Test-C1386_* case of scripts/test-nightly-run.ps1.
/// CARD-1396 adds the write-held run.lock read case.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyDeadOwnerLockTests
{
    [Test]
    public Task C1386_DeadOwnerReclaimed() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1386",
            nameof(C1386_DeadOwnerReclaimed), 6,
            [
                "C1386 DeadOwner legacy leaves lock phase",
                "C1386 DeadOwner legacy records dead-owner-legacy",
                "C1386 DeadOwner legacy replaces run id",
                "C1386 DeadOwner same-boot records dead-owner-same-boot",
                "C1386 DeadOwner reboot reclaims recycled pid",
                "C1386 DeadOwner reboot records dead-owner-rebooted"
            ]);

    [Test]
    public Task C1386_LiveOwnerRefused() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1386",
            nameof(C1386_LiveOwnerRefused), 4,
            [
                "C1386 LiveOwner refusal is live-owner",
                "C1386 LiveOwner keeps planted run id",
                "C1386 LiveOwner writes no reclaim record",
                "C1386 LiveOwner start mismatch stays live-owner"
            ]);

    [Test]
    public Task C1386_AmbiguousOwnerRefused() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1386",
            nameof(C1386_AmbiguousOwnerRefused), 6,
            [
                "C1386 Ambiguous parent-alive refuses lock-held",
                "C1386 Ambiguous parent-alive keeps the lock",
                "C1386 Ambiguous foreign-host refuses lock-held",
                "C1386 Ambiguous foreign-host keeps the lock",
                "C1386 Ambiguous missing-parent refuses lock-held",
                "C1386 Ambiguous missing-parent keeps the lock"
            ]);

    [Test]
    public Task C1396_WriteHeldLockReadable() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1396",
            nameof(C1396_WriteHeldLockReadable), 4,
            [
                "C1396 Reader opens the lock with FileShare.ReadWrite",
                "C1396 Write-held lock reads in-process",
                "C1396 Write-held lock still refuses FileShare.None",
                "C1396 Write-held lock reads from a second process"
            ]);
}
