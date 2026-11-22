using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-1931 Review b72ac3e7. Placement is the tracked generation observed as the sole member of
/// its own tree, inside the launch's remaining placement budget. A mixed or foreign first sample,
/// another generation, an exited root, a late read or a canceled launch is no placement. These
/// drive <see cref="PtyAgentRunner.ObserveRootPlacementAsync"/> with a scripted tree, a manual
/// clock and a scripted pause; nothing spawns.
/// </summary>
[Category("Unit")]
public sealed class Card1931PlacementObservationTests
{
    private const int Root = 4242;
    private const int Other = 7777;
    private static readonly DateTime Tracked = new(2026, 11, 22, 21, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Test]
    public async Task C1931_Sole_live_tracked_root_is_placed_with_observed_start()
    {
        var world = new World([Empty, Empty, Sole(Root)]);
        var placement = await world.ObserveAsync();
        placement.ShouldBe(new PtyRootPlacement(Root, Tracked));
        world.Tree.Reads.ShouldBe(3);
        world.StartReads.ShouldBe(1, "the generation is read after the sole sample");
    }

    [Test]
    public async Task C1931_Mixed_first_sample_is_no_placement()
    {
        var world = new World([Empty, Members(Root, Other), Sole(Root)]);
        (await world.ObserveAsync()).ShouldBeNull();
        world.Tree.Reads.ShouldBe(2, "a mixed first sample settles Unknown; later samples cannot say who entered first");
    }

