using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0747 follow-up (Debug 3b0adade). The orchestrator replay came from Pending queue rows, not
/// open land notes: 855 of 1,937 Pending rows on one session were older than seven days and 767 of
/// them had no land note at all. The hosted cleanup and the operator sweep scanned land notes only,
/// so those rows survived both and were typed one a minute after the next restart. These witnesses
/// pin the queue-row rule: a never-typed Pending Delegation or Check row whose source task is
/// terminal, created at or before the cutoff and linked to no open land note, is canceled (never
/// deleted) by the hosted cleanup and by the sweep apply. CARD-1857 adds the acceptance the
/// first witnesses left out: a surviving row is delivered once, a claimed row is spared, a held
/// lock waits a minute, and a pass that stops between batches resumes exactly once.
/// </summary>
[Category("Integration")]
[NotInParallel]
public sealed class C0747QueueRowExpiryTests
{
    /// <summary>
    /// case: an eight-day-old untyped Pending Delegation row for a Succeeded task, no land note.
    /// mutation: drop the queue-row step from the hosted loop (hosted-cleanup) or from the sweep
    /// (sweep-apply). red: the row stays Pending and the caller's next idle turn types it.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("hosted-cleanup")]
    [Arguments("sweep-apply")]
    public async Task C0747_Aged_unlinked_row_of_terminal_task_is_canceled(string arm, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var caller = await AddStoppedCallerAsync(h, ct);
        var row = await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(8), ct);
        var rowsBefore = await CountRowsAsync(h, caller, ct);

        if (arm == "hosted-cleanup")
            await RunHostedPassAsync(h, scans);
        else
            await NewSweep(h).SweepAsync(apply: true, olderThanDays: 7, ct);

