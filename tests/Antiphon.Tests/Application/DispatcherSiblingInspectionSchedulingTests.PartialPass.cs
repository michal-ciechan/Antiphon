using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DispatcherSiblingInspectionSchedulingTests
{
    [Test]
    [Arguments("completed")]
    [Arguments("inflight")]
    public async Task C1457_partial_ready_pass_preserves_unrelated_inspections(string state)
    {
        var waiters = new RepositoryLeaseWaiters();
        await using var f = await SiblingInspectionFixture.CreateAsync(
            configure: services => RefuseLease(services, waiters, int.MaxValue));
        var selected = Enumerable.Range(0, 8).Select(_ => f.NewTask()).ToArray();
        var tenth = f.NewTask();
        await using (var db = f.Read())
        {
            db.AgentTasks.AddRange(selected);
            db.AgentTasks.Add(tenth);
            await db.SaveChangesAsync();
            (await db.AgentTasks.CountAsync(t => t.Status == AgentTaskStatus.Queued)).ShouldBe(10);
        }
        foreach (var task in selected)
            f.Queue.TryEnqueue(CardSiblingInspectionIntent.From(task, "master")).ShouldBeTrue();
        await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        foreach (var task in selected)
            f.Queue.Peek(CardSiblingInspectionIntent.From(task, "master")).ShouldNotBeNull();

        var ninth = CardSiblingInspectionIntent.From(f.Task, "master");
        CancellationToken workerToken = default;
        var workerRequests = 0;
        f.Git.Before = (args, ct) =>
        {
            if (args[0] == "for-each-ref")
            {
                Interlocked.Increment(ref workerRequests);
                workerToken = ct;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        f.Git.Block = state == "inflight";
        try
        {
            f.Queue.TryEnqueue(ninth).ShouldBeTrue();
            if (state == "inflight")
                await f.Git.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            else
            {
                await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
                f.Queue.Peek(ninth).ShouldNotBeNull();
            }
            f.Queue.Attempted(f.Task.Id).ShouldBeTrue();
            var before = Volatile.Read(ref workerRequests);
            before.ShouldBeGreaterThan(0, "the ninth task reached a real inspection worker");
            var common = await f.Git.CommonDirectoryAsync(f.Repo.Path, default);
            for (var i = 0; i < selected.Length; i++)
                waiters.Register(common, selected[i].Id, Antiphon.Server.Application.Interfaces.RepositoryLeasePurposes.Dispatch,
                    f.Clock.GetUtcNow().AddSeconds(i), verificationCompatible: true);

            async Task ReadyAsync()
            {
                waiters.SignalReady();
                await using var scope = f.Provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().DispatchReadyWaitersAsync(default);
            }
            await ReadyAsync();
            if (state == "completed")
                f.Queue.Peek(ninth).ShouldNotBeNull("C1457 partial ready pass must preserve the unrelated completed guard");
            else
                workerToken.IsCancellationRequested.ShouldBeFalse("C1457 partial ready pass must not cancel the unrelated worker");
            f.Queue.Attempted(f.Task.Id).ShouldBeTrue("partial passes must preserve unrelated attempted markers");
            Volatile.Read(ref workerRequests).ShouldBe(before, "no replacement inspection was requested");
            await using (var db = f.Read())
            {
                (await db.AgentTasks.CountAsync(t => t.Status == AgentTaskStatus.Dispatched)).ShouldBe(0);
                await db.AgentTasks.Where(t => t.Id == f.Task.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
            }
            await ReadyAsync();
            if (state == "inflight") workerToken.IsCancellationRequested.ShouldBeTrue("terminal task cancels its owned worker");
            await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
            f.Queue.Peek(ninth).ShouldBeNull();
            f.Queue.Attempted(f.Task.Id).ShouldBeFalse();
            await using (var db = f.Read())
                await db.AgentTasks.Where(t => t.Status == AgentTaskStatus.Queued)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
            await using (var scope = f.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().DispatchQueuedAsync(default);
            f.Queue.Outstanding.ShouldBe(0, "an empty queued snapshot must clean all retained observations");
            selected.ShouldAllBe(t => !f.Queue.Attempted(t.Id));
        }
        finally
        {
            f.Git.Release.TrySetResult();
            await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
