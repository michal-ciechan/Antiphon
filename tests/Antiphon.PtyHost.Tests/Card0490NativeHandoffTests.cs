using Antiphon.Card0490.NativeHarness;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
public class Card0490NativeHandoffTests
{
    [Test]
    public async Task Crossed_peer_or_run_cannot_transfer()
    {
        var handoff = new CustodyPipeHandoff();
        var transferAllowed = handoff.TryTransfer("W1", "W2", "run", "run", true);
        transferAllowed.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Partial_duplication_never_authorizes_root_exit()
    {
        var handoff = new CustodyPipeHandoff();
        handoff.TryTransfer("W", "W", "run", "run", true);
        var exitRootSent = handoff.TryExitRoot(adopted: true, released: true, guestReady: true, duplicatedHandles: 3);
        exitRootSent.ShouldBeFalse();
        handoff.ExitRootSent.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Release_ack_requires_original_end_closure()
    {
        var handoff = new CustodyPipeHandoff();
        handoff.OnReleased(0);
        var openOriginalEndsAtReleased = handoff.OpenOriginalEndsAtReleased;
        openOriginalEndsAtReleased.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Sender_retains_originals_until_adoption()
    {
        var handoff = new CustodyPipeHandoff();
        handoff.OnAdopted(true);
        var originalHandlesOpenUntilAdopted = handoff.OriginalHandlesOpenUntilAdopted;
        originalHandlesOpenUntilAdopted.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Root_exit_waits_for_transfer_and_guest_ready()
    {
        var handoff = new CustodyPipeHandoff();
        handoff.TryTransfer("W", "W", "run", "run", true);
        var exitRootSent = handoff.TryExitRoot(adopted: false, released: true, guestReady: true, duplicatedHandles: 4);
        exitRootSent.ShouldBeFalse();
        await Task.CompletedTask;
    }
}
