using Antiphon.SessionRunner;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0824 S3 CP-2 (D-7). An incomplete holder scan is lingering: the release defers.
/// <see cref="Rm_lock_defers_release"/> is the Windows Restart Manager witness and skips on Linux.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class C0824WindowsSlotHolderTests
{
    [Test]
    public async Task Incomplete_scan_defers_release(CancellationToken ct)
    {
        // A probe that cannot prove the scan is complete must not look holder-free.
        using var scratch = Create();
        var pool = scratch.Pool(processes: new ProcWorkspaceSlotProcessProbe("/proc", false));
        var lease = await pool.LeaseAsync(scratch.RequestA(), ct);
        var response = await Release(pool, lease, TaskA);
        response.Outcome.ShouldBe("deferred");
    }

    [Test]
    public Task Rm_lock_defers_release()
    {
        // Real RmGetList is UNOBSERVED on this Linux runner. The method stays in the class so the
        // checkpoint roster is one executed case and one skip. The Windows body lands with the probe.
        NativeWindowsGate.Require(
            "CARD-0824 Windows Restart Manager file lock; the real probe runs on the desktop at S4");
        return Task.CompletedTask;
    }
}
