using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
/// deleted) by the hosted cleanup and by the sweep apply.
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
        Action<Antiphon.Server.Application.Settings.DeliveryVerificationSettings>? verification = null) =>
        ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false, ConnectionString = connectionString, ConfigureDeliveryVerification = verification,
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
    internal static async Task RunHostedPassAsync(BridgeQueueHarness h, ScanCounter scans)
    {
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        try
        {
            (await scans.WaitForScanAsync(1, TimeSpan.FromSeconds(30))).ShouldBeTrue("the hosted pass completes");
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
