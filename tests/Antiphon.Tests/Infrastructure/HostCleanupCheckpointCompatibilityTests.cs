using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointCompatibilityTests
{
    [Test]
    public async Task unfinished_registered_work_retains_roots(CancellationToken cancellationToken)
    {
        var f = new HostCleanupCheckpointFixture();
        using var timing = new CheckpointTimingHarness(nameof(unfinished_registered_work_retains_roots), "two-roots", cancellationToken);
        var clock = f.Clock;
        var scope = new CheckpointTestScope(f.Context, f.Parent, cleanupClock: clock);
        var roots = new[] { scope.TempDir(), scope.TempDir() };
        foreach (var root in roots) f.Io.Put(Path.Combine(root, "payload"), "keep");
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Register(held.Task);
        var disposal = scope.DisposeAsync().AsTask();
        try
        {
            clock.Events.Any(e => e.Action == "create" && e.DueTime == TimeSpan.FromSeconds(10)).ShouldBeTrue();
            clock.Advance(TimeSpan.FromSeconds(10));
            var failure = await Should.ThrowAsync<IOException>(() => disposal.WaitAsync(timing.Token));
            failure.Message.ShouldContain("unfinished registered work");
            foreach (var root in roots)
            {
                f.Io.Text(Path.Combine(root, "payload")).ShouldBe("keep", "unfinished-work-roots-retained");
                f.Io.FileExists(Path.Combine(root, CheckpointTestScope.MarkerName)).ShouldBeTrue("unfinished-work-roots-retained");
                f.Io.FileExists(IndexPath(f.Parent, root)).ShouldBeTrue("unfinished-work-roots-retained");
            }
        }
        finally
        {
            held.TrySetResult();
            using var cleanup = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget);
            await held.Task.WaitAsync(cleanup.Token);
            try { await disposal.WaitAsync(cleanup.Token); }
            catch (IOException) { }
            // A second safe teardown removes only the roots this scope owns.
            await scope.DisposeAsync();
        }
    }

    [Test]
    public async Task completed_faulted_work_reports_failure_and_deletes_roots()
    {
        var f = new HostCleanupCheckpointFixture();
        var scope = new CheckpointTestScope(f.Context, f.Parent);
        var roots = new[] { scope.TempDir(), scope.TempDir() };
        scope.Register(Task.FromException(new IOException("synthetic-registered-fault")));
        var failure = await Should.ThrowAsync<IOException>(async () => await scope.DisposeAsync(), "registered-fault-reported");
        failure.Message.Contains("synthetic-registered-fault", StringComparison.Ordinal).ShouldBeTrue("registered-fault-reported");
        foreach (var root in roots)
        {
            f.Io.DirectoryExists(root).ShouldBeFalse("completed-work-roots-deleted");
            f.Io.FileExists(IndexPath(f.Parent, root)).ShouldBeFalse("completed-work-roots-deleted");
        }
    }

    [Test]
    public void allocation_requires_an_owner()
    {
        Should.Throw<InvalidOperationException>(() => new UnopenedProbe().Allocate());
    }

    [Test]
    public async Task root_is_registered_before_return()
    {
        var f = new HostCleanupCheckpointFixture();
        var observed = false;
        await using var scope = new CheckpointTestScope(f.Context, f.Parent, beforeReturn: (root, roots) =>
            observed = roots.Contains(root)
                && f.Io.FileExists(Path.Combine(root, CheckpointTestScope.MarkerName))
                && f.Io.FileExists(IndexPath(f.Parent, root)));
        var path = scope.TempDir();
        observed.ShouldBeTrue();
        f.Io.FileExists(Path.Combine(path, CheckpointTestScope.MarkerName)).ShouldBeTrue();
    }

    [Test]
    public async Task sealed_scope_awaits_registered_work()
    {
        var f = new HostCleanupCheckpointFixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scope = new CheckpointTestScope(f.Context, f.Parent);
        var root = scope.TempDir();
        scope.Register(gate.Task);
        var disposal = scope.DisposeAsync().AsTask();
        f.Io.DirectoryExists(root).ShouldBeTrue();
        disposal.IsCompleted.ShouldBeFalse();
        gate.SetResult();
        await disposal;
        f.Io.DirectoryExists(root).ShouldBeFalse();
        f.Io.FileExists(Path.Combine(f.Parent, ".checkpoint-temp-roots",
            Path.GetFileName(root) + ".json")).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => scope.TempDir());
    }

    [Test]
    public async Task live_child_prevents_scope_deletion()
    {
        var f = new HostCleanupCheckpointFixture();
        var probe = new UnknownProbe(f.Context);
        var scope = new CheckpointTestScope(f.Context, f.Parent, probe);
        var root = scope.TempDir();
        scope.Register(new ProcessIdentityProbe(f.Context).Current());
        try
        {
            var failure = await Should.ThrowAsync<IOException>(async () => await scope.DisposeAsync());
            failure.Message.ShouldContain("identity-unknown");
            f.Io.DirectoryExists(root).ShouldBeTrue();
            f.Io.FileExists(IndexPath(f.Parent, root)).ShouldBeTrue("retained-root-stays-indexed");
        }
        finally
        {
            probe.Verdict = ProcessVerdict.Dead;
            await scope.DisposeAsync();
            f.Io.DirectoryExists(root).ShouldBeFalse("released-after-dead-verdict");
            f.Io.FileExists(IndexPath(f.Parent, root)).ShouldBeFalse("released-root-unregistered");
        }
    }

    [Test]
    public async Task canceled_test_gets_a_fresh_cleanup_token()
    {
        var f = new HostCleanupCheckpointFixture();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var scope = new CheckpointTestScope(f.Context, f.Parent);
        var root = scope.TempDir();
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Register(work.Task);
        var disposal = scope.DisposeAsync().AsTask();
        f.Clock.Events.ShouldContain(e => e.Action == "create" && e.DueTime == TimeSpan.FromSeconds(10),
            "C987.canceled_test_gets_a_fresh_cleanup_token.fresh.timer");
        disposal.IsCompleted.ShouldBeFalse("C987.canceled_test_gets_a_fresh_cleanup_token.fresh.join");
        work.SetResult();
        await disposal;
        canceled.IsCancellationRequested.ShouldBeTrue();
        f.Io.DirectoryExists(root).ShouldBeFalse();
        f.Clock.TimerStates.Values.ShouldAllBe(state => state == "dispose");
    }

    [Test]
    public async Task teardown_preserves_failure_and_attempts_other_roots()
    {
        var f = new HostCleanupCheckpointFixture();
        string? denied = null;
        var deny = true;
        var scope = new CheckpointTestScope(f.Context, f.Parent, beforeDelete: root =>
        {
            if (deny && root == denied) throw new IOException("delete-denied");
        });
        denied = scope.TempDir();
        var other = scope.TempDir();
        var failure = await Should.ThrowAsync<IOException>(async () =>
            await scope.DisposeAsync(new InvalidOperationException("original-assertion")));
        failure.Message.ShouldContain("original-assertion");
        failure.Message.ShouldContain("delete-denied");
        f.Io.DirectoryExists(denied).ShouldBeTrue();
        f.Io.DirectoryExists(other).ShouldBeFalse();
        f.Io.FileExists(IndexPath(f.Parent, denied)).ShouldBeTrue("retained-root-stays-indexed");
        f.Io.FileExists(IndexPath(f.Parent, other)).ShouldBeFalse("deleted-root-unregistered");
        deny = false;
        await scope.DisposeAsync();
        f.Io.DirectoryExists(denied).ShouldBeFalse("denied-root-released");
        f.Io.FileExists(IndexPath(f.Parent, denied)).ShouldBeFalse("denied-root-unregistered");

        // Preserve the native Windows enumeration's case-insensitive journal matching.
        var windows = new HostCleanupCheckpointFixture(Antiphon.HostCleanup.Checkpoints.CheckpointPlatform.Windows);
        var caseScope = new CheckpointTestScope(windows.Context, windows.Parent);
        var retained = caseScope.TempDir();
        var run = Path.Combine(retained, "run");
        windows.Io.CreateDirectory(run);
        var owner = new ProcessIdentityProbe(windows.Context).Current();
        RunOwnershipStore.Write(run, new()
        {
            RunId = "run", RunDirectory = run, Starter = owner,
            Launched = owner with { Host = "foreign-host" }, Phase = "launched",
        }, windows.Context);
        var journal = Path.Combine(run, RunOwnershipStore.FileName);
        var bytes = windows.Io.Nodes[journal].Bytes.ToArray();
        windows.Io.Nodes.Remove(journal);
        windows.Io.Put(Path.Combine(run, "OWNERSHIP.JSON"), bytes);
        windows.Io.Put(Path.Combine(run, "sentinel"), "keep");
        windows.Io.Events.Clear();
        var custodyFailure = await Should.ThrowAsync<IOException>(async () => await caseScope.DisposeAsync(),
            "C987.teardown_preserves_failure_and_attempts_other_roots.windows-journal.refusal");
        custodyFailure.Message.ShouldContain("identity-foreign", Case.Sensitive,
            "C987.teardown_preserves_failure_and_attempts_other_roots.windows-journal.owner");
        windows.Io.Count("open-read", journal).ShouldBeGreaterThan(0,
            "C987.teardown_preserves_failure_and_attempts_other_roots.windows-journal.reached");
        windows.Io.DirectoryExists(retained).ShouldBeTrue(
            "C987.teardown_preserves_failure_and_attempts_other_roots.windows-journal.retained");
        windows.Io.Text(Path.Combine(run, "sentinel")).ShouldBe("keep",
            "C987.teardown_preserves_failure_and_attempts_other_roots.windows-journal.sentinel");
    }

    [Test]
    public void marker_failure_rolls_back_empty_root()
    {
        var f = new HostCleanupCheckpointFixture();
        string? root = null;
        var scope = new CheckpointTestScope(f.Context, f.Parent, beforeMarker: path =>
        {
            root = path;
            throw new IOException("marker-write-failed");
        });
        Should.Throw<IOException>(() => scope.TempDir()).Message.ShouldContain("marker-write-failed");
        root.ShouldNotBeNull();
        f.Io.DirectoryExists(root).ShouldBeFalse();
    }

    [Test]
    public async Task parallel_scopes_have_distinct_attempt_ownership()
    {
        var f = new HostCleanupCheckpointFixture();
        await using var first = new CheckpointTestScope(f.Context, f.Parent);
        await using var second = new CheckpointTestScope(f.Context, f.Parent);
        var a = first.TempDir();
        var b = second.TempDir();
        a.ShouldNotBe(b);
        var markerA = JsonSerializer.Deserialize<CheckpointRootMarker>(f.Io.Text(Path.Combine(a, CheckpointTestScope.MarkerName)))!;
        var markerB = JsonSerializer.Deserialize<CheckpointRootMarker>(f.Io.Text(Path.Combine(b, CheckpointTestScope.MarkerName)))!;
        markerA.AttemptId.ShouldNotBe(markerB.AttemptId);
        markerA.RootId.ShouldNotBe(markerB.RootId);
    }

    [Test]
    public async Task owned_disposal_does_not_require_dead_test_host()
    {
        var f = new HostCleanupCheckpointFixture();
        var scope = new CheckpointTestScope(f.Context, f.Parent);
        var root = scope.TempDir();
        new ProcessIdentityProbe(f.Context).Observe(new ProcessIdentityProbe(f.Context).Current()).Verdict.ShouldBe(ProcessVerdict.AliveSame);
        await scope.DisposeAsync();
        f.Io.DirectoryExists(root).ShouldBeFalse();
    }


    private sealed class UnopenedProbe : CheckpointTestBase
    {
        public string Allocate() => TempDir();
    }

    private sealed class UnknownProbe(Antiphon.HostCleanup.Checkpoints.CheckpointCleanupContext context) : ProcessIdentityProbe(context)
    {
        public ProcessVerdict Verdict = ProcessVerdict.Unknown;
        public override ProcessObservation Observe(ProcessIdentity? expected) =>
            new(Verdict, Verdict == ProcessVerdict.Dead ? "identity-dead" : "identity-unknown");
    }
    [Test]
    public void live_roots_are_skipped_without_taking_the_root_lock() =>
        AssertRegistrationDuringObservation(ProcessVerdict.AliveSame, "live-preflight-registers");

    [Test]
    [Arguments(ProcessVerdict.Unknown)]
    [Arguments(ProcessVerdict.ReusedPid)]
    public void uncertain_roots_are_skipped_without_taking_the_root_lock(ProcessVerdict verdict) =>
        AssertRegistrationDuringObservation(verdict, "uncertain-preflight-registers");

    private void AssertRegistrationDuringObservation(ProcessVerdict verdict, string label)
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox, payloadBytes: 16, alive: true);
        var attempts = new List<(string Run, Exception? Failure)>();
        var probe = new CallbackProbe(f.Context, _ =>
        {
            // Every observation attempts real registration, including a second
            // observation under the lock if preflight wrongly falls through.
            var run = Path.Combine(root, "run-" + attempts.Count);
            f.Io.CreateDirectory(run);
            Exception? failure = null;
            try { TestRootGuard.RegisterRun(run, f.Context); }
            catch (IOException ex) { failure = ex; }
            attempts.Add((run, failure));
            return new(verdict, "scripted-" + verdict);
        });
        var receipt = Sweep(f, sandbox, probe: probe).SweepOnce();
        attempts.ShouldNotBeEmpty(label);
        foreach (var (run, failure) in attempts)
        {
            failure.ShouldBeNull(label);
            var journal = f.Io.Text(Path.Combine(root, TestRootGuard.RunsName)).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<string>(line)).ToArray();
            journal.ShouldContain(Path.GetFullPath(run), label);
            f.Io.DirectoryExists(run).ShouldBeTrue(label);
        }
        receipt.CompletedRoots.ShouldBe(0, label);
        receipt.Skips["scripted-" + verdict].ShouldBe(1, label);
        f.Io.Nodes[Path.Combine(root, "payload.bin")].Bytes.ShouldBe(new byte[16], label);
        TestRootGuard.Read(root, f.Context).ShouldNotBeNull(label);
        f.Io.FileExists(IndexPath(sandbox, root)).ShouldBeTrue(label);
    }

    [Test]
    public void young_roots_are_skipped_without_taking_the_root_lock()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var now = f.Clock.GetUtcNow();
        var root = Candidate(f, sandbox, createdAt: now);
        var receipt = Sweep(f, sandbox, clock: () => now, grace: TimeSpan.FromMinutes(10)).SweepOnce();
        f.Io.FileExists(Path.Combine(root, TestRootGuard.LockName)).ShouldBeFalse("young-preflight-no-lock");
        receipt.Skips["grace"].ShouldBe(1);
        TestRootGuard.Read(root, f.Context).ShouldNotBeNull();
    }

    [Test]
    public void root_lock_is_still_required_for_dead_candidates()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox, payloadBytes: 16);
        using var gate = TestRootGuard.TryLock(f.Context, root);
        gate.ShouldNotBeNull();
        var receipt = Sweep(f, sandbox).SweepOnce();
        receipt.CompletedRoots.ShouldBe(0, "root-busy-retains-payload");
        receipt.Skips["root-busy"].ShouldBe(1);
        f.Io.Nodes[Path.Combine(root, "payload.bin")].Bytes.ShouldBe(new byte[16], "root-busy-retains-payload");
        TestRootGuard.Read(root, f.Context).ShouldNotBeNull();
    }

    [Test]
    [Arguments("live")]
    [Arguments("unknown")]
    [Arguments("reused")]
    [Arguments("grace")]
    [Arguments("marker")]
    [Arguments("inventory")]
    public void eligibility_is_rechecked_after_the_root_lock(string change)
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var now = f.Clock.GetUtcNow();
        var root = Candidate(f, sandbox, payloadBytes: 16, createdAt: now.AddHours(-1));
        var marker = TestRootGuard.Read(root, f.Context)!;
        var changed = false;
        var probe = new CallbackProbe(f.Context, _ =>
        {
            if (!changed)
            {
                changed = true;
                switch (change)
                {
                    case "grace": marker.CreatedAt = now; TestRootGuard.Write(root, marker, f.Context); break;
                    case "marker": marker.RootId = Guid.NewGuid().ToString("N"); TestRootGuard.Write(root, marker, f.Context); break;
                    case "inventory":
                        for (var i = 0; i < 9; i++) f.Io.Put(Path.Combine(root, "extra-" + i), "keep");
                        break;
                }
                return new(ProcessVerdict.Dead, "scripted-dead");
            }
            var verdict = change switch
            {
                "live" => ProcessVerdict.AliveSame,
                "unknown" => ProcessVerdict.Unknown,
                "reused" => ProcessVerdict.ReusedPid,
                _ => ProcessVerdict.Dead,
            };
            return new(verdict, "scripted-" + verdict);
        });
        var receipt = Sweep(f, sandbox, clock: () => now, grace: TimeSpan.FromMinutes(10),
            maxDescendants: 8, probe: probe).SweepOnce();
        changed.ShouldBeTrue();
        var label = change is "live" or "unknown" or "reused" ? "locked-owner-retained" : "locked-" + change + "-retained";
        receipt.CompletedRoots.ShouldBe(0, label);
        f.Io.Nodes[Path.Combine(root, "payload.bin")].Bytes.ShouldBe(new byte[16], label);
        f.Io.FileExists(Path.Combine(root, TestRootGuard.MarkerName)).ShouldBeTrue(label);
        f.Io.FileExists(IndexPath(sandbox, root)).ShouldBeTrue(label);
        var reason = change switch
        {
            "grace" => "grace", "marker" => "marker-invalid", "inventory" => "inventory-incomplete-or-linked",
            "live" => "scripted-AliveSame", "unknown" => "scripted-Unknown", _ => "scripted-ReusedPid",
        };
        receipt.Skips[reason].ShouldBe(1, label);
    }

    [Test]
    public async Task two_hundred_concurrent_allocations_ignore_the_sweep_gate()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var gatePath = Path.Combine(sandbox, ".checkpoint-temp-coordinator.lock");
        using var gate = f.Io.Open(gatePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var allocations = Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Factory.StartNew(
            () => Candidate(f, sandbox), CancellationToken.None, TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));
        var completedWhileGateHeld = await Task.WhenAny(allocations, Task.Delay(TimeSpan.FromSeconds(20)))
            == allocations;
        gate.Dispose();
        var roots = await allocations;
        roots.Length.ShouldBe(200);
        roots.ShouldAllBe(root => f.Io.DirectoryExists(root));
        completedWhileGateHeld.ShouldBeTrue("registration must not queue behind the sweep gate");
    }

    [Test]
    public void grace_is_additional_to_dead_ownership()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var created = DateTimeOffset.Parse("2026-09-29T00:00:00Z");
        var root = Candidate(f, sandbox, createdAt: created);
        var now = created.AddMinutes(10).AddTicks(-1);
        var sweep = Sweep(f, sandbox, clock: () => now, grace: TimeSpan.FromMinutes(10));
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        f.Io.DirectoryExists(root).ShouldBeTrue();
        now = created.AddMinutes(10);
        sweep.SweepOnce().CompletedRoots.ShouldBe(1);
        f.Io.DirectoryExists(root).ShouldBeFalse();
    }

    [Test]
    public void sweep_interval_is_shared_across_hosts()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var now = f.Clock.GetUtcNow();
        Candidate(f, sandbox);
        var first = Sweep(f, sandbox, clock: () => now, interval: TimeSpan.FromMinutes(5));
        first.SweepOnce().CompletedRoots.ShouldBe(1);
        var secondRoot = Candidate(f, sandbox);
        var second = Sweep(f, sandbox, clock: () => now.AddMinutes(1), interval: TimeSpan.FromMinutes(5));
        using var gate = f.Io.Open(Path.Combine(sandbox, ".checkpoint-temp-coordinator.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var receipt = second.SweepOnce();
        receipt.Examined.ShouldBe(0);
        receipt.Skips.ShouldContainKey("interval");
        receipt.Skips.ShouldNotContainKey("coordinator-busy");
        f.Io.DirectoryExists(secondRoot).ShouldBeTrue();
    }

    [Test]
    public void cursor_write_failure_does_not_fail_the_sweep()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox);
        // A directory at the cursor path makes replacement fail on Linux too.
        var cursor = Path.Combine(sandbox, ".checkpoint-temp-cursor.json");
        f.Io.Before = (op, path) => { if (op == "move" && path == cursor) throw new IOException("cursor replacement denied"); };
        var receipt = Sweep(f, sandbox).SweepOnce();
        receipt.CompletedRoots.ShouldBe(1);
        f.Io.DirectoryExists(root).ShouldBeFalse();
    }

    [Test]
    public void sweep_removes_only_stale_index_temp_files()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox, alive: true);
        var index = Path.Combine(sandbox, ".checkpoint-temp-roots");
        var prefix = Path.GetFileName(root) + ".json.";
        var stale = Path.Combine(index, prefix + Guid.NewGuid().ToString("N") + ".tmp");
        var fresh = Path.Combine(index, prefix + Guid.NewGuid().ToString("N") + ".tmp");
        f.Io.Put(stale, "pending");
        f.Io.Put(fresh, "pending");
        f.Io.Nodes[stale].Stat = f.Io.Nodes[stale].Stat with { Modified = (f.Clock.GetUtcNow().AddMinutes(-11).UtcTicks - DateTime.UnixEpoch.Ticks) * 100 };
        Sweep(f, sandbox).SweepOnce();
        f.Io.FileExists(stale).ShouldBeFalse();
        f.Io.FileExists(fresh).ShouldBeTrue();
    }

    [Test]
    public void root_delete_count_is_bounded()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var roots = Enumerable.Range(0, 3).Select(_ => Candidate(f, sandbox)).ToArray();
        var sweep = Sweep(f, sandbox, maxRoots: 2);
        sweep.SweepOnce().CompletedRoots.ShouldBe(2);
        roots.Count(f.Io.DirectoryExists).ShouldBe(1);
    }

    [Test]
    public void actual_deleted_bytes_are_bounded()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox, payloadBytes: 16 * 1024);
        var sweep = Sweep(f, sandbox, maxBytes: 8 * 1024);
        var receipt = sweep.SweepOnce();
        receipt.ReclaimedBytes.ShouldBeLessThanOrEqualTo(8 * 1024);
        f.Io.DirectoryExists(root).ShouldBeTrue();
        f.Io.FileExists(Path.Combine(root, TestRootGuard.MarkerName)).ShouldBeTrue();
        f.Io.Length(Path.Combine(root, "payload.bin")).ShouldBe(8 * 1024);
    }

    [Test]
    public void direct_entry_scan_is_bounded()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var roots = Enumerable.Range(0, 3).Select(_ => Candidate(f, sandbox)).ToArray();
        var sweep = Sweep(f, sandbox, maxDirect: 2);
        sweep.SweepOnce().Examined.ShouldBe(2);
        roots.Count(f.Io.DirectoryExists).ShouldBe(1);
        sweep.SweepOnce().CompletedRoots.ShouldBe(1);
    }

    [Test]
    public void descendant_scan_is_bounded()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox);
        for (var i = 0; i < 4; i++) f.Io.Put(Path.Combine(root, "f" + i), "x");
        var sweep = Sweep(f, sandbox, maxDescendants: 2);
        var receipt = sweep.SweepOnce();
        receipt.CompletedRoots.ShouldBe(0);
        receipt.Skips["inventory-incomplete-or-linked"].ShouldBe(1);
        f.Io.DirectoryExists(root).ShouldBeTrue();
    }

    [Test]
    public void monotonic_deadline_stops_new_operations()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox);
        var probe = new CountingProbe(f.Context);
        var sweep = new CheckpointTempRootSweep(f.Context, sandbox, probe,
            new CheckpointSweepOptions { Grace = TimeSpan.Zero, Interval = TimeSpan.Zero,
                MaxDuration = TimeSpan.Zero });
        var receipt = sweep.SweepOnce();
        receipt.Examined.ShouldBe(0);
        receipt.BudgetExhausted.ShouldBeTrue();
        probe.Calls.ShouldBe(0);
        f.Io.DirectoryExists(root).ShouldBeTrue();
    }

    [Test]
    public void resumed_deletion_rechecks_every_veto()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox, payloadBytes: 16 * 1024);
        var sweep = Sweep(f, sandbox, maxBytes: 8 * 1024);
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        var payload = Path.Combine(root, "payload.bin");
        var remaining = f.Io.Length(payload);
        var marker = TestRootGuard.Read(root, f.Context)!;
        marker.Owner = new ProcessIdentityProbe(f.Context).Current();
        TestRootGuard.Write(root, marker, f.Context);
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        f.Io.Length(payload).ShouldBe(remaining);
        f.Io.DirectoryExists(root).ShouldBeTrue();
    }

    [Test]
    public void marker_survives_partial_deletion()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox, payloadBytes: 16 * 1024);
        var sweep = Sweep(f, sandbox, maxBytes: 8 * 1024);
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        var marker = TestRootGuard.Read(root, f.Context);
        marker.ShouldNotBeNull("partial-marker-retained");
        marker.State.ShouldBe("deleting", "partial-marker-retained");
        sweep.SweepOnce().CompletedRoots.ShouldBe(1);
        f.Io.DirectoryExists(root).ShouldBeFalse();
    }

    [Test]
    public void cursor_prevents_new_arrival_starvation()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var old = Enumerable.Range(0, 5).Select(_ => Candidate(f, sandbox)).ToArray();
        var sweep = Sweep(f, sandbox, maxDirect: 2);
        sweep.SweepOnce().CompletedRoots.ShouldBe(2);
        Candidate(f, sandbox);
        Candidate(f, sandbox);
        sweep.SweepOnce().CompletedRoots.ShouldBe(2);
        sweep.SweepOnce().CompletedRoots.ShouldBeGreaterThanOrEqualTo(1);
        old.ShouldAllBe(path => !f.Io.DirectoryExists(path));
    }

    [Test]
    public void absent_root_pointer_is_dropped_by_the_sweep()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var root = Candidate(f, sandbox);
        f.Io.RemoveDirectory(root, recursive: true);
        var receipt = Sweep(f, sandbox).SweepOnce();
        receipt.Skips["absent"].ShouldBe(1, "absent-pointer-counted");
        receipt.CompletedRoots.ShouldBe(0, "absent-pointer-deletes-nothing");
        f.Io.FileExists(IndexPath(sandbox, root)).ShouldBeFalse("absent-pointer-dropped");
    }

    [Test]
    public void disposed_roots_do_not_consume_the_next_sweep_budget()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var index = new CheckpointTempRootSweep(f.Context, sandbox);
        var legacyEntries = new List<string>();
        for (var i = 0; i < 700; i++)
        {
            var disposed = Candidate(f, sandbox);
            f.Io.RemoveDirectory(disposed, recursive: true);
            index.Unregister(disposed);
            legacyEntries.Add(JsonSerializer.Serialize(disposed));
        }
        var orphan = Candidate(f, sandbox);
        index.Unregister(orphan);
        legacyEntries.Add(JsonSerializer.Serialize(orphan));
        f.Io.Put(Path.Combine(sandbox, ".checkpoint-temp-roots.jsonl"), string.Join('\n', legacyEntries) + "\n");
        var receipt = Sweep(f, sandbox, maxDirect: 1).SweepOnce();
        receipt.Examined.ShouldBe(1);
        receipt.CompletedRoots.ShouldBe(1);
        f.Io.DirectoryExists(orphan).ShouldBeFalse();
    }

    [Test]
    public void eligible_backlog_drains_without_touching_exclusions()
    {
        var f = new HostCleanupCheckpointFixture();
        var sandbox = f.Parent;
        var eligible = Enumerable.Range(0, 4).Select(_ => Candidate(f, sandbox)).ToArray();
        var live = Candidate(f, sandbox, alive: true);
        var sweep = Sweep(f, sandbox, maxRoots: 2);
        for (var i = 0; i < 4 && eligible.Any(f.Io.DirectoryExists); i++) sweep.SweepOnce();
        eligible.ShouldAllBe(path => !f.Io.DirectoryExists(path));
        f.Io.DirectoryExists(live).ShouldBeTrue();
        TestRootGuard.Read(live, f.Context).ShouldNotBeNull();
    }


    private static CheckpointTempRootSweep Sweep(HostCleanupCheckpointFixture f, string sandbox, Func<DateTimeOffset>? clock = null,
        TimeSpan? grace = null, TimeSpan? interval = null, int maxRoots = 16, long maxBytes = 256L * 1024 * 1024,
        int maxDirect = 512, int maxDescendants = 10000, ProcessIdentityProbe? probe = null) => new(f.Context, sandbox, probe,
        options: new CheckpointSweepOptions
        {
            Grace = grace ?? TimeSpan.Zero, Interval = interval ?? TimeSpan.Zero,
            MaxRoots = maxRoots, MaxBytes = maxBytes, MaxDirectEntries = maxDirect,
            MaxDescendantEntries = maxDescendants, MaxDuration = TimeSpan.FromSeconds(5),
        }, clock: clock);

    private static string Candidate(HostCleanupCheckpointFixture f, string sandbox, int payloadBytes = 0,
        DateTimeOffset? createdAt = null, bool alive = false)
    {
        // Primitive dictionaries are single-threaded; this is independent of the held sweep gate.
        lock (f.Io)
        {
            var root = Path.Combine(sandbox, "c723-" + Guid.CreateVersion7().ToString("N"));
            f.Io.CreateDirectory(root);
            var current = new ProcessIdentityProbe(f.Context).Current();
            TestRootGuard.Write(root, new CheckpointRootMarker
            {
                RootId = Path.GetFileName(root)[5..], RootPath = root,
                AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "virtual-sweep",
                CreatedAt = createdAt ?? f.Clock.GetUtcNow().AddHours(-1),
                Owner = alive ? current : current with { Pid = int.MaxValue, StartUtcTicks = 1 },
            }, f.Context);
            if (payloadBytes > 0) f.Io.Put(Path.Combine(root, "payload.bin"), new byte[payloadBytes]);
            new CheckpointTempRootSweep(f.Context, sandbox).Register(root);
            return root;
        }
    }

    private sealed class CountingProbe(Antiphon.HostCleanup.Checkpoints.CheckpointCleanupContext context) : ProcessIdentityProbe(context)
    {
        public int Calls { get; private set; }
        public override ProcessObservation Observe(ProcessIdentity? expected)
        { Calls++; return base.Observe(expected); }
    }

    private static string IndexPath(string sandbox, string root) =>
        Path.Combine(sandbox, ".checkpoint-temp-roots", Path.GetFileName(root) + ".json");

    private sealed class CallbackProbe(Antiphon.HostCleanup.Checkpoints.CheckpointCleanupContext context,
        Func<ProcessIdentity?, ProcessObservation> observe) : ProcessIdentityProbe(context)
    {
        public override ProcessObservation Observe(ProcessIdentity? expected) => observe(expected);
    }
    [Test]
    public void nested_live_executor_vetoes_whole_root()
    {
        var f = new SweepFixture();
        f.MarkRun(new ProcessIdentityProbe(f.Files.Context).Current());
        f.Files.Io.Put(Path.Combine(f.Root, "sentinel"), "keep");
        f.ClearEvents();

        var receipt = f.Sweep.SweepOnce();
        receipt.Examined.ShouldBe(1, "C987.nested-live-reached");
        receipt.CompletedRoots.ShouldBe(0, "nested-live-retained");
        receipt.Skips.GetValueOrDefault("nested-identity-alive").ShouldBe(1, "C987.nested-live-guard");
        f.Files.Io.Text(Path.Combine(f.Root, "sentinel")).ShouldBe("keep", "nested-live-retained");
        f.AssertGateAndNoDelete();
        f.Files.Io.Count("open-read", Path.Combine(f.Run, "executor-ownership.json")).ShouldBeGreaterThan(0);

        // Removing only the live-owner veto must let this same candidate finish.
        f.MarkRun(f.Dead);
        f.Sweep.Register(f.Root);
        f.Sweep.SweepOnce().CompletedRoots.ShouldBe(1, "C987.nested-live-positive-pair");
        f.Files.Io.DirectoryExists(f.Root).ShouldBeFalse();
        f.Files.Io.RecursiveIntents.ShouldBe(0);
    }

    [Test]
    public void unknown_nested_custody_vetoes_whole_root()
    {
        var f = new SweepFixture();
        var sentinel = Path.Combine(f.Run, "tool", "sentinel");
        f.Files.Io.Put(sentinel, "keep");
        f.ClearEvents();

        var receipt = f.Sweep.SweepOnce();
        receipt.Examined.ShouldBe(1, "C987.nested-missing-reached");
        receipt.CompletedRoots.ShouldBe(0, "nested-missing-retained");
        receipt.Skips.GetValueOrDefault("nested-custody-missing").ShouldBe(1, "C987.nested-missing-guard");
        f.Files.Io.Text(sentinel).ShouldBe("keep", "nested-missing-retained");
        f.AssertGateAndNoDelete();
        f.Files.Io.Count("file-exists", Path.Combine(f.Run, "ownership.json")).ShouldBeGreaterThan(0);

        // Supply real serialized custody, never a canned deletion decision.
        f.MarkRun(f.Dead);
        f.Sweep.Register(f.Root);
        f.Sweep.SweepOnce().CompletedRoots.ShouldBe(1, "C987.nested-missing-positive-pair");
        f.Files.Io.DirectoryExists(f.Root).ShouldBeFalse();
        f.Files.Io.RecursiveIntents.ShouldBe(0);
    }

    private sealed class SweepFixture
    {
        public HostCleanupCheckpointFixture Files { get; } = new();
        public string Root { get; }
        public string Run => Path.Combine(Root, "custom-results", "run");
        public ProcessIdentity Dead => new(999, 1, Files.Io.MachineName, Files.Io.BootId, Files.Io.PidNamespace);
        public CheckpointTempRootSweep Sweep { get; }

        public SweepFixture()
        {
            Root = Path.Combine(Files.Parent, "c723-" + Guid.NewGuid().ToString("N"));
            Files.Io.CreateDirectory(Path.Combine(Run, "tool"));
            TestRootGuard.Write(Root, new()
            {
                RootId = Path.GetFileName(Root)["c723-".Length..], RootPath = Root,
                AttemptId = "virtual-attempt", AssemblyInvocationId = "virtual-assembly",
                CreatedAt = Files.Clock.GetUtcNow().AddHours(-1), Owner = Dead
            }, Files.Context);
            var index = Path.Combine(Files.Parent, ".checkpoint-temp-roots");
            Files.Io.CreateDirectory(index);
            Files.Io.Put(Path.Combine(index, Path.GetFileName(Root) + ".json"), JsonSerializer.Serialize(Root));
            Sweep = new(Files.Context, Files.Parent, options: new() { Interval = TimeSpan.Zero });
        }

        public void MarkRun(ProcessIdentity executor)
        {
            RunOwnershipStore.Write(Run, new()
            {
                RunId = "run", RunDirectory = Run, Starter = Dead, Launched = executor, Phase = "launched"
            }, Files.Context);
            ExecutorOwnershipStore.Write(Run, executor, Files.Context);
        }

        public void ClearEvents() { Files.Io.Events.Clear(); Files.Io.Opens.Clear(); }

        public void AssertGateAndNoDelete()
        {
            Files.Io.Opens.ShouldContain(o => o.Path == Path.Combine(Root, TestRootGuard.LockName)
                && o.Share == FileShare.None, "C987.nested-guard-under-root-gate");
            Files.Io.Count("unlink").ShouldBe(0);
            Files.Io.Count("rmdir").ShouldBe(0);
            TestRootGuard.Read(Root, Files.Context)!.State.ShouldBe("active");
        }
    }

    [Test]
    public async Task wait_uses_custody_not_progress_pid()
    {
        var f = new HostCleanupToolFixture();
        f.Owner.Phase = "launch-attempted"; f.Owner.Launched = null;
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Receipt.State.ExecutorPid = int.MaxValue;
        f.Io.Put(f.Receipt.StatePath, JsonSerializer.Serialize(f.Receipt.State, RunStateStore.Json));
        (await f.Wait()).ShouldBe(75);
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
        f.Io.Text(f.Receipt.MarkdownPath).ShouldContain("GREEN");
    }

    [Test]
    public void next_start_preserves_all_pre_existing_sibling_tools()
    {
        var f = new HostCleanupToolFixture();
        var siblings = new[] { Path.Combine(f.Files.Parent, ".antiphon", "checkpoints", "dead", "tool"), Path.Combine(f.Files.Parent, ".antiphon", "checkpoints", "unknown", "tool"), Path.Combine(f.Files.Parent, "other", "tool") };
        foreach (var p in siblings)
        {
            var siblingRun = Path.GetDirectoryName(p)!;
            SafeWrite.Require(f.Files.Write.CreateDirectory(siblingRun, f.Files.Write.Under(f.Files.Parent)));
            SafeWrite.Require(f.Files.Write.CreateFreshDirectory(p, f.Files.Write.Under(siblingRun)));
            SafeWrite.Require(f.Files.Write.Text(Path.Combine(p, "sentinel"), "keep", f.Files.Write.Under(p)));
            SafeWrite.Require(f.Files.Ledger.Seal(p));
            RunOwnershipStore.Write(siblingRun, new()
            {
                RunId = Path.GetFileName(siblingRun), RunDirectory = siblingRun,
                Starter = f.Dead, Launched = p == siblings[1] ? null : f.Dead,
                Phase = p == siblings[1] ? "launch-attempted" : "launched"
            }, f.Context);
        }
        // The dead sibling is deletable under this context: startup must not sweep it.
        f.Files.Ledger.Check(siblings[0]).ShouldBeNull();
        f.Cleanup().ObserveExecutor(Path.GetDirectoryName(siblings[0])!).Verdict.ShouldBe(ProcessVerdict.Dead);
        f.Receipt.ClearEvents();
        var run = CheckpointApp.CreateRun(HostCleanupToolFixture.Manifest(), new() { Branch = "virtual" }, f.Files.Parent, f.Runtime());
        f.Io.DirectoryExists(run).ShouldBeTrue();
        foreach (var p in siblings)
        {
            f.Io.DirectoryExists(p).ShouldBeTrue();
            f.Io.FileExists(Path.Combine(p, "sentinel")).ShouldBeTrue();
            f.Io.Text(Path.Combine(p, "sentinel")).ShouldBe("keep");
        }
        f.Io.Count("unlink").ShouldBe(0); f.Io.Count("rmdir").ShouldBe(0);
    }

    [Test]
    public async Task stop_validates_generation_before_signaling()
    {
        var f = new HostCleanupToolFixture(); var control = new CountingControl();
        f.Owner.Launched = new ProcessIdentityProbe(f.Context).Current() with { StartUtcTicks = 1 };
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        (await Antiphon.Checkpoints.Program.RunAsync(["stop", "--run", f.Run, "--repo-root", f.Files.Parent], f.Runtime(control))).ShouldBe(75);
        control.Calls.ShouldBe(0); f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
    }

    [Test]
    public async Task stop_waits_for_confirmed_exit()
    {
        var f = new HostCleanupToolFixture(); var control = new CountingControl();
        f.Owner.Launched = new ProcessIdentityProbe(f.Context).Current();
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Receipt.State.Phase = "running";
        f.Io.Put(f.Receipt.StatePath, JsonSerializer.Serialize(f.Receipt.State, RunStateStore.Json));
        (await Antiphon.Checkpoints.Program.RunAsync(["stop", "--run", f.Run, "--repo-root", f.Files.Parent], f.Runtime(control))).ShouldBe(75);
        control.Calls.ShouldBe(1);
        new RunStateStore(f.Context).TryRead(f.Receipt.StatePath)!.Phase.ShouldBe("running");
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
    }

    [Test]
    public void age_clean_cannot_bypass_live_custody()
    {
        var f = new HostCleanupToolFixture(); var results = Path.Combine(f.Files.Root, "results");
        f.Io.CreateDirectory(results);
        var identities = new[] { f.Dead, new ProcessIdentityProbe(f.Context).Current(), new ProcessIdentityProbe(f.Context).Current() with { StartUtcTicks = 1 }, f.Dead with { Host = "foreign" } };
        for (var i = 0; i < identities.Length; i++)
        {
            var run = Path.Combine(results, "run" + i); f.Io.CreateDirectory(run); f.Io.Put(Path.Combine(run, "sentinel"), "keep");
            RunOwnershipStore.Write(run, new() { RunId = Path.GetFileName(run), RunDirectory = run, Starter = f.Dead, Launched = identities[i], Phase = "launched" }, f.Context);
            f.Io.Nodes[run].Stat = f.Io.Nodes[run].Stat with { Modified = (f.Files.Clock.GetUtcNow().AddDays(-8).UtcTicks - DateTime.UnixEpoch.Ticks) * 100 };
        }
        var result = new CheckpointOutputCleanup(f.Context).RemoveOlderRuns(results, TimeSpan.FromDays(7), false);
        result.Deleted.ShouldBeEmpty(); result.Refusals.ShouldContain("output-ledger-missing");
        for (var i = 0; i < identities.Length; i++) f.Io.Text(Path.Combine(results, "run" + i, "sentinel")).ShouldBe("keep");
    }

    [Test]
    public void evidence_write_has_no_raw_delete_bypass()
    {
        var f = new HostCleanupToolFixture(); f.Io.Nodes.Remove(Path.Combine(f.Run, "ownership.json"));
        EvidenceFolder.Write(f.Run, new() { RunId = Path.GetFileName(f.Run) }, true, context: f.Context);
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue(); f.Io.Text(f.Payload).ShouldBe("keep");
    }

    [Test]
    public void automatic_recovery_preserves_all_evidence()
    {
        var f = new HostCleanupToolFixture();
        foreach (var n in new[] { "request.json", "manifest.resolved.yaml", "run.trx", "executor.log" }) f.Io.Put(Path.Combine(f.Run, n), "evidence-" + n);
        var before = f.Io.Nodes.Where(p => Path.GetDirectoryName(p.Key) == f.Run && p.Value.Stat.Kind == "file").ToDictionary(p => p.Key, p => Hash(p.Value.Bytes));
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("Removed");
        f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
        foreach (var (path, hash) in before)
        {
            f.Io.FileExists(path).ShouldBeTrue("preserved evidence: " + path);
            Hash(f.Io.Nodes[path].Bytes).ShouldBe(hash);
        }
    }

    [Test]
    public void sweep_never_signals_a_process()
    {
        var f = new HostCleanupToolFixture();
        f.Owner.Launched = new ProcessIdentityProbe(f.Context).Current();
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Cleanup().Sweep(Path.GetDirectoryName(f.Run)!).ShouldBe(0);
        HostCleanupCheckpointFixture.AssertNoSignalCalls(typeof(ToolCopyCleanup));
        f.Io.Processes[f.Io.CurrentProcessId].HasExited.ShouldBeFalse();
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
        f.Io.Count("process", f.Io.CurrentProcessId.ToString()).ShouldBeGreaterThan(0);
    }

    [Test]
    public Task failed_delete_receipt_is_truthful_and_retryable() => RetryDelayProof("C987.failed_delete_receipt_is_truthful_and_retryable.retry");

    internal static async Task RetryDelayProof(string label)
    {
        var f = new HostCleanupToolFixture();
        var attempts = 0;
        var task = Task.Run(() => f.Cleanup(beforeDelete: _ => { Interlocked.Increment(ref attempts); throw new IOException("delete-denied"); }).Remove(f.Run));
        for (var i = 0; i < 9; i++)
        {
            using var observing = new CancellationTokenSource();
            var timerTask = f.Files.Clock.WaitForTimerAsync(
                e => e.Action == "create" && e.DueTime == TimeSpan.FromMilliseconds(50 * (i + 1)), ct: observing.Token);
            var winner = await Task.WhenAny(task, timerTask);
            if (winner != timerTask)
            {
                observing.Cancel();
                try { await timerTask; } catch (OperationCanceledException) { }
                await task;
                (winner == timerTask).ShouldBeTrue(label + ": cleanup completed without scheduling the injected delay");
            }
            var timer = await timerTask;
            f.Files.Clock.Advance(timer.DueTime);
        }
        (await task).Outcome.ShouldBe("Failed", label); attempts.ShouldBe(10, label);
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("Removed"); f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
    }

    [Test]
    [Arguments(0, false)] [Arguments(0, true)] [Arguments(1, false)] [Arguments(1, true)]
    public async Task completed_wait_removes_only_tool(int exitCode, bool keepOutputs)
    {
        var f = new HostCleanupToolFixture(exitCode);
        f.Io.Put(Path.Combine(f.Run, "request.json"), JsonSerializer.Serialize(new RunRequest { KeepOutputs = keepOutputs }));
        var hash = Hash(f.Io.Nodes[f.Receipt.MarkdownPath].Bytes);
        (await f.Wait()).ShouldBe(exitCode);
        f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
        f.Io.FileExists(f.Receipt.MarkdownPath).ShouldBeTrue();
        Hash(f.Io.Nodes[f.Receipt.MarkdownPath].Bytes).ShouldBe(hash);
        f.Io.DirectoryExists(f.Receipt.Output).ShouldBeTrue();
    }

    [Test]
    public void already_absent_is_idempotent()
    {
        var f = new HostCleanupToolFixture();
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("Removed");
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("AlreadyAbsent");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal sealed class CountingControl : IProcessControl
    {
        public int Calls { get; private set; }
        public bool StopAndWait(ProcessIdentity identity, TimeSpan timeout) { Calls++; return false; }
    }
}
