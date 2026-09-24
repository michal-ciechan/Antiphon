using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class DurableRunnerSpillReceiptTests
{
    [Test]
    public async Task Queued_runner_spill_keeps_its_bytes_and_receives_a_complete_UserPrompt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
        const string cwd = "/runner/worktrees/task-one";
        const string oldPath = ".antiphon/old-brief.md";
        var fullBody = "durable-receipt-0647\n" + new string('r', 4096);
        h.Queue.StageRemoteSpill(h.SessionId, cwd, new PhoneHomeInputSpill(oldPath, fullBody));
        await QueuedReceiptAssertions.HoldRecipientBusyAsync(schema.ConnectionString, h.SessionId);
        await h.Queue.EnqueueAsync(h.SessionId, "Read " + cwd + "/" + oldPath,
            MessageSendMode.WhenIdle, CancellationToken.None, QueuedMessageOrigin.Delegation);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await db.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.RemoteSpillBody.ShouldBe(fullBody);
        row.Body.ShouldContain(TypedBodySpill.InboxRelativePath(row.Id.ToString("D")));
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            schema.ConnectionString, h, row, h.SessionId, busy: true, busyBeforeDelivery: true);
    }
}
