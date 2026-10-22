using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1430 V-5 (CARD-1462 option A plan, D-4). The dispatcher holds the repository mutation lease
/// R only for the crossings that mutate the repository: the worktree cut, a repair source, a
/// SourceLanding snapshot and its validation. It releases R before the progress-baseline capture,
/// whose git work is read-only plus a journaled pin that takes its own short lease. A Code claim
/// whose capture fails after the release is still failed, exactly as before.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DispatcherLeaseSpanTests
{
    /// <summary>
    /// Red witness: before D-4 the dispatch lease was disposed after the claim committed, so every
    /// baseline pin was recorded while R was held.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1430_lease_is_released_before_the_baseline_capture()
    {
        var log = new LeaseSpanLog();
        await using var f = await SiblingInspectionFixture.CreateAsync(
            configure: services => LeaseSpanLog.Install(services, log));

        await f.ObserveAsync();
        var cut = await f.TickAsync();

        cut.Dispatched.ShouldBe(1, log.Trace);
        await using (var read = f.Read())
        {
            var task = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            task.WorktreePath.ShouldNotBeNull();
            task.ProgressBaselineJson.ShouldNotBeNullOrEmpty("the Code claim still captures its source identity");
        }

        var released = log.IndexOf(LeaseSpanLog.DispatchReleased);
        var firstPin = log.IndexOf(LeaseSpanLog.Pin);
        var headRead = log.IndexOf(LeaseSpanLog.CaptureHeadRead);
        log.IndexOf(LeaseSpanLog.DispatchAcquired).ShouldBeGreaterThanOrEqualTo(0, "the cut took the dispatch lease: " + log.Trace);
        released.ShouldBeGreaterThanOrEqualTo(0, "the dispatch lease was released: " + log.Trace);
        firstPin.ShouldBeGreaterThanOrEqualTo(0, "the baseline was pinned: " + log.Trace);
        headRead.ShouldBeGreaterThanOrEqualTo(0, "the capture read the worktree HEAD: " + log.Trace);
        released.ShouldBeLessThan(firstPin, "baseline pin recorded before the lease dispose: " + log.Trace);
        released.ShouldBeLessThan(headRead, "the capture's first read ran under the dispatch lease: " + log.Trace);
        log.Count(LeaseSpanLog.DispatchReleased).ShouldBe(1, "the later disposals are idempotent: " + log.Trace);
    }

    /// <summary>
    /// The release sits after the dispatcher's own <c>ValidateVerificationAsync</c>: a SourceLanding
    /// snapshot's creation and validation both see a live dispatch lease and the task launches. A
    /// release placed one statement earlier would make the validation refuse
    /// <c>repository_lease_required</c>.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1430_source_landing_validation_runs_under_the_lease()
    {
        var log = new LeaseSpanLog();
        await using var world = await PostLandMutationWorld.CreateAsync(provision: false);
        Guid taskId;
        await using (var scope = world.Host.Services.CreateAsyncScope())
        {
            taskId = (await world.TaskService(scope.ServiceProvider)
                .CreateAsync(world.Request(world.Companion), world.Caller, default)).Id;
        }

        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            world.Host.Schema.ConnectionString, Path.Combine(world.Host.Fixture.Root, "trees"),
            git: world.Host.Fixture.Git, runners: new SingleRunnerDirectory(world.Runner),
            configure: services =>
            {
                // The SourceLanding claim reserves a verification execution before it launches
                // (CARD-0604); the world dispatches with the same two registrations.
                services.AddSingleton(world.Runner);
                services.AddScoped<VerificationExecutionService>();
                LeaseSpanLog.Install(services, log);
            });
        AgentTaskDispatcher.TickResult tick;
        await using (var scope = provider.CreateAsyncScope())
            tick = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);

        tick.Dispatched.ShouldBe(1, log.Trace);
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            task.WorktreePath.ShouldNotBeNull();
            task.WorktreeBaseSha.ShouldBe(task.SourceLandingSha, "the snapshot sits at the published source");
        }

        var released = log.IndexOf(LeaseSpanLog.DispatchReleased);
        released.ShouldBeGreaterThanOrEqualTo(0, "the dispatch lease was released: " + log.Trace);
        var checks = log.Events.Select((e, i) => (Event: e, Index: i))
            .Where(x => x.Event.StartsWith(LeaseSpanLog.DispatchOwns, StringComparison.Ordinal)).ToList();
        checks.Count.ShouldBeGreaterThanOrEqualTo(2,
            "creation and the dispatcher's own validation both check ownership: " + log.Trace);
        checks.ShouldAllBe(x => x.Event == LeaseSpanLog.DispatchOwns + "true" && x.Index < released,
            "every ownership check saw a live dispatch lease before the release: " + log.Trace);
        log.Count(LeaseSpanLog.DispatchReleased).ShouldBe(1, log.Trace);
    }

    /// <summary>
    /// A Code claim's capture failure after the release still fails the claim with
    /// <c>repair_source_identity_unavailable</c>, launches nothing and leaves R free.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1430_baseline_failure_after_release_still_fails_a_code_claim()
    {
        var log = new LeaseSpanLog();
        await using var f = await SiblingInspectionFixture.CreateAsync(
            configure: services => LeaseSpanLog.Install(services, log,
                git => git.FailRevParse = (_, revision) => revision == "HEAD"));

        await f.ObserveAsync();
        var tick = await f.TickAsync();

        tick.Dispatched.ShouldBe(0, "a Code claim whose source identity cannot be captured never launches: " + log.Trace);
        await using (var read = f.Read())
        {
            var task = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            task.Status.ShouldBe(AgentTaskStatus.Failed, log.Trace);
            task.FailureReason.ShouldBe("repair_source_identity_unavailable");
            task.AgentSessionId.ShouldBeNull();
            task.ProgressBaselineJson.ShouldBeNullOrEmpty();
        }

        var released = log.IndexOf(LeaseSpanLog.DispatchReleased);
        var headRead = log.IndexOf(LeaseSpanLog.CaptureHeadRead);
        released.ShouldBeGreaterThanOrEqualTo(0, "the dispatch lease was released: " + log.Trace);
        headRead.ShouldBeGreaterThanOrEqualTo(0, "the capture read the worktree HEAD: " + log.Trace);
        released.ShouldBeLessThan(headRead, "the failing capture ran under the dispatch lease: " + log.Trace);
        log.Count(LeaseSpanLog.Pin).ShouldBe(0, "nothing is pinned for an unreadable HEAD: " + log.Trace);
        log.Count(LeaseSpanLog.DispatchReleased).ShouldBe(1, "the failure path's later disposal is idempotent: " + log.Trace);

        // R is free again: the failed claim leaked nothing.
        var leases = f.Provider.GetRequiredService<IRepositoryMutationLease>();
        await using var probe = await leases.TryAcquireAsync(f.Repo.Path, default);
        probe.ShouldNotBeNull("R is free after the failed claim: " + log.Trace);
    }
}
