using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0589 V-1 (S2, D-3/D-5). The host build-slot broker: a bounded number of build/test driver
/// leases, a FIFO queue a newcomer cannot jump, a live free-memory floor checked at grant time, and
/// a sweep that reaps a lease whose holder died, was recycled, or outlived its TTL. Nothing is
/// killed; the fake probes stand in for the process table and the host's free memory.
/// </summary>
[Category("Unit")]
public sealed class BuildSlotBrokerTests
{
    private static readonly DateTime T0 = new(2026, 9, 25, 6, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Grants_up_to_MaxConcurrent_with_occupancy_and_ttl()
    {
        var f = new Fixture(maxConcurrent: 2, ttlMinutes: 90);

        var first = f.Granted(f.Request(101, "a"));
        var second = f.Granted(f.Request(102, "b"));

        first.LeaseId.ShouldNotBeNull();
        second.LeaseId.ShouldNotBeNull();
        second.LeaseId.ShouldNotBe(first.LeaseId);
        (first.Occupied, first.Budget).ShouldBe((1, 2));
        (second.Occupied, second.Budget).ShouldBe((2, 2));
        first.Unlimited.ShouldBeFalse();
        first.ExpiresAtUtc.ShouldBe(T0.AddMinutes(90));
        f.Broker.List().Leases.Select(l => l.Label).ShouldBe(["a", "b"]);
    }

    [Test]
    public void Beyond_the_budget_a_request_is_busy_with_its_queue_position_and_retry_hint()
    {
        var f = new Fixture(maxConcurrent: 1, retryAfterMs: 1234);
        f.Granted(f.Request(101, "holder"));

        var first = f.Busy(f.Request(201, "w1"));
        var second = f.Busy(f.Request(202, "w2"));

        first.ShouldBe(new BuildSlotBusy(Occupied: 1, Budget: 1, QueuePosition: 1, RetryAfterMs: 1234));
        second.QueuePosition.ShouldBe(2);
        // A re-poll keeps its place rather than joining the back of the queue.
        f.Busy(f.Request(201, "w1")).QueuePosition.ShouldBe(1);
        f.Broker.List().Waiters.Select(w => (w.Pid, w.Position)).ShouldBe([(201, 1), (202, 2)]);
    }

    [Test]
    public void A_newcomer_is_refused_while_an_earlier_waiter_is_at_the_head()
    {
        var f = new Fixture(maxConcurrent: 1);
        var holder = f.Granted(f.Request(101, "holder"));
        f.Busy(f.Request(201, "early"));
        f.Broker.Release(holder.LeaseId!.Value).ShouldBeTrue();

        // A slot is free, but "early" has waited longest: the newcomer may not overtake it.
        var newcomer = f.Busy(f.Request(301, "late"));
        newcomer.QueuePosition.ShouldBe(2);
        newcomer.Occupied.ShouldBe(0);

        f.Granted(f.Request(201, "early")).LeaseId.ShouldNotBeNull();
        f.Busy(f.Request(301, "late")).QueuePosition.ShouldBe(1);
    }

    [Test]
    public void A_waiter_that_stops_polling_is_dropped_after_the_silence_window()
    {
        var f = new Fixture(maxConcurrent: 1, waiterSilenceMs: 60_000);
        var holder = f.Granted(f.Request(101, "holder"));
        f.Busy(f.Request(201, "gone"));
        f.Time.Advance(TimeSpan.FromSeconds(30));
        f.Busy(f.Request(301, "polling")).QueuePosition.ShouldBe(2);

        f.Time.Advance(TimeSpan.FromSeconds(31)); // "gone" silent 61 s, "polling" 31 s
        f.Broker.Sweep();
        f.Broker.List().Waiters.Select(w => w.Pid).ShouldBe([301]);

        f.Broker.Release(holder.LeaseId!.Value);
        f.Granted(f.Request(301, "polling")).LeaseId.ShouldNotBeNull();
    }

    [Test]
    public void Release_frees_the_slot_and_an_unknown_lease_is_not_released()
    {
        var f = new Fixture(maxConcurrent: 1);
        var grant = f.Granted(f.Request(101, "a"));
        f.Busy(f.Request(102, "b"));

        f.Broker.Release(grant.LeaseId!.Value).ShouldBeTrue();
        f.Broker.Release(grant.LeaseId!.Value).ShouldBeFalse("a lease is released once");
        f.Broker.Release(Guid.NewGuid()).ShouldBeFalse();

        f.Granted(f.Request(102, "b")).Occupied.ShouldBe(1);
    }

    [Test]
    public void A_dead_holder_is_reaped_on_the_next_acquire()
    {
        var f = new Fixture(maxConcurrent: 1);
        f.Granted(f.Request(101, "dies"));
        f.Liveness.Kill(101);

        var next = f.Granted(f.Request(102, "next"));

        next.Occupied.ShouldBe(1);
        f.Broker.List().Leases.Select(l => l.Pid).ShouldBe([102]);
        f.Messages.ShouldContain(m => m.Contains("dies") && m.Contains("reaped"));
    }

    [Test]
    public void A_recycled_pid_is_reaped_even_though_a_process_answers_under_it()
    {
        var f = new Fixture(maxConcurrent: 1);
        f.Granted(f.Request(101, "original"));
        // The holder exits and the OS hands pid 101 to a process started 5 minutes later.
        f.Liveness.Start(101, T0.AddMinutes(5));
        f.Time.Advance(TimeSpan.FromMinutes(6));

        f.Broker.List().Leases.Single().HolderAlive.ShouldBeFalse();
        f.Broker.Sweep().ShouldBe(1);
        f.Broker.List().Leases.ShouldBeEmpty();

        // Within the tolerance the same pid is still the holder.
        var g = new Fixture(maxConcurrent: 1);
        g.Granted(g.Request(101, "original"));
        g.Liveness.Start(101, T0.AddSeconds(90));
        g.Broker.Sweep().ShouldBe(0);
        g.Broker.List().Leases.Single().HolderAlive.ShouldBeTrue();
    }

    [Test]
    public void A_lease_past_its_ttl_is_reaped()
    {
        var f = new Fixture(maxConcurrent: 1, ttlMinutes: 90);
        f.Granted(f.Request(101, "long"));

        f.Time.Advance(TimeSpan.FromMinutes(89));
        f.Broker.Sweep().ShouldBe(0);
        f.Busy(f.Request(102, "next"));

        f.Time.Advance(TimeSpan.FromMinutes(1));
        f.Granted(f.Request(102, "next")).Occupied.ShouldBe(1);
        f.Broker.List().Leases.Select(l => l.Label).ShouldBe(["next"]);
        f.Messages.ShouldContain(m => m.Contains("long") && m.Contains("ttl"));
    }

    [Test]
    public void Below_the_memory_floor_a_free_slot_is_refused_and_admits_once_memory_returns()
    {
        var f = new Fixture(maxConcurrent: 4, floorMb: 6144, retryAfterMs: 777);
        f.Memory.AvailableBytes = 6143L * 1024 * 1024;

        f.Broker.TryAcquire(f.Request(101, "a"), out var outcome).ShouldBeFalse();
        var floor = outcome.ShouldBeOfType<BuildSlotOutcome.MemoryFloor>().Refusal;
        floor.ShouldBe(new BuildSlotMemoryFloor(AvailableMb: 6143, FloorMb: 6144, QueuePosition: 1, RetryAfterMs: 777));
        f.Broker.List().Occupied.ShouldBe(0);

        f.Memory.AvailableBytes = 6144L * 1024 * 1024;
        f.Granted(f.Request(101, "a")).Occupied.ShouldBe(1);
    }

    [Test]
    public void Disabled_answers_unlimited_with_the_configured_cpu_count_and_holds_nothing()
    {
        var f = new Fixture(maxConcurrent: 1, maxCpuCount: 7, enabled: false);

        var a = f.Granted(f.Request(101, "a"));
        var b = f.Granted(f.Request(102, "b"));

        a.Unlimited.ShouldBeTrue();
        a.LeaseId.ShouldBeNull();
        a.MaxCpuCount.ShouldBe(7);
        b.Unlimited.ShouldBeTrue();
        f.Broker.List().Leases.ShouldBeEmpty();
        f.Broker.List().Enabled.ShouldBeFalse();
    }

    [Test]
    public void Every_grant_carries_the_configured_MaxCpuCount_including_after_a_wait()
    {
        var f = new Fixture(maxConcurrent: 1, maxCpuCount: 3);
        var first = f.Granted(f.Request(101, "a"));
        f.Busy(f.Request(102, "b"));
        f.Broker.Release(first.LeaseId!.Value);
        var second = f.Granted(f.Request(102, "b"));

        first.MaxCpuCount.ShouldBe(3);
        second.MaxCpuCount.ShouldBe(3);
        f.Broker.List().MaxCpuCount.ShouldBe(3);
    }

    internal sealed class Fixture
    {
        public FakeTimeProvider Time { get; } = new(T0);
        public FakeLiveness Liveness { get; } = new();
        public FakeMemory Memory { get; } = new() { AvailableBytes = 64L * 1024 * 1024 * 1024 };
        public List<string> Messages { get; } = new();
        public BuildSlotBroker Broker { get; }

        public Fixture(int maxConcurrent = 2, int maxCpuCount = 4, int floorMb = 0, int ttlMinutes = 90,
            int retryAfterMs = 15_000, int waiterSilenceMs = 60_000, bool enabled = true)
        {
            var settings = new BuildSlotSettings
            {
                Enabled = enabled, MaxConcurrent = maxConcurrent, MaxCpuCount = maxCpuCount,
                MinAvailableMemoryMb = floorMb, LeaseTtlMinutes = ttlMinutes, RetryAfterMs = retryAfterMs,
                WaiterSilenceMs = waiterSilenceMs,
            };
            Broker = new BuildSlotBroker(Options.Create(settings), Liveness, Memory, Time, new ListLogger<BuildSlotBroker>(Messages));
        }

        public BuildSlotRequest Request(int pid, string label)
        {
            if (!Liveness.Knows(pid))
                Liveness.Start(pid, T0);
            return new BuildSlotRequest(pid, T0, label, "session-" + pid, "task-" + pid);
        }

        public BuildSlotGrant Granted(BuildSlotRequest request)
        {
            Broker.TryAcquire(request, out var outcome).ShouldBeTrue(outcome.ToString());
            return outcome.ShouldBeOfType<BuildSlotOutcome.Granted>().Grant;
        }

        public BuildSlotBusy Busy(BuildSlotRequest request)
        {
            Broker.TryAcquire(request, out var outcome).ShouldBeFalse(outcome.ToString());
            return outcome.ShouldBeOfType<BuildSlotOutcome.Busy>().Refusal;
        }
    }

    /// <summary>Mirrors <see cref="SystemProcessLivenessProbe"/>: a pid started more than two minutes after the recorded start is a different process.</summary>
    internal sealed class FakeLiveness : IProcessLivenessProbe
    {
        private readonly Dictionary<int, DateTime> _started = new();
        public void Start(int pid, DateTime startUtc) => _started[pid] = startUtc;
        public void Kill(int pid) => _started.Remove(pid);
        public bool Knows(int pid) => _started.ContainsKey(pid);
        public bool IsAlive(int pid, DateTime startedAt) =>
            _started.TryGetValue(pid, out var actual) && actual <= startedAt + TimeSpan.FromMinutes(2);
        public string? TryGetProcessName(int pid) => _started.ContainsKey(pid) ? "pwsh" : null;
        public DateTime? TryGetStartTimeUtc(int pid) => _started.TryGetValue(pid, out var actual) ? actual : null;
    }

    internal sealed class FakeMemory : IHostMemoryProbe
    {
        public long? AvailableBytes { get; set; }
    }
}
