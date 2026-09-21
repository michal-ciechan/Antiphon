using Antiphon.Card0490.NativeHarness;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card0490NativeCustodyTests
{
    private static void RequireRealQemuCustody() =>
        throw new SkipTestException(
            "CARD-0490 D-12 is not yet implemented: V-F3/V-F4 need a real QEMU process, pipe handoff and original job.");

    [Test]
    public async Task Transfer_refuses_child_outside_original_job()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Qemu_remains_accounted_after_wrapper_exit()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Guest_shutdown_allows_original_job_zero_and_drain()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Abrupt_Qemu_stop_joins_and_rejects_partial_evidence()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }
}
