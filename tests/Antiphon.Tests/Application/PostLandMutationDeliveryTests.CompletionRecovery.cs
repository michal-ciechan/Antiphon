using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
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
        using var worker = RecoveryWorker(settled.Bridge, clock);
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
        using var worker = RecoveryWorker(settled.Bridge, clock);
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

    private static CompletionNoteWorkHostedService RecoveryWorker(BridgeQueueHarness h, TimeProvider clock) => new(
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Provider.GetRequiredService<CompletionNoteFlushQueue>(), new SpecialistFailureQueue(), clock,
        NullLogger<CompletionNoteWorkHostedService>.Instance);

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
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Sql.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Sql.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
    }
}
