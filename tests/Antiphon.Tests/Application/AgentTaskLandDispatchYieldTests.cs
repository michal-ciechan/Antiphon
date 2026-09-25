using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0672 V-5 (D-2): a land yields at admission, before it acquires the repository mutation
/// lease, while a queued dispatch (or a settlement sync) waits for that lease on the same
/// repository - for at most <c>Delegation:LandYieldToDispatchMaxSeconds</c>. Real git through
/// <see cref="LandingSafetyHarness"/>; the clock is fake so the budget is advanced, never slept.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandDispatchYieldTests
{
    [Test]
    [Arguments(RepositoryLeasePurposes.Dispatch)]
    [Arguments(RepositoryLeasePurposes.WorktreeSettlement)]
    public async Task C672_a_waiting_dispatch_holds_the_land_at_admission_until_it_clears(string purpose)
    {
        await using var h = await StartAsync();
        var waiter = Guid.NewGuid();
        h.Waiters.Register(await h.CommonAsync(), waiter, purpose, h.Clock.GetUtcNow());
        h.Harness.Fixture.Git.Trace.Clear();

        // (a) The land stands aside: Held with the yield code, one Held event, no caller note.
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        var request = await h.RequestAsync();
        request.State.ShouldBe(LandRequestState.Held);
        request.HoldReasonCode.ShouldBe(AgentTaskLandService.LeaseYieldedToDispatchCode);
        request.HoldDetail.ShouldNotBeNull();
        request.HoldDetail.ShouldContain(DelegationReportFormatter.Short(waiter));
        request.HoldDetail.ShouldContain($"({purpose})");
        request.HeldSince.ShouldBe(h.Clock.GetUtcNow().UtcDateTime);
        request.HoldingTaskId.ShouldBeNull();
        (await h.EventsAsync(AgentTaskEventType.Held)).ShouldHaveSingleItem().LandRequestId.ShouldBe(request.Id);
        (await h.NotificationCountAsync()).ShouldBe(0);
        h.Harness.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("push")
            || a.Contains("worktree") || a.Contains("--ff-only"));
        await using (var probe = await h.Harness.Services.GetRequiredService<IRepositoryMutationLease>()
                         .TryAcquireAsync(h.Harness.Fixture.Repository, CancellationToken.None))
        {
            probe.ShouldNotBeNull("the yielding land never took the lease");
        }

        // (b) Re-picked while the waiter is still there: still Held, no second Held event.
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        (await h.EventsAsync(AgentTaskEventType.Held)).Count.ShouldBe(1);
        (await h.RequestAsync()).HeldSince.ShouldBe(request.HeldSince);

        // (c) The dispatch got its gap: the next pass is admitted and lands.
        h.Waiters.Clear(waiter);
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Complete);
        var released = (await h.EventsAsync(AgentTaskEventType.HeldReleased)).ShouldHaveSingleItem();
        released.At.ShouldBeGreaterThan((await h.EventsAsync(AgentTaskEventType.Held)).Single().At);
        await h.AssertLandedAsync();
        (await h.EventsAsync(AgentTaskEventType.Warning)).ShouldNotContain(w => w.Detail.Contains("yield"));
    }

    [Test]
    public async Task C672_yield_budget_exhausted_lands_with_one_warning()
    {
        await using var h = await StartAsync();
        h.Waiters.Register(await h.CommonAsync(), Guid.NewGuid(), RepositoryLeasePurposes.Dispatch, h.Clock.GetUtcNow());

        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        h.Clock.Advance(TimeSpan.FromSeconds(89));
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        (await h.EventsAsync(AgentTaskEventType.Warning)).ShouldBeEmpty();

        h.Clock.Advance(TimeSpan.FromSeconds(2));
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Complete);

        var warning = (await h.EventsAsync(AgentTaskEventType.Warning))
            .Where(w => w.Detail.Contains("yield")).ShouldHaveSingleItem();
        warning.Detail.ShouldContain("yield budget exhausted after 91s");
        warning.LandRequestId.ShouldBe((await h.RequestAsync()).Id);
        (await h.EventsAsync(AgentTaskEventType.Held)).Count.ShouldBe(1);
        await h.AssertLandedAsync();
        h.Waiters.Any(await h.CommonAsync()).ShouldBeTrue("the land proceeds past the waiter; it never clears it");
    }

    /// <summary>
    /// Review 3488192e (1): admission ends the yield budget. A land that proceeded past an exhausted
    /// budget and was then interrupted is retried under the same request; with a dispatch still
    /// waiting, the retry must yield again on a fresh budget instead of inheriting the spent one.
    /// </summary>
    [Test]
    public async Task C672_admission_ends_an_exhausted_yield_budget_so_a_retry_yields_again()
    {
        // One registry for the whole server process: the retry is a re-pick by the same process.
        var waiters = new RepositoryLeaseWaiters();
        await using var h = await StartAsync(shared: waiters);
        var waiter = Guid.NewGuid();
        waiters.Register(await h.CommonAsync(), waiter, RepositoryLeasePurposes.Dispatch, h.Clock.GetUtcNow());

        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        h.Clock.Advance(TimeSpan.FromSeconds(91));
        var crashed = false;
        h.Harness.Fixture.Git.AfterCommand = (_, args, result) =>
        {
            if (!crashed && result.Succeeded && args[0] == "push")
            {
                crashed = true;
                throw new InterruptedLand();
            }
            return Task.CompletedTask;
        };

        // The budget is spent, so this pass is admitted and runs until the injected interruption.
        await Should.ThrowAsync<InterruptedLand>(() => h.Harness.RunAsync());
        crashed.ShouldBeTrue();
        var interrupted = await h.RequestAsync();
        interrupted.IsPending.ShouldBeTrue();
        interrupted.Attempt.ShouldBe(1);
        (await h.EventsAsync(AgentTaskEventType.Warning)).Count(w => w.Detail.Contains("yield")).ShouldBe(1);

        // The retry: same request, the dispatch still waiting. A fresh budget yields again.
        h.Harness.Fixture.Git.AfterCommand = null;
        await h.Harness.RestartServicesAsync();
        h.Harness.Fixture.Git.Trace.Clear();
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        var retried = await h.RequestAsync();
        retried.Id.ShouldBe(interrupted.Id);
        retried.State.ShouldBe(LandRequestState.Held);
        retried.HoldReasonCode.ShouldBe(AgentTaskLandService.LeaseYieldedToDispatchCode);
        retried.HeldSince.ShouldBe(h.Clock.GetUtcNow().UtcDateTime, "a new yield episode starts a new budget");
        retried.Attempt.ShouldBe(1, "the yielding retry was never admitted");
        h.Harness.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("--ff-only"));

        // The dispatch got its gap; the retry is admitted and finishes the publication.
        waiters.Clear(waiter);
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Complete);
        (await h.RequestAsync()).Attempt.ShouldBe(2);
        await h.AssertLandedAsync();
        (await h.EventsAsync(AgentTaskEventType.Warning)).Count(w => w.Detail.Contains("yield")).ShouldBe(1);
    }

    /// <summary>
    /// Review c1c0dd1a (2): every terminal return ends the request's yield entry, not only admission
    /// and the leased pass. The request yields first (its entry exists), then a later pass returns
    /// with the request terminal: refused because the landing protocol is unavailable, already
    /// terminal when picked, or cancelled as no longer eligible. The registry is the observer:
    /// <see cref="RepositoryLeaseWaiters.FirstYield"/> answers the entry's start while one is
    /// retained, and the time asked about once it has been ended.
    /// </summary>
    [Test]
    [Arguments("protocol-unavailable")]
    [Arguments("already-terminal")]
    [Arguments("no-longer-eligible")]
    public async Task C672_a_terminal_return_ends_the_request_yield_entry(string arm)
    {
        await using var h = await StartAsync();
        var common = await h.CommonAsync();
        h.Waiters.Register(common, Guid.NewGuid(), RepositoryLeasePurposes.Dispatch, h.Clock.GetUtcNow());
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        var yielded = await h.RequestAsync();
        yielded.HoldReasonCode.ShouldBe(AgentTaskLandService.LeaseYieldedToDispatchCode);
        var firstYield = h.Clock.GetUtcNow();

        await using (var db = h.Harness.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Harness.Fixture.TaskId);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == yielded.Id);
            switch (arm)
            {
                case "protocol-unavailable":
                    task.RepoPath = null;
                    break;
                case "already-terminal":
                    request.State = LandRequestState.Canceled;
                    request.IsPending = false;
                    break;
                case "no-longer-eligible":
                    task.Status = AgentTaskStatus.Failed;
                    break;
            }
            await db.SaveChangesAsync();
        }

        h.Clock.Advance(TimeSpan.FromSeconds(5));
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Complete);
        var terminal = await h.RequestAsync();
        terminal.Id.ShouldBe(yielded.Id);
        terminal.IsPending.ShouldBeFalse();
        if (arm == "protocol-unavailable")
            (await h.EventsAsync(AgentTaskEventType.LandRefused)).ShouldHaveSingleItem()
                .Detail.ShouldContain("landing_protocol_unavailable");
        if (arm == "no-longer-eligible")
            terminal.ReconciliationError.ShouldBe("task_no_longer_eligible");

        h.Waiters.Any(common).ShouldBeTrue("the dispatch still waits; only the land's own entry ends");
        var asked = h.Clock.GetUtcNow().AddSeconds(30);
        h.Waiters.FirstYield(yielded.Id, asked).ShouldBe(asked,
            $"the {arm} return left the yield entry begun at {firstYield:O} behind");
    }

    /// <summary>
    /// Review 3488192e (3): the land monitor ages a yield from the yield itself. A request whose
    /// progress clock is old but which only just stood aside is not a LandAged warning; the same
    /// yield past LandWarningSeconds is, exactly once.
    /// </summary>
    [Test]
    public async Task C672_land_monitor_ages_a_yield_from_its_own_start()
    {
        await using var h = await StartAsync();
        var requested = await h.RequestAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(400));
        h.Waiters.Register(await h.CommonAsync(), Guid.NewGuid(), RepositoryLeasePurposes.Dispatch, h.Clock.GetUtcNow());
        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        var yielded = await h.RequestAsync();
        yielded.LastProgressAt.ShouldBe(requested.LastProgressAt, "a yield is not land progress");
        var heldSince = yielded.HeldSince.ShouldNotBeNull();
        var warning = new DelegationSettings().LandWarningSeconds;
        (heldSince - yielded.LastProgressAt).TotalSeconds.ShouldBeGreaterThan(warning);

        await h.SweepMonitorAsync(heldSince.AddSeconds(20));
        (await h.EventsAsync(AgentTaskEventType.LandAged)).ShouldBeEmpty();
        (await h.RequestAsync()).WarningAt.ShouldBeNull();

        await h.SweepMonitorAsync(heldSince.AddSeconds(warning + 1));
        var aged = (await h.EventsAsync(AgentTaskEventType.LandAged)).ShouldHaveSingleItem();
        aged.Detail.ShouldStartWith("Warning:");
        aged.Detail.ShouldContain("reason=" + AgentTaskLandService.LeaseYieldedToDispatchCode);
        (await h.RequestAsync()).ErrorAt.ShouldBeNull();
    }

    [Test]
    [Arguments("disabled")]
    [Arguments("other-repository")]
    public async Task C672_no_yield_when_disabled_or_waiting_on_another_repository(string arm)
    {
        await using var h = await StartAsync(maxSeconds: arm == "disabled" ? 0 : 90);
        var common = arm == "other-repository"
            ? Path.Combine(h.Harness.Fixture.Root, "another-repository", ".git")
            : await h.CommonAsync();
        h.Waiters.Register(common, Guid.NewGuid(), RepositoryLeasePurposes.Dispatch, h.Clock.GetUtcNow());

        (await h.Harness.RunAsync()).ShouldBe(LandRunResult.Complete);

        (await h.EventsAsync(AgentTaskEventType.Held)).ShouldBeEmpty();
        (await h.EventsAsync(AgentTaskEventType.Warning)).ShouldNotContain(w => w.Detail.Contains("yield"));
        await h.AssertLandedAsync();
    }

    private static async Task<World> StartAsync(int maxSeconds = 90, RepositoryLeaseWaiters? shared = null)
    {
        var h = new LandingSafetyHarness();
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        h.Clock = clock;
        h.LandSettings = new() { LandYieldToDispatchMaxSeconds = maxSeconds };
        if (shared is not null)
            h.ConfigureServices = services => services.AddSingleton(shared);
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.RequestAsync();
        return new World(h, clock);
    }

    private sealed class World(LandingSafetyHarness harness, FakeTimeProvider clock) : IAsyncDisposable
    {
        public LandingSafetyHarness Harness { get; } = harness;
        public FakeTimeProvider Clock { get; } = clock;
        public RepositoryLeaseWaiters Waiters => Harness.Services.GetRequiredService<RepositoryLeaseWaiters>();

        public Task<string> CommonAsync() =>
            Harness.Fixture.Git.CommonDirectoryAsync(Harness.Fixture.Repository, CancellationToken.None);

        public async Task<AgentTaskLandRequest> RequestAsync()
        {
            await using var db = Harness.CreateContext();
            return await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == Harness.Fixture.TaskId);
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(AgentTaskEventType type)
        {
            await using var db = Harness.CreateContext();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == Harness.Fixture.TaskId && e.Type == type)
                .OrderBy(e => e.At).ThenBy(e => e.Id)
                .ToListAsync();
        }

        public async Task<int> NotificationCountAsync()
        {
            await using var db = Harness.CreateContext();
            return await db.AgentTaskLandNotifications.CountAsync(
                n => n.TaskId == Harness.Fixture.TaskId && n.Kind == LandNotificationKind.Held);
        }

        public async Task AssertLandedAsync()
        {
            new AgentTaskLandingState().HasPublication((await Harness.OperationAsync()).ShouldNotBeNull()).ShouldBeTrue();
            await Harness.Fixture.AssertRemoteSourceAsync();
        }

        /// <summary>One land monitor sweep, as the hosted monitor runs it, at <paramref name="at"/>.</summary>
        public async Task SweepMonitorAsync(DateTime at)
        {
            await using var db = Harness.CreateContext();
            await new AgentTaskLandMonitorService(db,
                    new FakeTimeProvider(new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))),
                    Options.Create(new DelegationSettings()), new MockEventBus())
                .SweepAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }

    private sealed class InterruptedLand : Exception;
}
