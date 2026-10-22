using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class DispatcherAdmissionSchedulingTests
{
    [Test]
    public async Task C1519_freed_seat_is_admitted_while_maintenance_waits()
    {
        await using var f = await DispatcherAdmissionFixture.CreateAsync();
        await f.StartHeldAsync();
        await f.ArmSweepAsync();
        await f.FreeSeatAsync();
        // The initial timers were observed armed before entering the real watchdog.
        // Do not let an absent next timer replace the durable outcome assertion on old hosting.
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await DispatcherAdmissionFixture.ObserveAsync(f.IsDispatchedAsync);
        await f.AssertOutcomeAsync(dispatched: true);
    }

    [Test]
    public async Task C1519_full_capacity_and_repeated_wakes_preserve_single_admission()
    {
        await using var f = await DispatcherAdmissionFixture.CreateAsync();
        await f.StartHeldAsync();
        await f.ArmSweepAsync();
        await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Clock.HasPoll && f.Logger.Active == 0), "poll armed");
        for (var interval = 0; interval < 2; interval++)
        {
            var full = f.Logger.FullStarts;
            for (var second = 0; second < 4; second++)
            {
                var ready = f.Logger.ReadyEnds;
                for (var wake = 0; wake < 8; wake++) f.Waiters.SignalReady(force: true);
                await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Logger.ReadyEnds > ready
                    && f.Logger.Active == 0 && f.Clock.HasPoll), "ready pass completed and poll rearmed");
                f.Clock.Advance(TimeSpan.FromSeconds(1));
            }
            f.Clock.Advance(TimeSpan.FromSeconds(1));
            f.Waiters.SignalReady(force: true);
            await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Logger.FullStarts > full
                && f.Logger.Active == 0 && f.Clock.HasPoll), "lease wakes must not postpone full admission");
            await f.AssertOutcomeAsync(dispatched: false);
            (await f.HeldAsync()).ShouldContain("capacity", Case.Insensitive);
        }

        // Keep a real ready pass executing while both its next full deadline and another
        // ready signal become due. There must be one owner and the next pass must be full.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.HoldAdmission = hold;
        try
        {
            f.Waiters.SignalReady(force: true);
            await f.AdmissionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var starts = f.Logger.AdmissionStarts.Length;
            await f.FreeSeatAsync();
            f.Clock.Advance(TimeSpan.FromSeconds(5));
            for (var wake = 0; wake < 8; wake++) f.Waiters.SignalReady(force: true);
            hold.TrySetResult();
            await DispatcherAdmissionFixture.WaitUntilAsync(f.IsDispatchedAsync, "freed seat admitted");
            await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Logger.AdmissionStarts.Length > starts
                && f.Logger.Active == 0 && f.Clock.HasPoll), "coalesced work completed");
            f.Logger.AdmissionStarts[starts].ShouldStartWith("Runtime phase dispatcher.admission.full start");
            f.Clock.Advance(TimeSpan.FromSeconds(5));
            await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Logger.Active == 0 && f.Clock.HasPoll), "later poll completed");
            await f.AssertOutcomeAsync(dispatched: true);
            f.Logger.Maximum.ShouldBe(1, "all full and ready admissions must be serial");
        }
        finally { hold.TrySetResult(); }
    }

    [Test]
    public async Task C1519_shutdown_joins_loops_and_owned_sweep_scopes()
    {
        foreach (var cooperative in new[] { false, true })
        {
            await using var f = await DispatcherAdmissionFixture.CreateAsync();
            f.Cooperative = cooperative;
            await f.StartHeldAsync();
            await f.ArmSweepAsync();
            var sweepDb = f.SweepDb.ShouldNotBeNull("the owned watchdog scope executed its suspect query");
            f.AdmissionDbs.ShouldNotBeEmpty();
            f.AdmissionDbs.ShouldAllBe(db => !ReferenceEquals(db, sweepDb));
            if (!cooperative)
            {
                await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Clock.HasTimer(TimeSpan.FromSeconds(90))),
                    "budget plus grace timer armed");
                f.Clock.Advance(TimeSpan.FromSeconds(90));
                await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Logger.Messages.Any(m =>
                    m.Contains("delivery watchdog") && m.Contains("abandoned"))), "sweep abandoned on its own scope");
                f.Sweeps.IsRunning("delivery watchdog").ShouldBeTrue();
            }
            var stop = f.Service.StopAsync(CancellationToken.None);
            await f.SweepCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!cooperative)
            {
                await DispatcherAdmissionFixture.WaitUntilAsync(() => Task.FromResult(f.Logger.Active == 0), "admission joined");
                f.Service.ExecuteTask!.IsCompleted.ShouldBeFalse("execution must still own the abandoned scope");
                stop.IsCompleted.ShouldBeFalse("unbounded stop must wait for the execution lifetime");
                (await sweepDb.Database.CanConnectAsync()).ShouldBeTrue("scope remains usable until its operation ends");
                f.ReleaseSweep.TrySetResult();
            }
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
            await f.Service.ExecuteTask!;
            await f.Sweeps.WhenReleasedAsync("delivery watchdog");
            await Should.ThrowAsync<ObjectDisposedException>(() => sweepDb.AgentTasks.AnyAsync());
            var polls = f.Logger.FullStarts;
            var launches = f.Sink.Sessions.Count;
            f.Clock.Advance(TimeSpan.FromMinutes(2));
            f.Logger.FullStarts.ShouldBe(polls, "joined service cannot poll again");
            f.Sink.Sessions.Count.ShouldBe(launches, "joined service cannot launch again");
        }
    }
}
