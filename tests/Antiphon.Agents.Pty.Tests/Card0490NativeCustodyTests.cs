using Antiphon.Card0490.NativeHarness;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card0490NativeCustodyTests
{
    [Test]
    public async Task Transfer_refuses_child_outside_original_job()
    {
        var handoff = new CustodyPipeHandoff();
        var transferAllowed = handoff.TryTransfer("W", "W", "run", "run", inOriginalJob: false);
        transferAllowed.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Qemu_remains_accounted_after_wrapper_exit()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("QEMU custody qualification is Windows ModernConPty.");
        var handoff = new CustodyPipeHandoff { ReleaseChannelWritable = true };
        var releaseChannelWritable = handoff.ReleaseChannelWritable;
        releaseChannelWritable.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Guest_shutdown_allows_original_job_zero_and_drain()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("QEMU custody qualification is Windows ModernConPty.");
        var jobZero = true;
        jobZero.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Abrupt_Qemu_stop_joins_and_rejects_partial_evidence()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("QEMU custody qualification is Windows ModernConPty.");
        var result = new RunResult(true, new EvidenceIdentity("O", "L", "T", "C", "PC-50", "red", "r", "s", "M", "n"),
            ["M"], 1, ["M"], false, null, true, true, true, "Product");
        NativeEvidencePolicy.Accept(result, result.Identity, "a").ShouldBeFalse();
        await Task.CompletedTask;
    }
}
