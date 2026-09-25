using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomeOutageMentionTests
{
    [Test]
    public async Task C696Red_Mention_before_first_List_is_persisted()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        await h.RouteAsync("@fake please retain this accepted mention\n");
        var row = (await h.RowsAsync()).ShouldHaveSingleItem("accepted mention must be durable before any runner List");
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryAttempts.ShouldBe(0);
        await using var peer = await h.RecoverAsync();
        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None)).ShouldBe(1);
        await h.AssertReceiptAsync(row.Body);
        (await h.RowsAsync()).Single().Status.ShouldBe(QueuedMessageStatus.Sent);
        peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(2);
    }

    [Test]
    public async Task C696Red_Mention_in_reconnect_gap_survives_restart()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        await using var a = await h.RecoverAsync();
        await h.DisconnectAsync(a);
        await h.RouteAsync("@fake please survive the desktop graph restart\n");
        var row = (await h.RowsAsync()).ShouldHaveSingleItem("reconnect gap must retain the accepted route");
        await h.RecreateGraphAsync();
        (await h.RowsAsync()).Single().Id.ShouldBe(row.Id);
        await using var b = await h.RecoverAsync();
        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None)).ShouldBe(1);
        await h.AssertReceiptAsync(row.Body);
        b.RequestCount(PhoneHomeOperation.Input).ShouldBe(2);
    }
}