    [Test]
    public async Task C1931_Mixed_first_sample_with_root_listed_first_is_no_placement()
    {
        var world = new World([Members(Root, Other)]);
        (await world.ObserveAsync()).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Foreign_first_member_is_no_placement()
    {
        var world = new World([Sole(Other), Sole(Root)]);
        (await world.ObserveAsync()).ShouldBeNull();
        world.Tree.Reads.ShouldBe(1);
    }

    [Test]
    public async Task C1931_Count_disagreeing_with_members_is_no_placement()
    {
        var world = new World([new PtyCustodyPopulation(2, [Root])]);
        (await world.ObserveAsync()).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Start_time_mismatch_is_no_placement()
    {
        var world = new World([Sole(Root)]) { ObservedStart = Tracked.AddTicks(1) };
        (await world.ObserveAsync()).ShouldBeNull();
        world.StartReads.ShouldBe(1);
    }

    [Test]
    public async Task C1931_Unreadable_start_time_is_no_placement()
    {
        var world = new World([Sole(Root)]) { ObservedStart = null };
        (await world.ObserveAsync()).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Untracked_generation_is_no_placement()
    {
        var world = new World([Sole(Root)]) { TrackedStart = null };
        (await world.ObserveAsync()).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Sole_sample_before_tracking_completes_is_placed_once_tracked()
    {
        var tracking = new TaskCompletionSource<DateTime?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var world = new World([Empty, Sole(Root), Members(Root, Other)]) { TrackedTask = tracking.Task };
        var observing = world.ObserveAsync();
        observing.IsCompleted.ShouldBeFalse("the sole sample waits for the durable tracking start");
        world.Tree.Reads.ShouldBe(2, "the first nonempty sample is taken before tracking returns");
        tracking.SetResult(Tracked);
        (await observing).ShouldBe(new PtyRootPlacement(Root, Tracked));
        world.Tree.Reads.ShouldBe(2);
    }

    [Test]
    public async Task C1931_Tracking_completing_after_the_deadline_is_rejected()
    {
        var tracking = new TaskCompletionSource<DateTime?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var world = new World([Sole(Root)]) { TrackedTask = tracking.Task };
        var observing = world.ObserveAsync();
        world.Clock.Advance(Bound);
        tracking.SetResult(Tracked);
        (await observing).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Cancellation_while_waiting_for_tracking_is_no_placement()
    {
        using var cts = new CancellationTokenSource();
        var tracking = new TaskCompletionSource<DateTime?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var world = new World([Sole(Root)]) { TrackedTask = tracking.Task };
        var observing = world.ObserveAsync(cts.Token);
        cts.Cancel();
        (await observing).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Exited_sole_root_is_no_placement()
    {
        var world = new World([Sole(Root)]) { Exited = true };
        (await world.ObserveAsync()).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Root_exit_while_tree_empty_stops_without_placement()
    {
        var world = new World([Empty, Sole(Root)]) { Exited = true };
        (await world.ObserveAsync()).ShouldBeNull();
        world.Tree.Reads.ShouldBe(1);
    }

    [Test]
    public async Task C1931_Read_completing_at_the_deadline_is_rejected()
    {
        var world = new World([Sole(Root)]);
        world.Tree.DuringRead = read => world.Clock.Advance(Bound);
        (await world.ObserveAsync()).ShouldBeNull();
        world.StartReads.ShouldBe(0, "a late sample is rejected before it is accepted");
    }

    [Test]
    public async Task C1931_Start_read_completing_after_the_deadline_is_rejected()
    {
        var world = new World([Sole(Root)]);
        world.DuringStartRead = () => world.Clock.Advance(Bound);
        (await world.ObserveAsync()).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Budget_counts_from_launch_start_not_from_spawn()
    {
        var world = new World([Empty, Sole(Root)]);
        world.Clock.Advance(Bound - TimeSpan.FromMilliseconds(100)); // the spawn spent the budget
        world.PauseAdvance = TimeSpan.FromMilliseconds(200);
        (await world.ObserveAsync()).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Unreadable_tree_until_deadline_is_no_placement()
    {
        var world = new World([]) { Unreadable = true, PauseAdvance = TimeSpan.FromSeconds(1) };
        (await world.ObserveAsync()).ShouldBeNull();
        world.Tree.Reads.ShouldBe(5);
    }

    [Test]
    public async Task C1931_Cancellation_during_pause_stops_without_placement()
    {
        using var cts = new CancellationTokenSource();
        var world = new World([Empty, Sole(Root)]);
        world.OnPause = token =>
        {
            cts.Cancel();
            token.IsCancellationRequested.ShouldBeTrue("the pause receives the launch token");
            return Task.FromCanceled(token);
        };
        (await world.ObserveAsync(cts.Token)).ShouldBeNull();
        world.Tree.Reads.ShouldBe(1);
    }

    [Test]
    public async Task C1931_Cancellation_after_a_completed_pause_rejects_the_next_read()
    {
        using var cts = new CancellationTokenSource();
        var world = new World([Empty, Sole(Root)]);
        world.OnPause = _ =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };
        (await world.ObserveAsync(cts.Token)).ShouldBeNull();
    }

    [Test]
    public async Task C1931_Zero_bound_makes_one_read()
    {
        var placed = new World([Sole(Root)]) { SettleBound = TimeSpan.Zero };
        (await placed.ObserveAsync()).ShouldBe(new PtyRootPlacement(Root, Tracked));
        var empty = new World([Empty, Sole(Root)]) { SettleBound = TimeSpan.Zero };
        (await empty.ObserveAsync()).ShouldBeNull();
        empty.Tree.Reads.ShouldBe(1);
    }

    private static PtyCustodyPopulation Empty => new(0, []);
    private static PtyCustodyPopulation Sole(int pid) => new(1, [pid]);
    private static PtyCustodyPopulation Members(params int[] pids) => new((uint)pids.Length, pids);

    private sealed class World(IReadOnlyList<PtyCustodyPopulation> samples)
    {
        public ScriptedTree Tree { get; } = new(samples);
        public ManualClock Clock { get; } = new();
        public DateTime? TrackedStart { get; init; } = Tracked;
        /// <summary>When set, the tracked start completes only once the test says so.</summary>
        public Task<DateTime?>? TrackedTask { get; init; }
        public DateTime? ObservedStart { get; init; } = Tracked;
        public bool Exited { get; init; }
        public bool Unreadable { init => Tree.Unreadable = value; }
        public TimeSpan SettleBound { init => Tree.Bound = value; }
        public TimeSpan PauseAdvance { get; set; } = TimeSpan.FromMilliseconds(10);
        public Func<CancellationToken, Task>? OnPause { get; set; }
        public Action? DuringStartRead { get; set; }
        public int StartReads { get; private set; }

        public Task<PtyRootPlacement?> ObserveAsync(CancellationToken ct = default)
        {
            return PtyAgentRunner.ObserveRootPlacementAsync(Tree, Root, TrackedTask ?? Task.FromResult(TrackedStart),
                pid =>
                {
                    pid.ShouldBe(Root);
                    StartReads++;
                    DuringStartRead?.Invoke();
                    return ObservedStart;
                },
                pid =>
                {
                    pid.ShouldBe(Root);
                    return Exited;
                },
                Clock, launchStarted: 0, token =>
                {
                    if (OnPause is { } pause)
                        return pause(token);
                    Clock.Advance(PauseAdvance);
                    return Task.CompletedTask;
                }, ct);
        }
    }

    private sealed class ScriptedTree(IReadOnlyList<PtyCustodyPopulation> samples) : IPtyCustodyContainment
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public TimeSpan Bound { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan PlacementSettleBound => Bound;
        public bool Unreadable { get; set; }
        public int Reads { get; private set; }
        public Action<int>? DuringRead { get; set; }
        public PtyTrackedLaunch Place(PtyTrackedLaunch launch) => launch;
        public PtyCustodyPopulation ReadActive()
        {
            var read = Reads++;
            DuringRead?.Invoke(read);
            if (Unreadable)
                throw new IOException("verification_custody_tree_unreadable");
            return read < samples.Count ? samples[read] : samples[^1];
        }
        public PtyTerminationObservation Terminate() => throw new InvalidOperationException("never terminates");
    }

    /// <summary>A monotonic clock the test moves by hand; one tick is 100 ns.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
