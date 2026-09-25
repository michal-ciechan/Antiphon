using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PostLandMutationDeliveryTests
{
    [Test]
    public async Task C699_Committed_settlement_signals_the_task_even_when_publication_is_interrupted()
    {
        var cut = new SettlementPublicationCut();
        await using var settled = await SettleMutationAsync(new()
        {
            ConfigureServices = services => services.AddSingleton<LandDeliveryBoundary>(cut),
        });
        cut.Hits.ShouldBe(1);
        var signals = settled.Bridge.Provider.GetRequiredService<CompletionNoteFlushQueue>().Recovery
            .Take(DateTime.UtcNow);
        signals.Tasks.ShouldContain(settled.World.TaskId, "settlement must signal before any note publication succeeds");
        await using var db = settled.World.Host.CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C699_Recovery_error_sweeps_other_missed_events_without_waiting_fifteen_minutes()
    {
        var commands = new RecoveryCommands();
        await using var settled = await SettleMutationAsync(new()
        {
            ConfigureDbContext = o => o.AddInterceptors(commands),
        });
        await using (var db = settled.World.Host.CreateContext())
            await db.SessionQueuedMessages.Where(m => m.SourceTaskId == settled.World.TaskId).ExecuteDeleteAsync();
        var clock = new RecoveryClock();
        using var worker = RecoveryWorker(settled.Bridge, clock);
        await worker.StartAsync(default);
        try
        {
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var missed = Guid.NewGuid();
            await using (var db = settled.World.Host.CreateContext())
            {
                await db.AgentTasks.Where(t => t.Id == settled.World.TaskId).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.CompletionNoteQueuedAt, (DateTime?)null)
                    .SetProperty(t => t.CompletionNoteDigest, (string?)null));
                db.AgentTasks.Add(new AgentTask
                {
                    Id = missed, RootTaskId = missed, Title = "missed event", Goal = "recover",
                    SourceLandingOperationId = settled.World.Operation, Status = AgentTaskStatus.Succeeded,
                    ParentSessionId = settled.Bridge.SessionId, ReplyTo = AgentTaskReplyTo.Session,
                    Result = "missed report", CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            commands.FailNextRecoveryRead = true;
            settled.Bridge.Provider.GetRequiredService<CompletionNoteFlushQueue>().Recovery.Check(settled.World.TaskId);
            await UntilAsync(async () =>
            {
                await using var db = settled.World.Host.CreateContext();
                return await db.AgentTasks.CountAsync(t => (t.Id == missed || t.Id == settled.World.TaskId)
                    && t.CompletionNoteQueuedAt != null) == 2;
            }, "one targeted failure must sweep both the requested task and an unrelated missed event", 10);
            commands.Failures.ShouldBe(1);
            await using var verify = settled.World.Host.CreateContext();
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == missed)).ShouldBe(1);
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(1);
        }
        finally { await worker.StopAsync(default); }
    }

    [Test]
    public async Task C699_Startup_pages_past_a_full_batch_without_skipping_owed_tasks()
    {
        await using var settled = await SettleMutationAsync();
        var ids = Enumerable.Range(0, 130).Select(_ => Guid.NewGuid()).ToArray();
        await using (var db = settled.World.Host.CreateContext())
        {
            db.AgentTasks.AddRange(ids.Select(id => new AgentTask
            {
                Id = id, RootTaskId = id, Title = "missed event", Goal = "recover",
                SourceLandingOperationId = settled.World.Operation, Status = AgentTaskStatus.Succeeded,
                ParentSessionId = settled.Bridge.SessionId, ReplyTo = AgentTaskReplyTo.Session,
                Result = "missed report", CreatedAt = DateTime.UtcNow,
            }));
            await db.SaveChangesAsync();
        }
        // This is the paging/stamping test. Hold delivery at the caller's working boundary
        // so 130 real terminal round trips cannot consume its recovery allowance.
        await SetWorkingAsync(settled.World.Host.Schema.ConnectionString, settled.Bridge.SessionId, true);
        using var worker = RecoveryWorker(settled.Bridge, new RecoveryClock());
        await worker.StartAsync(default);
        try
        {
            await UntilAsync(async () =>
            {
                await using var db = settled.World.Host.CreateContext();
                return await db.AgentTasks.CountAsync(t => ids.Contains(t.Id) && t.CompletionNoteQueuedAt != null) == ids.Length;
            }, "stamping the first page must not skip the next page", 20);
            await using var verify = settled.World.Host.CreateContext();
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId != null && ids.Contains(m.SourceTaskId.Value)))
                .ShouldBe(ids.Length);
        }
        finally { await worker.StopAsync(default); }
    }

    [Test]
    public async Task C699_Settlement_event_recovers_failed_publication_without_polling()
    {
        var fault = new FailFirstCompletionInsert();
        var clock = new RecoveryClock();
        CompletionNoteWorkHostedService? worker = null;
        try
        {
            await using var settled = await SettleMutationAsync(new()
            {
                ConfigureDbContext = o => o.AddInterceptors(fault),
            }, beforeSettlement: async h =>
            {
                worker = RecoveryWorker(h, clock);
                await worker.StartAsync(default);
                await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            });
            fault.Failures.ShouldBe(1);
            await UntilAsync(async () =>
            {
                await using var db = settled.World.Host.CreateContext();
                return await db.AgentTasks.AnyAsync(t => t.Id == settled.World.TaskId
                    && t.CompletionNoteQueuedAt != null);
            }, "settlement/publication failure must wake recovery without advancing the sweep clock", 5);
            await using var verify = settled.World.Host.CreateContext();
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(1);
            await worker!.StopAsync(default);
        }
        finally
        {
            if (worker is not null) { await worker.StopAsync(default); worker.Dispose(); }
        }
    }

    [Test]
    public async Task C699_Startup_recovers_a_missed_event_and_restarts_do_not_duplicate()
    {
        var fault = new QueueInsertFault();
        await using var settled = await SettleMutationAsync(new()
        {
            ConfigureDbContext = o => o.AddInterceptors(fault),
        }, fault: fault);
        for (var restart = 0; restart < 2; restart++)
        {
            var clock = new RecoveryClock();
            using var worker = RecoveryWorker(settled.Bridge, clock);
            await worker.StartAsync(default);
            try
            {
                await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using var db = settled.World.Host.CreateContext();
                (await db.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId)).CompletionNoteQueuedAt.ShouldNotBeNull();
                (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(1);
            }
            finally { await worker.StopAsync(default); }
        }
    }

    [Test]
    public async Task C699_History_is_excluded_from_bounded_recovery_and_stamp_repair_queries()
    {
        var commands = new RecoveryCommands();
        await using var settled = await SettleMutationAsync(new()
        {
            ConfigureDbContext = o => o.AddInterceptors(commands),
        });
        await using (var db = settled.World.Host.CreateContext())
            await db.SessionQueuedMessages.Where(m => m.SourceTaskId == settled.World.TaskId).ExecuteDeleteAsync();
        commands.Sql.Clear();
        var clock = new RecoveryClock();
        using var worker = RecoveryWorker(settled.Bridge, clock, new CompletionNoteFlushQueue());
        await worker.StartAsync(default);
        try
        {
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var queries = commands.Sql.ToArray();
            queries.Count(s => s.Contains("UPDATE \"AgentTasks\"", StringComparison.Ordinal)).ShouldBe(0);
            queries.Count(s => s.Contains("FROM \"AgentTaskLandNotifications\"", StringComparison.Ordinal)).ShouldBe(0,
                "a stamped historical task must never reach per-task receipt lookups");
            var candidates = queries.Where(s => s.Contains("FROM \"AgentTasks\"", StringComparison.Ordinal)).ToArray();
            candidates.Length.ShouldBe(1);
            candidates[0].ShouldContain("\"CompletionNoteQueuedAt\" IS NULL");
            candidates[0].ShouldContain("LIMIT");
        }
        finally { await worker.StopAsync(default); }
    }

    [Test]
    public async Task C699_Backstop_waits_fifteen_minutes_between_successful_sweeps()
    {
        var commands = new RecoveryCommands();
        await using var settled = await SettleMutationAsync(new()
        {
            ConfigureDbContext = o => o.AddInterceptors(commands),
        });
        await using (var db = settled.World.Host.CreateContext())
            await db.SessionQueuedMessages.Where(m => m.SourceTaskId == settled.World.TaskId).ExecuteDeleteAsync();
        commands.Sql.Clear();
        var clock = new RecoveryClock();
        using var worker = RecoveryWorker(settled.Bridge, clock, new CompletionNoteFlushQueue());
        await worker.StartAsync(default);
        try
        {
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.DueTimes.ShouldContain(TimeSpan.FromMinutes(15));
            var reads = commands.Sql.Count;
            clock.Inner.Advance(TimeSpan.FromMinutes(14));
            await Task.Delay(100);
            commands.Sql.Count.ShouldBe(reads, "no idle scan before the fifteen-minute deadline");
            clock.Inner.Advance(TimeSpan.FromMinutes(1));
            await UntilAsync(() => Task.FromResult(commands.Sql.Count > reads), "periodic backstop did not run", 5);
        }
        finally { await worker.StopAsync(default); }
    }

    private static CompletionNoteWorkHostedService RecoveryWorker(BridgeQueueHarness h, TimeProvider clock,
        CompletionNoteFlushQueue? flushes = null) => new(
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        flushes ?? h.Provider.GetRequiredService<CompletionNoteFlushQueue>(), new SpecialistFailureQueue(), clock,
        NullLogger<CompletionNoteWorkHostedService>.Instance);

    private sealed class SettlementPublicationCut : LandDeliveryBoundary
    {
        public int Hits;
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "settlement-saved") return Task.CompletedTask;
            Hits++;
            throw new InvalidOperationException("owned interruption after settlement commit before publication");
        }
    }

    private sealed class RecoveryClock : TimeProvider
    {
        public FakeTimeProvider Inner { get; } = new(DateTimeOffset.UtcNow);
        public ConcurrentQueue<TimeSpan> DueTimes { get; } = new();
        public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = Inner.CreateTimer(callback, state, dueTime, period);
            DueTimes.Enqueue(dueTime);
            TimerCreated.TrySetResult();
            return timer;
        }
    }

    private sealed class FailFirstCompletionInsert : SaveChangesInterceptor
    {
        public int Failures;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added)
                && Interlocked.CompareExchange(ref Failures, 1, 0) == 0)
                throw new InvalidOperationException("owned first completion insert failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecoveryCommands : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Sql { get; } = new();
        public volatile bool FailNextRecoveryRead;
        public int Failures;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Sql.Enqueue(command.CommandText);
            if (FailNextRecoveryRead && command.CommandText.Contains("\"SourceLandingOperationId\" IS NOT NULL", StringComparison.Ordinal))
            {
                FailNextRecoveryRead = false;
                Failures++;
                throw new InvalidOperationException("owned completion recovery read failure");
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Sql.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
    }
}
