using System.Data.Common;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class OutputDistillationCleanupTests
{
    [Test]
    public async Task Clock_pump_does_not_expire_a_ledger_write_while_real_io_is_blocked()
    {
        var barrier = new CleanupBarrier("ledger")
        { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var h = new OutputDistillationHarness(configureDb: o => o.AddInterceptors(barrier));
        var seat = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync();
        using var stop = new CancellationTokenSource();
        var pending = h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, stop.Token);
        Task? pump = null;
        try
        {
            var run = await h.WaitForDistillAsync(seat.Id);
            barrier.Armed = true;
            await h.SettleDistillAsync(run.Id, h.PassingDistillation());
            pump = h.PumpClockAsync(pending);
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cleanupTime = h.Clock.GetUtcNow();
            await Task.Delay(100);
            h.Clock.GetUtcNow().ShouldBe(cleanupTime);
            barrier.Active.ShouldBeTrue("the pump must not cancel real ledger I/O with a clock advance");
            barrier.Release.TrySetResult();
            await pump;
            (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem().Outcome.ShouldBe(DistillationOutcome.Shadowed);
        }
        finally
        {
            barrier.Release.TrySetResult();
            stop.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
            if (pump is not null) { try { await pump; } catch (OperationCanceledException) { } }
        }
    }

    [Test]
    public async Task A_canceled_never_typed_brief_is_expired_despite_its_dispatch_stamp()
    {
        var barrier = new CleanupBarrier("cancel")
        { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var h = new OutputDistillationHarness(configureDb: o => o.AddInterceptors(barrier));
        var seat = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync();
        using var stop = new CancellationTokenSource();
        var pending = h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, stop.Token);
        try
        {
            var run = await h.WaitForDistillAsync(seat.Id);
            await using var db = OutputDistillationHarness.CreateContext();
            await db.AgentTasks.Where(t => t.Id == run.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, AgentTaskStatus.Dispatched)
                .SetProperty(t => t.DispatchedAt, h.Clock.GetUtcNow().UtcDateTime));
            barrier.Armed = true;
            h.Clock.Advance(TimeSpan.FromSeconds(45));
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The terminal queue's durable verdict: the dispatch claim existed, but no input
            // was ever typed. Queue behavior itself is exercised by the expired-brief tests.
            await db.AgentTasks.Where(t => t.Id == run.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, AgentTaskStatus.Canceled)
                .SetProperty(t => t.FailureReason, "Optional work expired before execution."));
            barrier.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
            ledger.Outcome.ShouldBe(DistillationOutcome.DegradedExpired);
            ledger.ExpiryPhase.ShouldBe("dispatch-queue");
        }
        finally
        {
            barrier.Release.TrySetResult();
            stop.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task Blocked_alert_does_not_delay_the_next_request()
    {
        var alert = new BlockedAlert();
        using var h = new OutputDistillationHarness(servicesOverride: s =>
        {
            s.AddSingleton<IAlertService>(alert);
            s.AddSingleton<IModelAvailability>(new FailedAvailability());
        });
        await h.EnsureSpecialistAsync();
        using var worker = new CompletionNoteWorkHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            new CompletionNoteFlushQueue(), h.Provider.GetRequiredService<SpecialistFailureQueue>(), h.Clock,
            NullLogger<CompletionNoteWorkHostedService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var first = await h.SeedSourceAsync();
            await h.Distiller.RequestAsync(first.Task.Id, first.QueuedMessageId, CancellationToken.None);
            await alert.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = await h.SeedSourceAsync();
            await h.Distiller.RequestAsync(second.Task.Id, second.QueuedMessageId, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            alert.Active.ShouldBeTrue("the second request must finish while incident delivery is still held");
            (await h.LedgerAsync(second.Task.Id)).ShouldHaveSingleItem().Outcome.ShouldBe(DistillationOutcome.DegradedUnavailable);
            h.Clock.Advance(TimeSpan.FromSeconds(2));
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(() => !alert.Active);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private sealed class FailedAvailability : IModelAvailability
    {
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) =>
            throw new IOException("synthetic availability I/O failure");
    }

    private sealed class BlockedAlert : IAlertService
    {
        public bool Active;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RaiseAsync(AlertRaise alert, CancellationToken ct)
        {
            Active = true;
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { Active = false; }
        }
    }

    [Test]
    public async Task Earlier_cleanup_consumes_the_later_operations_allowance()
    {
        var barrier = new CleanupBarrier("hold");
        using var h = new OutputDistillationHarness(configureDb: o => o.AddInterceptors(barrier));
        var seat = await h.EnsureSpecialistAsync();
        var now = h.Clock.GetUtcNow();
        var seed = await h.SeedSourceAsync(holdUntil: now.AddSeconds(45).UtcDateTime);
        var pending = h.Distiller.RequestAsync(new(seed.Task.Id, seed.QueuedMessageId, now,
            now.AddSeconds(45), OutputDistillerMode.Apply), CancellationToken.None);
        await h.WaitForDistillAsync(seat.Id);
        barrier.BeforeCancel = () => h.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        barrier.Armed = true;
        h.Clock.Advance(TimeSpan.FromSeconds(45));
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Clock.GetUtcNow().ShouldBe(now.AddSeconds(46.5));
        h.Clock.Advance(TimeSpan.FromMilliseconds(500));
        await Task.WhenAny(pending, Task.Delay(300));
        var completedWithinBudget = pending.IsCompleted;
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        completedWithinBudget.ShouldBeTrue("the hold release has only the 500ms left from the one cleanup allowance");
        barrier.Active.ShouldBeFalse();
    }

    [Test]
    [Arguments("cancel")]
    [Arguments("hold")]
    [Arguments("ledger")]
    public async Task Cleanup_has_one_separate_budget(string phase)
    {
        var barrier = new CleanupBarrier(phase);
        using var h = new OutputDistillationHarness(configureDb: o => o.AddInterceptors(barrier));
        var seat = await h.EnsureSpecialistAsync();
        var now = h.Clock.GetUtcNow();
        var seed = await h.SeedSourceAsync(holdUntil: now.AddSeconds(45).UtcDateTime);
        var pending = h.Distiller.RequestAsync(new(seed.Task.Id, seed.QueuedMessageId, now,
            now.AddSeconds(45), OutputDistillerMode.Apply), CancellationToken.None);
        await h.WaitForDistillAsync(seat.Id);
        barrier.Armed = true;
        h.Clock.Advance(TimeSpan.FromSeconds(45));
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        barrier.Active.ShouldBeTrue();
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        barrier.Active.ShouldBeFalse("cooperative I/O completed before scope disposal");
        h.Clock.GetUtcNow().ShouldBe(now.AddSeconds(47));
        var note = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        (note.HoldUntil is null || note.HoldUntil <= h.Clock.GetUtcNow().UtcDateTime).ShouldBeTrue();
        note.Body.ShouldBe(seed.RawBody);
        (await h.ReloadTaskAsync(seed.Task.Id)).Result.ShouldBe(seed.Report);
    }

    private sealed class CleanupBarrier(string phase) : DbCommandInterceptor
    {
        public bool Armed;
        public bool Active;
        public Action? BeforeCancel;
        public TaskCompletionSource? Release;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task WaitAsync(DbCommand command, CancellationToken ct)
        {
            var sql = command.CommandText;
            if (Armed && sql.StartsWith("UPDATE") && sql.Contains("AgentTasks"))
            { BeforeCancel?.Invoke(); BeforeCancel = null; }
            var matches = phase switch
            {
                "cancel" => sql.StartsWith("UPDATE") && sql.Contains("AgentTasks"),
                "hold" => sql.StartsWith("UPDATE") && sql.Contains("SessionQueuedMessages"),
                _ => sql.Contains("INSERT INTO \"OutputDistillations\""),
            };
            if (!Armed || !matches) return;
            Active = true;
            Entered.TrySetResult();
            try { await (Release?.Task ?? Task.Delay(Timeout.InfiniteTimeSpan, ct)).WaitAsync(ct); }
            finally { Active = false; }
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { await WaitAsync(command, cancellationToken); return result; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { await WaitAsync(command, cancellationToken); return result; }
    }
}
