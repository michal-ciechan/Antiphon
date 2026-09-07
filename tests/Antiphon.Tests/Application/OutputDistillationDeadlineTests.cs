using Antiphon.Server.Application.Services;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class OutputDistillationDeadlineTests
{
    [Test]
    public async Task Expired_dequeue_does_not_provision_and_preserves_phase()
    {
        using var h = new OutputDistillationHarness();
        var now = h.Clock.GetUtcNow();
        var seed = await h.SeedSourceAsync(holdUntil: now.AddSeconds(45).UtcDateTime);
        h.Clock.Advance(TimeSpan.FromSeconds(45));
        await h.Distiller.RequestAsync(new(seed.Task.Id, seed.QueuedMessageId, now,
            now.AddSeconds(45), OutputDistillerMode.Apply), CancellationToken.None);
        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.DegradedExpired);
        ledger.ExpiryPhase.ShouldBe("request-queue");
        ledger.QueueWaitMs.ShouldBe(45_000);
        ledger.DistillTaskId.ShouldBeNull();
        ledger.DeadlineAt.ShouldBe(now.AddSeconds(45).UtcDateTime);
        Directory.Exists(Path.Combine(h.Scratch, ".claude")).ShouldBeFalse();
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).Body.ShouldBe(seed.RawBody);
    }

    [Test]
    public async Task Queue_and_wait_share_one_deadline()
    {
        using var h = new OutputDistillationHarness();
        var seat = await h.EnsureSpecialistAsync();
        var now = h.Clock.GetUtcNow();
        var seed = await h.SeedSourceAsync(holdUntil: now.AddSeconds(45).UtcDateTime);
        h.Clock.Advance(TimeSpan.FromSeconds(20));
        using var stop = new CancellationTokenSource();
        var request = h.Distiller.RequestAsync(new(seed.Task.Id, seed.QueuedMessageId,
            now, now.AddSeconds(45), OutputDistillerMode.Apply), stop.Token);
        var run = await h.WaitForDistillAsync(seat.Id);
        try
        {
        run.ExecutionDeadlineAt.ShouldBe(now.AddSeconds(45).UtcDateTime);
        h.Clock.Advance(TimeSpan.FromSeconds(25));
        await request.WaitAsync(TimeSpan.FromSeconds(5));
        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.DegradedExpired);
        ledger.QueueWaitMs.ShouldBe(20_000);
        ledger.DeadlineAt.ShouldBe(now.AddSeconds(45).UtcDateTime);
        ledger.DecisionAt.ShouldBe(now.AddSeconds(45).UtcDateTime);
        (await h.ReloadTaskAsync(run.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        }
        finally
        {
            stop.Cancel();
            try { await request; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    [Arguments(0, "already")]
    [Arguments(1, "already")]
    [Arguments(0, "lock")]
    [Arguments(0, "claim")]
    public async Task Expired_brief_is_rejected_before_first_input(int attempts, string expiresAt)
    {
        var clock = new BriefClock();
        var probe = new ClaimClock(clock);
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        { TimeProvider = clock, ConfigureDbContext = o => o.AddInterceptors(probe) });
        var deadline = clock.GetUtcNow().AddSeconds(expiresAt == "already" ? -1 : 45).UtcDateTime;
        var id = Guid.NewGuid();
        Guid? messageId = null;
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Title = "optional", Goal = "optional",
                Role = AgentTaskRole.Distill, Status = AgentTaskStatus.Dispatched, AgentId = h.AgentId,
                AgentSessionId = h.SessionId, CreatedAt = DateTime.UtcNow, DispatchedAt = DateTime.UtcNow,
                WorkingDirectory = h.TempRoot, ExecutionDeadlineAt = deadline });
            await db.SaveChangesAsync();
        }
        await h.Queue.EnqueueAsync(h.SessionId, "complete optional brief", MessageSendMode.WhenIdle,
            CancellationToken.None, onCreated: x => messageId = x, deliverIfIdle: false,
            executionDeadlineAt: deadline, executionTaskId: id);
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            await db.SessionQueuedMessages.Where(m => m.Id == messageId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.DeliveryAttempts, attempts));
        }
        if (expiresAt == "claim") probe.Armed = true;
        var sem = h.Queue.GetLock(h.SessionId);
        if (expiresAt == "lock") await sem.WaitAsync();
        var flush = h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        if (expiresAt == "lock") { clock.Offset = TimeSpan.FromSeconds(45); sem.Release(); }
        await flush.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = BridgeQueueHarness.CreateContext();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == id);
        if (attempts == 0)
        {
            h.Adapter.SentInput.ShouldBeEmpty();
            task.Status.ShouldBe(AgentTaskStatus.Canceled);
            (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == messageId)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        }
        else { task.Status.ShouldBe(AgentTaskStatus.Dispatched); h.Adapter.SentInput.ShouldNotBeEmpty(); }
        h.Adapter.KillCount.ShouldBe(0);
    }

    private sealed class BriefClock : TimeProvider
    {
        public TimeSpan Offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    private sealed class ClaimClock(BriefClock clock) : DbCommandInterceptor
    {
        public bool Armed;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.StartsWith("UPDATE") && command.CommandText.Contains("SessionQueuedMessages"))
            { Armed = false; clock.Offset = TimeSpan.FromSeconds(45); }
            return ValueTask.FromResult(result);
        }
    }
}
