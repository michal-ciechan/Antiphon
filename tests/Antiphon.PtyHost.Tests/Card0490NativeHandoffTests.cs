using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
public class Card0490NativeHandoffTests
{
    private static void RequireRealQemuCustody() =>
        throw new SkipTestException(
            "CARD-0490 D-12 is not yet implemented: V-F3 pipe handoff needs a real QEMU process, owned pipes and original job.");

    [Test]
    public async Task Crossed_peer_or_run_cannot_transfer()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Partial_duplication_never_authorizes_root_exit()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Release_ack_requires_original_end_closure()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Sender_retains_originals_until_adoption()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Root_exit_waits_for_transfer_and_guest_ready()
    {
        RequireRealQemuCustody();
        await Task.CompletedTask;
    }
}
