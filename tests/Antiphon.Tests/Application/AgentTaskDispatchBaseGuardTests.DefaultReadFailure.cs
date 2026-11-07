using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskDispatchBaseGuardTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C1581_DefaultReadFailureKeepsQueuedAndWaiter(bool timeout) =>
        ExerciseDefaultReadFailureAsync(timeout, "preserve");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C1581_DefaultReadRecoveryDispatchesOnLaterTick(bool timeout) =>
        ExerciseDefaultReadFailureAsync(timeout, "recover");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C1581_PersistentDefaultReadFailureIsBounded(bool timeout) =>
        ExerciseDefaultReadFailureAsync(timeout, "bound");

    private async Task ExerciseDefaultReadFailureAsync(bool timeout, string scenario)
    {
        await using var w = await SiblingWorld.CreateAsync();
        await w.AddAsync();
        w.Task.ProjectId = w.Project.Id;
        w.Project.BaseBranch = "master";
        await w.Db.SaveChangesAsync();
        var fault = new DefaultReadFailure(timeout);
        await using var provider = CreateProvider(w.Connection, w.Repo.WorktreeRoot,
            interceptor: fault, onLeaseAcquired: async () =>
            {
                await using var edit = w.Fresh();
                await edit.Projects.Where(p => p.Id == w.Project.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.BaseBranch, "missing-c1581"));
                fault.Arm();
            });
        var queue = provider.GetRequiredService<CardSiblingInspectionQueue>();
        var waiters = provider.GetRequiredService<RepositoryLeaseWaiters>();
        var common = (await w.Repo.GitReadAsync("rev-parse", "--path-format=absolute", "--git-common-dir")).Trim();
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        waiters.Register(common, w.Task.Id, RepositoryLeasePurposes.Dispatch, since);
        queue.Start();
        await using (var scope = provider.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await dispatcher.TickAsync(default);
            await queue.WhenIdleAsync();
            var refused = await dispatcher.TickAsync(default);
            refused.Dispatched.ShouldBe(0);
            refused.Failures.ShouldBe(0);
        }
        fault.Failures.ShouldBe(1, "the injected read is the refresh after the claim read rejected the changed default");
        await AssertQueuedAsync();
        if (scenario == "preserve") return;

        if (scenario == "recover")
        {
            fault.Recover();
            // New dispatcher scope: retry state must not depend on the previous scoped instance.
            await using var scope = provider.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await dispatcher.TickAsync(default);
            await queue.WhenIdleAsync();
            var recovered = await dispatcher.TickAsync(default);
            recovered.Dispatched.ShouldBe(1);
            recovered.Failures.ShouldBe(0);
            await using var check = w.Fresh();
            var task = await check.AgentTasks.SingleAsync(t => t.Id == w.Task.Id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched);
            task.AgentSessionId.ShouldNotBeNull();
            task.WorktreePath.ShouldNotBeNull();
            (await check.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).ShouldBe(0);
            waiters.Snapshot(common).ShouldNotContain(x => x.TaskId == task.Id);
            return;
        }

        // Three deferred failures, then a fourth takes the ordinary failure/outbox path.
        // Each tick uses a new scope, proving the finite budget survives scoped dispatchers.
        for (var attempt = 2; attempt <= 4; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            var tick = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
            fault.Failures.ShouldBe(attempt, "at most one read attempt per tick; no inline retry loop");
            tick.Dispatched.ShouldBe(0);
            tick.Failures.ShouldBe(attempt == 4 ? 1 : 0);
            if (attempt < 4) await AssertQueuedAsync();
        }
        await using (var check = w.Fresh())
        {
            var failed = await check.AgentTasks.SingleAsync(t => t.Id == w.Task.Id);
            failed.Status.ShouldBe(AgentTaskStatus.Failed);
            failed.AgentSessionId.ShouldBeNull();
            failed.WorktreePath.ShouldBeNull();
            failed.FailureReason.ShouldContain("Sibling default read failed 4 times");
            failed.FailureReason.ShouldContain(timeout ? "inspection_timeout" : "injected default read failure");
            (await check.AgentTaskEvents.CountAsync(e => e.AgentTaskId == failed.Id && e.Type == AgentTaskEventType.Failed)).ShouldBe(1);
            (await check.AgentTaskLandNotifications.CountAsync(n => n.TaskId == failed.Id)).ShouldBe(1);
            waiters.Snapshot(common).ShouldNotContain(x => x.TaskId == failed.Id);
        }

        async Task AssertQueuedAsync()
        {
            await using var check = w.Fresh();
            var queued = await check.AgentTasks.SingleAsync(t => t.Id == w.Task.Id);
            queued.Status.ShouldBe(AgentTaskStatus.Queued);
            queued.AgentSessionId.ShouldBeNull();
            queued.WorktreePath.ShouldBeNull();
            queued.FailureReason.ShouldBeNull();
            (await check.AgentTaskEvents.CountAsync(e => e.AgentTaskId == queued.Id && e.Type == AgentTaskEventType.Failed)).ShouldBe(0);
            (await check.AgentTaskEvents.CountAsync(e => e.AgentTaskId == queued.Id && e.Type == AgentTaskEventType.Warning
                && e.Detail!.StartsWith("sibling_default_read_retry:"))).ShouldBe(fault.Failures);
            var waiter = waiters.Snapshot(common).Single(x => x.TaskId == queued.Id);
            waiter.Since.ShouldBe(since);
            waiter.Purpose.ShouldBe(RepositoryLeasePurposes.Dispatch);
        }
    }

    private sealed class DefaultReadFailure(bool timeout) : DbCommandInterceptor
    {
        private bool _armed;
        private int _reads;
        public int Failures { get; private set; }
        public void Arm() => _armed = true;
        public void Recover() => _armed = false;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken ct = default)
        {
            if (_armed && command.CommandText.Contains("SELECT p.\"BaseBranch\"", StringComparison.Ordinal)
                && eventData.Context is AppDbContext context && context.ChangeTracker.Entries<AgentTask>().Any()
                && ++_reads > 1)
            {
                Failures++;
                // Exercise the production ReadAsync cancellation-to-budget-exhaustion conversion.
                if (timeout) throw new OperationCanceledException("injected default read timeout", ct);
                throw new IOException("injected default read failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
