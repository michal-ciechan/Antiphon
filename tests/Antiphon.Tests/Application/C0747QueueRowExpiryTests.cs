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