        var saved = await RowAsync(h, row.Id, ct);
        saved.Status.ShouldBe(QueuedMessageStatus.Canceled, "an aged row of a terminal task is not news");
        saved.CanceledAt.ShouldNotBeNull();
        saved.DeliveryAttempts.ShouldBe(0);
        (await CountRowsAsync(h, caller, ct)).ShouldBe(rowsBefore, "cancel never deletes");
        h.Adapter.Inputs.ShouldBeEmpty("expiry never types");
    }

    /// <summary>
    /// case: a mixed cohort on one caller. Eligible: an aged Delegation row of a Succeeded task, an aged
    /// Check row of a Failed (checked) task, and an aged row of a Canceled task whose land note is already
    /// LegacyUnverified. Not eligible, one per exclusion: a six-day row, a Working and a Blocked source,
    /// a row linked to a provider-wait note (closed), a capacity wait, a row an open RetryPending note
    /// owns, a typed Pending row, a Sent row, a System row with no task and a Ui row of a terminal task;
    /// the sweep arm adds a row an open AwaitingReceipt note links by its queue id.
    /// mutation: drop any one exclusion from the candidate query. red: that row is canceled.
    /// mutation: drop the cancel. red: an eligible row stays Pending.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("hosted-cleanup")]
    [Arguments("sweep-apply")]
    public async Task C0747_Only_eligible_aged_rows_are_canceled(string arm, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var caller = await AddStoppedCallerAsync(h, ct);
        var cohort = await SeedCohortAsync(h, caller, withQueueLinkedOpenNote: arm == "sweep-apply", ct);
        var before = await StatusesAsync(h, cohort.Values, ct);
        var rowsBefore = await CountRowsAsync(h, caller, ct);

        if (arm == "hosted-cleanup")
            await RunHostedPassAsync(h, scans);
        else
            await NewSweep(h).SweepAsync(apply: true, olderThanDays: 7, ct);

        var after = await StatusesAsync(h, cohort.Values, ct);
        foreach (var (name, id) in cohort)
            after[id].ShouldBe(Eligible.Contains(name) ? QueuedMessageStatus.Canceled : before[id], name);
        (await CountRowsAsync(h, caller, ct)).ShouldBe(rowsBefore, "cancel never deletes");
        h.Adapter.Inputs.ShouldBeEmpty("expiry never types");
    }

    /// <summary>
    /// case: the same cohort through the sweep. mutation: let the dry run cancel, drop the per-kind count,
    /// or count a typed row as eligible. red: a dry run writes, or the reported counts differ. A second
    /// apply cancels nothing (idempotent) and a six-day sweep is still refused.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C0747_Sweep_dry_run_reports_queue_rows_by_kind_and_apply_is_idempotent(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var caller = await AddStoppedCallerAsync(h, ct);
        var cohort = await SeedCohortAsync(h, caller, withQueueLinkedOpenNote: true, ct);
        var sweep = NewSweep(h);
        var before = await StatusesAsync(h, cohort.Values, ct);

        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ValidationException>(() => sweep.SweepAsync(true, 6, ct));
        var dry = await sweep.SweepAsync(apply: false, olderThanDays: null, ct);
        (await StatusesAsync(h, cohort.Values, ct)).ShouldBe(before, "a dry run writes nothing");
        dry.Scanned.ShouldBe(0, "no aged open land note in this cohort");
        dry.QueueRows.Scanned.ShouldBe(4);
        dry.QueueRows.Outcomes.ShouldBe(new Dictionary<string, int> { ["WouldCancel"] = 3, ["TypedPending"] = 1 }, ignoreOrder: true);
        dry.QueueRows.EligibleByKind.ShouldBe(new Dictionary<string, int> { ["Delegation"] = 2, ["Check"] = 1 }, ignoreOrder: true);
        dry.QueueRows.Oldest.ShouldNotBeNull();
        dry.QueueRows.Newest.ShouldNotBeNull();
        dry.QueueRows.Oldest!.Value.ShouldBeLessThanOrEqualTo(dry.QueueRows.Newest!.Value);
        dry.QueueRows.Newest.Value.ShouldBeLessThanOrEqualTo(dry.Cutoff);
        dry.QueueRows.Failures.ShouldBe(0);

        var applied = await sweep.SweepAsync(apply: true, olderThanDays: 7, ct);
        applied.QueueRows.Outcomes.ShouldBe(new Dictionary<string, int> { ["Canceled"] = 3, ["TypedPending"] = 1 }, ignoreOrder: true);
        applied.QueueRows.EligibleByKind.ShouldBe(new Dictionary<string, int> { ["Delegation"] = 2, ["Check"] = 1 }, ignoreOrder: true);
        var after = await StatusesAsync(h, cohort.Values, ct);
        foreach (var (name, id) in cohort)
            after[id].ShouldBe(Eligible.Contains(name) ? QueuedMessageStatus.Canceled : before[id], name);

        var again = await sweep.SweepAsync(apply: true, olderThanDays: 7, ct);
        // Idempotent: only the typed row is still scanned.
        again.QueueRows.Outcomes.ShouldBe(new Dictionary<string, int> { ["TypedPending"] = 1 }, ignoreOrder: true);
        again.QueueRows.EligibleByKind.ShouldBeEmpty();
        (await StatusesAsync(h, cohort.Values, ct)).ShouldBe(after);
    }

    /// <summary>
    /// case: an eight-day-old untyped Pending Delegation row of a Succeeded task, no land note. After the
    /// row is selected and the session queue lock is held, the source is retried to Queued, or an open
    /// land note is linked to the row. mutation: cancel on Pending and untyped alone. red: the row is
    /// Canceled even though it is no longer eligible.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("retried-source")]
    [Arguments("open-land-note")]
    public async Task C0747_Eligibility_rechecked_under_the_lock_keeps_a_row_pending(string arm, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var caller = await AddStoppedCallerAsync(h, ct);
        var row = await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(8), ct);
        h.Queue.BeforeAgedExpiryRecheck = async (db, token) =>
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == row.SourceTaskId, token);
            if (arm == "retried-source")
            {
                task.Status = AgentTaskStatus.Queued;
                await db.SaveChangesAsync(token);
                return;
            }

            var eventId = Guid.NewGuid();
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = eventId, AgentTaskId = task.Id, AgentTask = task, Type = AgentTaskEventType.LandRefused,
                Detail = "C0747 race open note", At = DateTime.UtcNow,
            });
            db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
            {
                Id = Guid.NewGuid(), TaskId = task.Id, SourceEventId = eventId, Kind = LandNotificationKind.Outcome,
                ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = caller, Body = "open land note published while expiry waited",
                ContentDigest = "c0747-race", CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow.AddHours(1),
                State = LandNotificationState.AwaitingReceipt, QueueMessageId = row.Id,
            });
            await db.SaveChangesAsync(token);
        };

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        await new AgedQueueRowExpiry(db, h.Queue).ExpireAsync(DateTime.UtcNow.AddDays(-7), apply: true, lockWait: null, ct);

        await using var observed = h.CreateObserverContext();
        if (arm == "retried-source")
            (await observed.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.SourceTaskId, ct)).Status
                .ShouldBe(AgentTaskStatus.Queued, "the retry landed before the write");
        else
            (await observed.AgentTaskLandNotifications.AsNoTracking().CountAsync(n => n.QueueMessageId == row.Id
                && n.State == LandNotificationState.AwaitingReceipt, ct))
                .ShouldBe(1, "the open note landed before the write");
        var saved = await RowAsync(h, row.Id, ct);
        saved.Status.ShouldBe(QueuedMessageStatus.Pending, "eligibility is decided again under the queue lock");
        saved.CanceledAt.ShouldBeNull();
        saved.DeliveryAttempts.ShouldBe(0);
    }

    /// <summary>
    /// case: 1,937 aged eligible Pending rows on one stopped caller, the backlog measured on one
    /// orchestrator session. One hosted pass cancels them a page at a time. mutation: after each page,
    /// publish the whole remaining queue including bodies. red: a publication carries the body marker
    /// or is larger than one page of ids.
    /// </summary>
    [Test]
    [Timeout(300_000)]
    public async Task C0747_Scale_expiry_cancels_in_bounded_batches(CancellationToken ct)
    {
        const int rows = 1937;
        const string marker = "C0747-SCALE-BODY";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var caller = await AddStoppedCallerAsync(h, ct);
        await SeedScaleAsync(h, caller, rows, marker, ct);
        var batches = new List<SessionMessageQueueService.AgedExpiryBatch>();
        h.Queue.OnAgedExpiryBatch = batches.Add;

        await RunHostedPassAsync(h, scans, TimeSpan.FromMinutes(3));

        await using var observed = h.CreateObserverContext();
        (await observed.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == caller, ct))
            .ShouldBe(rows, "cancel never deletes");
        (await observed.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == caller
            && m.Status == QueuedMessageStatus.Canceled, ct)).ShouldBe(rows);

        h.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionQueueChanged");
        var publications = h.EventBus.PublishedEvents.Where(e => e.EventName == "SessionQueueExpiry").ToList();
        publications.Count.ShouldBe(8, "1,937 rows cancel a page at a time");
        batches.Count.ShouldBe(publications.Count);
        batches.Sum(b => b.RowsUnderLock).ShouldBe(rows);
        batches.Sum(b => b.PublishedIds).ShouldBe(rows);
        var maxJson = 0;
        foreach (var publication in publications)
        {
            var notice = publication.Payload.ShouldBeOfType<SessionQueueExpiryNotice>();
            notice.CanceledIds.Count.ShouldBeGreaterThan(0);
            notice.CanceledIds.Count.ShouldBeLessThanOrEqualTo(AgedQueueRowExpiry.BatchSize);
            var json = JsonSerializer.Serialize(notice);
            json.ShouldNotContain(marker);
            json.Length.ShouldBeLessThanOrEqualTo(16 * 1024);
            if (json.Length > maxJson) maxJson = json.Length;
        }
        foreach (var batch in batches)
        {
            batch.RowsUnderLock.ShouldBeGreaterThan(0);
            batch.RowsUnderLock.ShouldBeLessThanOrEqualTo(AgedQueueRowExpiry.BatchSize);
            batch.PublishedIds.ShouldBe(batch.RowsUnderLock);
        }
        publications.Select(p => ((SessionQueueExpiryNotice)p.Payload).PendingRemaining).Min().ShouldBe(0);
        publications.Select(p => ((SessionQueueExpiryNotice)p.Payload).PendingRemaining).Max()
            .ShouldBe(rows - AgedQueueRowExpiry.BatchSize);
        var maxRows = batches.Max(b => b.RowsUnderLock);
        var maxHold = batches.Max(b => b.LockHeldMilliseconds);
        var maxIds = batches.Max(b => b.PublishedIds);
        Console.WriteLine(
            $"C0747 scale batches={batches.Count} maxRows={maxRows} maxHoldMs={maxHold} maxPublishedIds={maxIds} maxJsonChars={maxJson} holdsMs={string.Join(',', batches.Select(b => b.LockHeldMilliseconds))} pageSizes={string.Join(',', batches.Select(b => b.RowsUnderLock))}");
    }

    /// <summary>
    /// case: LandNoteReceiptExpiryDays = 0, an aged eligible row. mutation: drop the off-switch check from
    /// the hosted step. red: the row is canceled although expiry is off.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C0747_Hosted_queue_row_expiry_honours_the_off_switch(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans, v => v.LandNoteReceiptExpiryDays = 0);
        var caller = await AddStoppedCallerAsync(h, ct);
        var row = await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(30), ct);

        await RunHostedPassAsync(h, scans);

        (await RowAsync(h, row.Id, ct)).Status.ShouldBe(QueuedMessageStatus.Pending, "expiry is off");
    }

    /// <summary>
    /// CARD-1857. case: one eight-day eligible row and one six-day row on a live idle caller.
    /// Expiry cancels only the aged row. The survivor is then flushed. mutation: cancel every
    /// pending row, or flush a canceled row. red: the survivor has no complete UserPrompt, or the
    /// canceled body appears in one.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1857_Survivor_reaches_the_recipient_once_and_a_cancelled_row_never_does(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var kept = await SeedRowAsync(h, h.SessionId, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(6), ct);
        var aged = await SeedRowAsync(h, h.SessionId, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(8), ct);

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            await new AgedQueueRowExpiry(db, h.Queue).ExpireAsync(DateTime.UtcNow.AddDays(-7), apply: true, lockWait: null, ct);

        (await RowAsync(h, aged.Id, ct)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        (await RowAsync(h, kept.Id, ct)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await UserPromptTextsAsync(h, h.SessionId, ct)).ShouldBeEmpty("expiry never types");

        await h.Queue.FlushIfIdleAsync(h.SessionId, ct);
        await h.Queue.FlushIfIdleAsync(h.SessionId, ct);

        var savedKept = await RowAsync(h, kept.Id, ct);
        savedKept.Status.ShouldBe(QueuedMessageStatus.Sent);
        savedKept.DeliveryAttempts.ShouldBe(1);
        savedKept.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        var savedAged = await RowAsync(h, aged.Id, ct);
        savedAged.Status.ShouldBe(QueuedMessageStatus.Canceled);
        savedAged.DeliveryAttempts.ShouldBe(0);
        savedAged.CanceledAt.ShouldNotBeNull();
        var prompts = await UserPromptTextsAsync(h, h.SessionId, ct);
        prompts.Count.ShouldBe(1, "the survivor is delivered once");
        PromptSubmissionMatch.IsCompleteIn(kept.Body, prompts[0]).ShouldBeTrue();
        PromptSubmissionMatch.Normalize(prompts[0]).ShouldBe(PromptSubmissionMatch.Normalize(kept.Body));
        prompts.ShouldAllBe(text => !PromptSubmissionMatch.IsCompleteIn(aged.Body, text),
            "a canceled row never reaches the recipient");
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
    }

    /// <summary>
    /// CARD-1857. case: the expiry scan has already selected an eligible row, then delivery claims
    /// it under the queue lock before the cancel write. mutation: cancel the selected ids without
    /// reading eligibility again. red: the claimed row is Canceled and its UserPrompt is not the
    /// delivery.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1857_Delivery_claim_between_select_and_cancel_is_not_cancelled(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var row = await SeedRowAsync(h, h.SessionId, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(8), ct);
        h.Queue.BeforeAgedExpiryRecheck = (_, token) => h.Queue.FlushPendingUnderHeldLockAsync(h.SessionId, token);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        var result = await new AgedQueueRowExpiry(db, h.Queue).ExpireAsync(DateTime.UtcNow.AddDays(-7), apply: true, lockWait: null, ct);

        result.Scanned.ShouldBe(1, "the scan selected the row before delivery claimed it");
        result.Outcomes.ShouldBe(new Dictionary<string, int> { ["Changed"] = 1 }, ignoreOrder: true);
        var saved = await RowAsync(h, row.Id, ct);
        saved.Status.ShouldBe(QueuedMessageStatus.Sent, "the cancel re-reads eligibility under the lock");
        saved.CanceledAt.ShouldBeNull();
        saved.DeliveryAttempts.ShouldBe(1);
        saved.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        var prompts = await UserPromptTextsAsync(h, h.SessionId, ct);
        prompts.Count.ShouldBe(1);
        PromptSubmissionMatch.IsCompleteIn(row.Body, prompts[0]).ShouldBeTrue();
        PromptSubmissionMatch.Normalize(prompts[0]).ShouldBe(PromptSubmissionMatch.Normalize(row.Body));
    }

    /// <summary>
    /// CARD-1857. case: the caller's queue lock is held across the hosted expiry pass. The row
    /// stays pending, a pass 59 seconds later still skips it, and the pass at one minute cancels
    /// it. mutation: retry on the next loop tick, or wait the full hour. red: the row is canceled
    /// at 59 seconds, or still pending at one minute.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1857_Held_queue_lock_is_retried_after_one_minute(CancellationToken ct)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans, clock: clock);
        var caller = await AddStoppedCallerAsync(h, ct);
        var row = await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(8), ct);
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        try
        {
            var gate = h.Queue.GetLock(caller);
            await gate.WaitAsync(ct);
            try
            {
                await ExpireDueAsync(worker, ct);
                (await RowAsync(h, row.Id, ct)).Status.ShouldBe(QueuedMessageStatus.Pending, "a held queue lock skips the session");
            }
            finally
            {
                gate.Release();
            }

            clock.Advance(TimeSpan.FromSeconds(59));
            await ExpireDueAsync(worker, ct);
            (await RowAsync(h, row.Id, ct)).Status.ShouldBe(QueuedMessageStatus.Pending, "the busy retry waits a minute");

            clock.Advance(TimeSpan.FromSeconds(1));
            await ExpireDueAsync(worker, ct);
            var saved = await RowAsync(h, row.Id, ct);
            saved.Status.ShouldBe(QueuedMessageStatus.Canceled);
            saved.DeliveryAttempts.ShouldBe(0);
            saved.CanceledAt.ShouldNotBeNull();
        }
        finally
        {
            worker.Dispose();
        }
    }

    /// <summary>
    /// CARD-1857. case: one more eligible row than a batch. The pass is canceled after the first
    /// batch commits. The next pass cancels only what is still pending, and a third pass cancels
    /// nothing. mutation: restart from the first id and cancel again, or abandon the remainder.
    /// red: a first-batch CanceledAt changes, or a pending row survives the second pass.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1857_Stop_between_batches_resumes_and_cancels_the_rest_once(CancellationToken ct)
    {
        var rows = AgedQueueRowExpiry.BatchSize + 1;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanCounter();
        await using var h = await CreateHarnessAsync(schema.ConnectionString, scans);
        var caller = await AddStoppedCallerAsync(h, ct);
        await SeedScaleAsync(h, caller, rows, "C1857-RECOVERY", ct);
        using var stop = new CancellationTokenSource();
        var batches = 0;
        h.Queue.OnAgedExpiryBatch = _ =>
        {
            if (Interlocked.Increment(ref batches) == 1)
                stop.Cancel();
        };

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            await Should.ThrowAsync<OperationCanceledException>(() =>
                new AgedQueueRowExpiry(db, h.Queue).ExpireAsync(DateTime.UtcNow.AddDays(-7), apply: true, lockWait: null, stop.Token));

        batches.ShouldBe(1, "the pass stops after the first committed batch");
        var first = await CanceledStampsAsync(h, caller, ct);
        first.Count.ShouldBe(AgedQueueRowExpiry.BatchSize);
        (await PendingCountAsync(h, caller, ct)).ShouldBe(1);
        var firstNotice = h.EventBus.PublishedEvents.Where(e => e.EventName == "SessionQueueExpiry").ShouldHaveSingleItem()
            .Payload.ShouldBeOfType<SessionQueueExpiryNotice>();
        firstNotice.CanceledIds.Count.ShouldBe(AgedQueueRowExpiry.BatchSize);
        firstNotice.CanceledIds.OrderBy(id => id).ShouldBe(first.Keys.OrderBy(id => id));

        AgedQueueRowExpiryDto resumed;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            resumed = await new AgedQueueRowExpiry(db, h.Queue).ExpireAsync(DateTime.UtcNow.AddDays(-7), apply: true, lockWait: null, ct);

        resumed.Scanned.ShouldBe(1, "a canceled row is no longer a candidate");
        resumed.Outcomes.ShouldBe(new Dictionary<string, int> { ["Canceled"] = 1 }, ignoreOrder: true);
        var second = await CanceledStampsAsync(h, caller, ct);
        second.Count.ShouldBe(rows);
        foreach (var (id, at) in first)
            second[id].ShouldBe(at, "the first batch is not canceled again");
        (await PendingCountAsync(h, caller, ct)).ShouldBe(0);
        var notices = h.EventBus.PublishedEvents.Where(e => e.EventName == "SessionQueueExpiry")
            .Select(e => e.Payload.ShouldBeOfType<SessionQueueExpiryNotice>()).ToList();
        notices.Count.ShouldBe(2);
        notices[1].CanceledIds.Count.ShouldBe(1);
        first.Keys.ShouldNotContain(notices[1].CanceledIds[0]);

        AgedQueueRowExpiryDto again;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            again = await new AgedQueueRowExpiry(db, h.Queue).ExpireAsync(DateTime.UtcNow.AddDays(-7), apply: true, lockWait: null, ct);
        again.Scanned.ShouldBe(0);
        again.Outcomes.ShouldBeEmpty();
        (await CanceledStampsAsync(h, caller, ct)).ShouldBe(second);
        h.EventBus.PublishedEvents.Count(e => e.EventName == "SessionQueueExpiry").ShouldBe(2);
    }

    private static readonly HashSet<string> Eligible = ["delegation-succeeded", "check-failed", "closed-note-canceled-task"];

    private static async Task<Dictionary<string, Guid>> SeedCohortAsync(BridgeQueueHarness h, Guid caller, bool withQueueLinkedOpenNote,
        CancellationToken ct)
    {
        var aged = TimeSpan.FromDays(8);
        var cohort = new Dictionary<string, Guid>
        {
            ["delegation-succeeded"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, aged, ct)).Id,
            ["check-failed"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Check, AgentTaskStatus.Failed, aged, ct)).Id,
            ["young"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, TimeSpan.FromDays(6), ct)).Id,
            ["non-terminal-working"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Working, aged, ct)).Id,
            ["non-terminal-blocked"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Check, AgentTaskStatus.Blocked, aged, ct)).Id,
            ["capacity-wait"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, aged, ct,
                r => r.CapacityWaitId = Guid.NewGuid())).Id,
            ["typed"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, aged, ct,
                r => r.DeliveryAttempts = 1)).Id,
            ["sent"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Delegation, AgentTaskStatus.Succeeded, aged, ct, r =>
            {
                r.Status = QueuedMessageStatus.Sent;
                r.SentAt = DateTime.UtcNow - aged;
            })).Id,
            ["system-no-task"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.System, null, aged, ct)).Id,
            ["ui-terminal-task"] = (await SeedRowAsync(h, caller, QueuedMessageOrigin.Ui, AgentTaskStatus.Succeeded, aged, ct)).Id,
        };
        cohort["closed-note-canceled-task"] = await SeedNoteLinkedRowAsync(h, caller, aged, AgentTaskStatus.Canceled, ct, n =>
        {
            n.State = LandNotificationState.LegacyUnverified;
            n.LastErrorCode = AgentTaskLandNotificationService.ReceiptExpiredCode;
        });
        cohort["provider-wait-note"] = await SeedNoteLinkedRowAsync(h, caller, aged, AgentTaskStatus.Succeeded, ct, n =>
        {
            n.Kind = LandNotificationKind.ProviderInputWait;
            n.State = LandNotificationState.NotRequired;
        });
        cohort["open-note"] = await SeedNoteLinkedRowAsync(h, caller, aged, AgentTaskStatus.Succeeded, ct, n =>
        {
            n.State = LandNotificationState.RetryPending;
            n.NextAttemptAt = DateTime.UtcNow.AddHours(1);
        });
        if (withQueueLinkedOpenNote)
            cohort["open-note-queue-link"] = await SeedNoteLinkedRowAsync(h, caller, aged, AgentTaskStatus.Succeeded, ct, n =>
                n.State = LandNotificationState.AwaitingReceipt, linkByQueueId: true);
        return cohort;
    }

    /// <summary>
    /// A land note (created now, so the note rule never ages it) and an aged Pending row for its task,
    /// linked by the row's source note (default) or only by the note's queue id.
    /// </summary>
    private static async Task<Guid> SeedNoteLinkedRowAsync(BridgeQueueHarness h, Guid caller, TimeSpan age, AgentTaskStatus taskStatus,
        CancellationToken ct, Action<AgentTaskLandNotification> shape, bool linkByQueueId = false)
    {
        await using var db = h.CreateObserverContext();
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, caller);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == note.TaskId, ct);
        task.Status = taskStatus;
        shape(note);
        var sequence = (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == caller)
            .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;
        var row = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = caller, Status = QueuedMessageStatus.Pending, Sequence = sequence,
            Origin = QueuedMessageOrigin.Delegation, SourceTaskId = note.TaskId, CreatedAt = DateTime.UtcNow - age,
            SourceLandNotificationId = linkByQueueId ? null : note.Id, Body = note.Body, ContentDigest = note.ContentDigest,
        };
        db.SessionQueuedMessages.Add(row);
        if (linkByQueueId) note.QueueMessageId = row.Id;
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private static async Task<Dictionary<Guid, QueuedMessageStatus>> StatusesAsync(BridgeQueueHarness h, IEnumerable<Guid> ids,
        CancellationToken ct)
    {
        var keep = ids.ToList();
        await using var db = h.CreateObserverContext();
        return await db.SessionQueuedMessages.AsNoTracking().Where(m => keep.Contains(m.Id)).OrderBy(m => m.Id)
            .ToDictionaryAsync(m => m.Id, m => m.Status, ct);
    }

    internal static Task<BridgeQueueHarness> CreateHarnessAsync(string connectionString, LandDeliveryBoundary scans,
        Action<Antiphon.Server.Application.Settings.DeliveryVerificationSettings>? verification = null,
        TimeProvider? clock = null) =>
        ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false, ConnectionString = connectionString, ConfigureDeliveryVerification = verification,
            TimeProvider = clock,
            ConfigureServices = services =>
            {
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddScoped<AgentTaskLandNotificationService>();
                services.AddSingleton(scans);
            },
        });

    internal static LandNoteExpirySweepService NewSweep(BridgeQueueHarness h) =>
        new(new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)),
            h.Provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);

    /// <summary>Starts the real hosted loop, waits for its first completed pass, and stops it.</summary>
    internal static async Task RunHostedPassAsync(BridgeQueueHarness h, ScanCounter scans, TimeSpan? budget = null)
    {
        var limit = budget ?? TimeSpan.FromSeconds(90);
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(limit);
        await worker.StartAsync(stop.Token);
        try
        {
            (await scans.WaitForScanAsync(1, limit)).ShouldBeTrue("the hosted pass completes");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    /// <summary>A stopped caller session of its own, so no flush can type a seeded row first.</summary>
    internal static async Task<Guid> AddStoppedCallerAsync(BridgeQueueHarness h, CancellationToken ct)
    {
        await using var db = h.CreateObserverContext();
        var clone = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId, ct);
        clone.Id = Guid.NewGuid();
        clone.DelegationTokenHash = null;
        clone.Status = SessionStatus.Stopped;
        db.AgentSessions.Add(clone);
        await db.SaveChangesAsync(ct);
        return clone.Id;
    }

    internal static async Task<SessionQueuedMessage> SeedRowAsync(BridgeQueueHarness h, Guid caller, QueuedMessageOrigin origin,
        AgentTaskStatus? taskStatus, TimeSpan age, CancellationToken ct, Action<SessionQueuedMessage>? shape = null)
    {
        await using var db = h.CreateObserverContext();
        Guid? taskId = null;
        if (taskStatus is AgentTaskStatus status)
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "C0747 queue row", Goal = "aged queue row fixture",
                WorkingDirectory = Path.GetTempPath(), Status = status, ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = caller, CreatedAt = DateTime.UtcNow - age,
            });
            taskId = id;
        }
        var sequence = (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == caller)
            .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;
        var row = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = caller, Status = QueuedMessageStatus.Pending, Sequence = sequence,
            Origin = origin, SourceTaskId = taskId, CreatedAt = DateTime.UtcNow - age,
            Body = $"[task {(taskId ?? Guid.Empty).ToString()[..8]} done] C0747 aged note {sequence}",
        };
        shape?.Invoke(row);
        db.SessionQueuedMessages.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>One terminal task and <paramref name="rows"/> aged untyped Delegation rows on <paramref name="caller"/>.</summary>
    private static async Task SeedScaleAsync(BridgeQueueHarness h, Guid caller, int rows, string marker, CancellationToken ct)
    {
        await using var db = h.CreateObserverContext();
        var taskId = Guid.NewGuid();
        var created = DateTime.UtcNow.AddDays(-8);
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "C0747 scale", Goal = "aged queue scale fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = caller, CreatedAt = created,
        });
        var body = marker + "-" + new string('x', 48);
        for (var i = 1; i <= rows; i++)
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = caller, Status = QueuedMessageStatus.Pending, Sequence = i,
                Origin = QueuedMessageOrigin.Delegation, SourceTaskId = taskId, CreatedAt = created, Body = body,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    internal static async Task<SessionQueuedMessage> RowAsync(BridgeQueueHarness h, Guid id, CancellationToken ct)
    {
        await using var db = h.CreateObserverContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id, ct);
    }

    internal static async Task<int> CountRowsAsync(BridgeQueueHarness h, Guid caller, CancellationToken ct)
    {
        await using var db = h.CreateObserverContext();
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == caller, ct);
    }

    /// <summary>
    /// The hosted loop sleeps five real seconds between passes. The busy-retry witness drives the
    /// due check itself so the minute is the test clock.
    /// </summary>
    private static async Task ExpireDueAsync(AgentTaskLandNotificationHostedService worker, CancellationToken ct)
    {
        var method = typeof(AgentTaskLandNotificationHostedService).GetMethod(
            "ExpireAgedQueueRowsIfDueAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        var task = (Task)method!.Invoke(worker, [ct])!;
        await task;
    }

    private static async Task<List<string>> UserPromptTextsAsync(BridgeQueueHarness h, Guid sessionId, CancellationToken ct)
    {
        await using var db = h.CreateObserverContext();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt)
            .OrderBy(e => e.Sequence)
            .Select(e => e.Text ?? "")
            .ToListAsync(ct);
    }

    private static async Task<Dictionary<Guid, DateTime>> CanceledStampsAsync(BridgeQueueHarness h, Guid caller, CancellationToken ct)
    {
        await using var db = h.CreateObserverContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == caller && m.Status == QueuedMessageStatus.Canceled && m.CanceledAt != null)
            .ToDictionaryAsync(m => m.Id, m => m.CanceledAt!.Value, ct);
    }

    private static async Task<int> PendingCountAsync(BridgeQueueHarness h, Guid caller, CancellationToken ct)
    {
        await using var db = h.CreateObserverContext();
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == caller && m.Status == QueuedMessageStatus.Pending, ct);
    }

    internal sealed class ScanCounter : LandDeliveryBoundary
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "notification-scan") Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }

        public async Task<bool> WaitForScanAsync(int count, TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            while (Count < count)
            {
                if (DateTime.UtcNow >= deadline) return false;
                await Task.Delay(50);
            }
            return true;
        }
    }
}
