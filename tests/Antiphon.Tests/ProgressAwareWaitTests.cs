using Antiphon.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>CARD-0550 D-4: quiet deadline renewed by progress, bounded by the cap. Fake clock; no wall time.</summary>
[Category("Unit")]
public sealed class ProgressAwareWaitTests
{
    private const string Evidence = "owned evidence";

    /// <summary>A fake clock that also counts timer registrations: one per Task.Delay the wait loop arms.</summary>
    private sealed class CountingClock : TimeProvider
    {
        public readonly FakeTimeProvider Inner = new(new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero));
        public int Timers;
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override long GetTimestamp() => Inner.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref Timers);
            return Inner.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class Drive
    {
        public readonly CountingClock Clock = new();
        public int Polls;
        public long Progress;
        public int GrowUntilStep;
        public Func<bool> Satisfied = () => false;
        public Task Wait = Task.CompletedTask;

        public void Start(bool withProgress = true) =>
            Wait = ProgressAwareWait.UntilAsync(() =>
            {
                Interlocked.Increment(ref Polls);
                return Task.FromResult(Satisfied());
            }, Evidence, seconds: 60, progress: withProgress ? () => Volatile.Read(ref Progress) : null, capSeconds: 600, clock: Clock);

        /// <summary>
        /// One poll interval per step; progress grows by one per step while step &lt;= GrowUntilStep. After each
        /// advance the driver waits until the loop has armed its next poll timer or settled, so the clock never
        /// moves past an unarmed loop (the pooled continuation would otherwise miss a tick and hang the driver).
        /// </summary>
        public async Task<int> RunAsync(int maxSteps)
        {
            var steps = 0;
            while (!Wait.IsCompleted && steps < maxSteps)
            {
                var armed = Volatile.Read(ref Clock.Timers);
                steps++;
                if (steps <= GrowUntilStep) Volatile.Write(ref Progress, Progress + 1);
                Clock.Inner.Advance(TimeSpan.FromMilliseconds(ProgressAwareWait.PollMilliseconds));
                while (!Wait.IsCompleted && Volatile.Read(ref Clock.Timers) == armed) await Task.Yield();
            }
            return steps;
        }
    }

    [Test]
    public async Task Constant_progress_times_out_at_the_quiet_deadline()
    {
        var d = new Drive { GrowUntilStep = 0 };
        d.Start();
        (await d.RunAsync(10_000)).ShouldBe(600, "60 s of 100 ms polls with no progress");
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe("owned evidence (quiet 60s after 60.0s; progress 0)");
    }

    [Test]
    public async Task Growing_progress_renews_the_quiet_deadline_until_the_cap()
    {
        var d = new Drive { GrowUntilStep = int.MaxValue };
        d.Start();
        var steps = await d.RunAsync(10_000);
        d.Wait.IsCompleted.ShouldBeTrue("the cap bounds renewal");
        steps.ShouldBe(6000, "renewed every poll, stopped by the 600 s cap");
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe("owned evidence (quiet 60s after 600.0s; progress 6000)");
    }

    [Test]
    public async Task Progress_that_stops_times_out_one_quiet_period_after_the_last_change()
    {
        var d = new Drive { GrowUntilStep = 300 };
        d.Start();
        (await d.RunAsync(10_000)).ShouldBe(900, "last change at 30 s, quiet deadline 60 s later");
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe("owned evidence (quiet 60s after 90.0s; progress 300)");
    }

    [Test]
    public async Task A_slow_but_progressing_predicate_is_allowed_to_finish_past_the_quiet_deadline()
    {
        var d = new Drive { GrowUntilStep = int.MaxValue };
        d.Satisfied = () => Volatile.Read(ref d.Polls) >= 700;
        d.Start();
        var steps = await d.RunAsync(10_000);
        d.Wait.IsCompletedSuccessfully.ShouldBeTrue("a land that keeps issuing git commands is allowed to finish after 60 s");
        steps.ShouldBe(699, "poll 700 follows the 699th advance");
    }

    [Test]
    public async Task Without_a_progress_probe_the_wait_is_the_plain_sixty_second_deadline()
    {
        var d = new Drive { GrowUntilStep = int.MaxValue };
        d.Start(withProgress: false);
        (await d.RunAsync(10_000)).ShouldBe(600);
        var error = await Should.ThrowAsync<TimeoutException>(() => d.Wait);
        error.Message.ShouldBe(Evidence, "no probe: the message is the evidence verbatim, as before CARD-0550");
    }

    [Test]
    public async Task A_satisfied_predicate_returns_before_any_deadline()
    {
        var d = new Drive { GrowUntilStep = 0 };
        d.Satisfied = () => Volatile.Read(ref d.Polls) >= 5;
        d.Start();
        (await d.RunAsync(10_000)).ShouldBe(4);
        d.Wait.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
