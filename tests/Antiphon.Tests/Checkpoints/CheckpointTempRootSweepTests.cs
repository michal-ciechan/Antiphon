using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointTempRootSweepTests : CheckpointTestBase
{
    [Test]
    public async Task two_hundred_concurrent_allocations_ignore_the_sweep_gate()
    {
        var sandbox = TempDir();
        var gatePath = Path.Combine(sandbox, ".checkpoint-temp-coordinator.lock");
        using var gate = new FileStream(gatePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var allocations = Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Factory.StartNew(
            () => Candidate(sandbox), CancellationToken.None, TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));
        var completedWhileGateHeld = await Task.WhenAny(allocations, Task.Delay(TimeSpan.FromSeconds(20)))
            == allocations;
        gate.Dispose();
        var roots = await allocations;
        roots.Length.ShouldBe(200);
        roots.ShouldAllBe(root => Directory.Exists(root));
        completedWhileGateHeld.ShouldBeTrue("registration must not queue behind the sweep gate");
    }

    [Test]
    public void grace_is_additional_to_dead_ownership()
    {
        var sandbox = TempDir();
        var created = DateTimeOffset.Parse("2026-09-29T00:00:00Z");
        var root = Candidate(sandbox, createdAt: created);
        var now = created.AddMinutes(10).AddTicks(-1);
        var sweep = Sweep(sandbox, clock: () => now, grace: TimeSpan.FromMinutes(10));
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        Directory.Exists(root).ShouldBeTrue();
        now = created.AddMinutes(10);
        sweep.SweepOnce().CompletedRoots.ShouldBe(1);
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public void sweep_interval_is_shared_across_hosts()
    {
        var sandbox = TempDir();
        var now = DateTimeOffset.UtcNow;
        Candidate(sandbox);
        var first = Sweep(sandbox, clock: () => now, interval: TimeSpan.FromMinutes(5));
        first.SweepOnce().CompletedRoots.ShouldBe(1);
        var secondRoot = Candidate(sandbox);
        var second = Sweep(sandbox, clock: () => now.AddMinutes(1), interval: TimeSpan.FromMinutes(5));
        using var gate = new FileStream(Path.Combine(sandbox, ".checkpoint-temp-coordinator.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        second.SweepOnce().Examined.ShouldBe(0);
        Directory.Exists(secondRoot).ShouldBeTrue();
    }

    [Test]
    public void root_delete_count_is_bounded()
    {
        var sandbox = TempDir();
        var roots = Enumerable.Range(0, 3).Select(_ => Candidate(sandbox)).ToArray();
        var sweep = Sweep(sandbox, maxRoots: 2);
        sweep.SweepOnce().CompletedRoots.ShouldBe(2);
        roots.Count(Directory.Exists).ShouldBe(1);
    }

    [Test]
    public void actual_deleted_bytes_are_bounded()
    {
        var sandbox = TempDir();
        var root = Candidate(sandbox, payloadBytes: 16 * 1024);
        var sweep = Sweep(sandbox, maxBytes: 8 * 1024);
        var receipt = sweep.SweepOnce();
        receipt.ReclaimedBytes.ShouldBeLessThanOrEqualTo(8 * 1024);
        Directory.Exists(root).ShouldBeTrue();
        File.Exists(Path.Combine(root, TestRootGuard.MarkerName)).ShouldBeTrue();
        new FileInfo(Path.Combine(root, "payload.bin")).Length.ShouldBe(8 * 1024);
    }

    [Test]
    public void direct_entry_scan_is_bounded()
    {
        var sandbox = TempDir();
        var roots = Enumerable.Range(0, 3).Select(_ => Candidate(sandbox)).ToArray();
        var sweep = Sweep(sandbox, maxDirect: 2);
        sweep.SweepOnce().Examined.ShouldBe(2);
        roots.Count(Directory.Exists).ShouldBe(1);
        sweep.SweepOnce().CompletedRoots.ShouldBe(1);
    }

    [Test]
    public void descendant_scan_is_bounded()
    {
        var sandbox = TempDir();
        var root = Candidate(sandbox);
        for (var i = 0; i < 4; i++) File.WriteAllText(Path.Combine(root, "f" + i), "x");
        var sweep = Sweep(sandbox, maxDescendants: 2);
        var receipt = sweep.SweepOnce();
        receipt.CompletedRoots.ShouldBe(0);
        receipt.Skips["inventory-incomplete-or-linked"].ShouldBe(1);
        Directory.Exists(root).ShouldBeTrue();
    }

    [Test]
    public void monotonic_deadline_stops_new_operations()
    {
        var sandbox = TempDir();
        var root = Candidate(sandbox);
        var probe = new CountingProbe();
        var sweep = new CheckpointTempRootSweep(sandbox, probe,
            new CheckpointSweepOptions { Grace = TimeSpan.Zero, Interval = TimeSpan.Zero,
                MaxDuration = TimeSpan.Zero });
        var receipt = sweep.SweepOnce();
        receipt.Examined.ShouldBe(0);
        receipt.BudgetExhausted.ShouldBeTrue();
        probe.Calls.ShouldBe(0);
        Directory.Exists(root).ShouldBeTrue();
    }

    [Test]
    public void resumed_deletion_rechecks_every_veto()
    {
        var sandbox = TempDir();
        var root = Candidate(sandbox, payloadBytes: 16 * 1024);
        var sweep = Sweep(sandbox, maxBytes: 8 * 1024);
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        var payload = Path.Combine(root, "payload.bin");
        var remaining = new FileInfo(payload).Length;
        var marker = TestRootGuard.Read(root)!;
        marker.Owner = new ProcessIdentityProbe().Current();
        TestRootGuard.Write(root, marker);
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        new FileInfo(payload).Length.ShouldBe(remaining);
        Directory.Exists(root).ShouldBeTrue();
    }

    [Test]
    public void marker_survives_partial_deletion()
    {
        var sandbox = TempDir();
        var root = Candidate(sandbox, payloadBytes: 16 * 1024);
        var sweep = Sweep(sandbox, maxBytes: 8 * 1024);
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        TestRootGuard.Read(root)!.State.ShouldBe("deleting");
        sweep.SweepOnce().CompletedRoots.ShouldBe(1);
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public void cursor_prevents_new_arrival_starvation()
    {
        var sandbox = TempDir();
        var old = Enumerable.Range(0, 5).Select(_ => Candidate(sandbox)).ToArray();
        var sweep = Sweep(sandbox, maxDirect: 2);
        sweep.SweepOnce().CompletedRoots.ShouldBe(2);
        Candidate(sandbox);
        Candidate(sandbox);
        sweep.SweepOnce().CompletedRoots.ShouldBe(2);
        sweep.SweepOnce().CompletedRoots.ShouldBeGreaterThanOrEqualTo(1);
        old.ShouldAllBe(path => !Directory.Exists(path));
    }

    [Test]
    public void disposed_roots_do_not_consume_the_next_sweep_budget()
    {
        var sandbox = TempDir();
        var index = new CheckpointTempRootSweep(sandbox);
        var legacyEntries = new List<string>();
        for (var i = 0; i < 700; i++)
        {
            var disposed = Candidate(sandbox);
            Directory.Delete(disposed, recursive: true);
            index.Unregister(disposed);
            legacyEntries.Add(JsonSerializer.Serialize(disposed));
        }
        var orphan = Candidate(sandbox);
        index.Unregister(orphan);
        legacyEntries.Add(JsonSerializer.Serialize(orphan));
        File.WriteAllLines(Path.Combine(sandbox, ".checkpoint-temp-roots.jsonl"), legacyEntries);
        var receipt = Sweep(sandbox, maxDirect: 1).SweepOnce();
        receipt.Examined.ShouldBe(1);
        receipt.CompletedRoots.ShouldBe(1);
        Directory.Exists(orphan).ShouldBeFalse();
    }

    [Test]
    public void eligible_backlog_drains_without_touching_exclusions()
    {
        var sandbox = TempDir();
        var eligible = Enumerable.Range(0, 4).Select(_ => Candidate(sandbox)).ToArray();
        var live = Candidate(sandbox, alive: true);
        var sweep = Sweep(sandbox, maxRoots: 2);
        for (var i = 0; i < 4 && eligible.Any(Directory.Exists); i++) sweep.SweepOnce();
        eligible.ShouldAllBe(path => !Directory.Exists(path));
        Directory.Exists(live).ShouldBeTrue();
        TestRootGuard.Read(live).ShouldNotBeNull();
    }

    private static CheckpointTempRootSweep Sweep(string sandbox, Func<DateTimeOffset>? clock = null,
        TimeSpan? grace = null, TimeSpan? interval = null, int maxRoots = 16, long maxBytes = 256L * 1024 * 1024,
        int maxDirect = 512, int maxDescendants = 10000) => new(sandbox,
        options: new CheckpointSweepOptions
        {
            Grace = grace ?? TimeSpan.Zero, Interval = interval ?? TimeSpan.Zero,
            MaxRoots = maxRoots, MaxBytes = maxBytes, MaxDirectEntries = maxDirect,
            MaxDescendantEntries = maxDescendants, MaxDuration = TimeSpan.FromSeconds(5),
        }, clock: clock);

    private static string Candidate(string sandbox, int payloadBytes = 0, DateTimeOffset? createdAt = null,
        bool alive = false)
    {
        var root = Path.Combine(sandbox, "c723-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(root);
        var current = new ProcessIdentityProbe().Current();
        TestRootGuard.Write(root, new CheckpointRootMarker
        {
            RootId = Path.GetFileName(root)[5..], RootPath = Path.GetFullPath(root),
            AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "sweep-unit",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddHours(-1),
            Owner = alive ? current : current with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
        if (payloadBytes > 0) File.WriteAllBytes(Path.Combine(root, "payload.bin"), new byte[payloadBytes]);
        new CheckpointTempRootSweep(sandbox).Register(root);
        return root;
    }

    private sealed class CountingProbe : ProcessIdentityProbe
    {
        public int Calls { get; private set; }
        public override ProcessObservation Observe(ProcessIdentity? expected)
        { Calls++; return base.Observe(expected); }
    }
}
