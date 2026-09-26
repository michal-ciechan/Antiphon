using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0726 V-16. An alarm note is a pending WhenIdle system row, hinted, never typed inline.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerAlarmNotifierTests
{
    [Test]
    public async Task a_note_is_a_pending_whenidle_system_row_hinted_and_never_typed_inline()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync();
        var flush = new DroppingFlushQueue(0);
        var notifier = new QueueRunnerAlarmNotifier(harness.Queue, flush, NullLogger<QueueRunnerAlarmNotifier>.Instance);
        const string header = "[runner server2 unavailable]";
        const string body = "two tasks are pinned";

        await notifier.NotifyAsync(harness.SessionId, header, body, CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(harness.ConnectionString));
        var row = db.SessionQueuedMessages.Where(message => message.AgentSessionId == harness.SessionId).ShouldHaveSingleItem();
        row.Origin.ShouldBe(QueuedMessageOrigin.System);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryAttempts.ShouldBe(0);
        row.NoteHeader.ShouldBe(header);
        row.Body.ShouldContain(body);
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();
        flush.Calls.ShouldBe([harness.SessionId]);

        await Should.ThrowAsync<Exception>(() => notifier.NotifyAsync(Guid.NewGuid(), header, body, CancellationToken.None));
        db.SessionQueuedMessages.Count(message => message.AgentSessionId != harness.SessionId).ShouldBe(0);
    }
}
