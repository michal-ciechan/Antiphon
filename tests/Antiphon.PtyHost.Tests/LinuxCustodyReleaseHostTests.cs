using Antiphon.TestSupport;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-1614 round 2. Portable Linux host witnesses. A fake containment stands in for the
/// cgroup; nothing here calls sudo or the installed helpers.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NativeLinuxOnly("The inbox pty launch runs on Linux.")]
public sealed class LinuxCustodyReleaseHostTests
{
    [Test]
    public async Task C1614_Released_launch_failure_exits_within_ttl()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var ttl = TimeSpan.FromMilliseconds(400);
        var namespaceId = Guid.Parse("16140000-0000-4000-8000-0000000000b2");
        var files = new TrackingRefusal();
        HostCustodyJournal.LinuxNamespaceForTests.Value = namespaceId;
        var host = HostHarness.Start(o => o with
        {
            PtyBackend = "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            LingerTtl = ttl,
        }, files);
        var containment = new FakeContainment();
        host.Session.UseContainmentForTests(containment);
        var (store, binding) = Reserve(host, files);
        try
        {
            var reply = await host.Session.LaunchAsync(Spec(host, store, binding, "sleep 30"), CancellationToken.None);
            reply.ShouldBeOfType<ErrorMessage>();
            files.Failures.ShouldBeGreaterThan(0);
            File.Exists(store.PathFor(binding.ExecutionId, "native-start-intent.json")).ShouldBeTrue();
            File.Exists(store.PathFor(binding.ExecutionId, "tracking.json")).ShouldBeFalse();

            var ns = store.ReadRecord<LinuxNamespaceRecord>(binding, "linux-namespace.json");
            ns.ShouldNotBeNull();
            ns!.NamespaceId.ShouldBe(namespaceId);

            store.WriteRecord(binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
            containment.Pids.Clear();
            var status = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
            status.State.ShouldBe(VerificationCustodyState.Unknown);
            status.Receipt.ShouldBeNull();
            File.Exists(store.PathFor(binding.ExecutionId, "linux-release-disposition.json")).ShouldBeTrue();
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();

            var done = await Task.WhenAny(host.RunTask, Task.Delay(ttl + TimeSpan.FromSeconds(2)));
            done.ShouldBe(host.RunTask);
            await host.RunTask;
        }
        finally
        {
            HostCustodyJournal.LinuxNamespaceForTests.Value = null;
            try
            {
                if (files.Child is { HasExited: false })
                    files.Child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            files.Child?.Dispose();
            await Finish(host);
        }
    }

    /// <summary>
    /// Root exit with a live descendant is Draining. The host must not terminate.
    /// Green on the current tip; red if Linux seals because Status is Exited.
    /// </summary>
    [Test]
    public async Task C1614_Root_exit_with_live_descendants_does_not_terminate()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment();
        containment.Pids.Add(424242);
        var host = HostHarness.Start(o => o with
        {
            PtyBackend = "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            LingerTtl = TimeSpan.FromHours(24),
        });
        host.Session.UseContainmentForTests(containment);
        var (store, binding) = Reserve(host, files: null);
        var release = Path.Combine(host.TempDir, "release");
        var child = 0;
        try
        {
            // Inbox does not replay an exit that landed before Exited is subscribed.
            // Hold the child until LaunchAsync returns, then let it exit.
            var reply = await host.Session.LaunchAsync(Spec(host, store, binding,
                HoldUntil(release), "exit 0"), CancellationToken.None);
            reply.ShouldBeOfType<LaunchedMessage>(reply is ErrorMessage error
                ? error.Code + ": " + error.Message : "launch reply");
            child = ((LaunchedMessage)reply).ChildPid;
            File.WriteAllText(release, "go");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (host.Session.Status != PtyHostStatus.Exited && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            host.Session.Status.ShouldBe(PtyHostStatus.Exited);
            await Task.Delay(300);
            containment.Terminations.ShouldBe(0);

            var status = await host.Session.GetCustodyAsync(binding, false, CancellationToken.None);
            status.State.ShouldBe(VerificationCustodyState.Draining);
            status.Reason.ShouldBe("descendants_running");
            status.Receipt.ShouldBeNull();
            containment.Terminations.ShouldBe(0);
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
            File.Exists(store.PathFor(binding.ExecutionId, "producer-seal.json")).ShouldBeFalse();
            File.Exists(store.PathFor(binding.ExecutionId, "linux-release-disposition.json")).ShouldBeFalse();
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// An ordinary read of a missing tree stays Unknown and does not terminate or receipt.
    /// Green on the current tip; red if an unreadable tree is certified as a natural zero.
    /// </summary>
    [Test]
    public async Task C1614_Missing_tree_ordinary_read_stays_unknown()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var namespaceId = Guid.Parse("16140000-0000-4000-8000-0000000000b3");
        var containment = new FakeContainment();
        var files = new RememberTracking(containment);
        HostCustodyJournal.LinuxNamespaceForTests.Value = namespaceId;
        var host = HostHarness.Start(o => o with
        {
            PtyBackend = "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            LingerTtl = TimeSpan.FromHours(24),
        }, files);
        host.Session.UseContainmentForTests(containment);
        var (store, binding) = Reserve(host, files);
        var release = Path.Combine(host.TempDir, "release");
        var child = 0;
        try
        {
            var reply = await host.Session.LaunchAsync(Spec(host, store, binding,
                HoldUntil(release), "exit 0"), CancellationToken.None);
            reply.ShouldBeOfType<LaunchedMessage>(reply is ErrorMessage error
                ? error.Code + ": " + error.Message : "launch reply");
            var launched = (LaunchedMessage)reply;
            child = launched.ChildPid;
            var placement = store.ReadRecord<LinuxPlacementRecord>(binding, "linux-placement.json");
            placement.ShouldNotBeNull();
            placement!.NamespaceId.ShouldBe(namespaceId);
            containment.Pids.ShouldContain(launched.ChildPid);
            containment.Missing = true;
            File.WriteAllText(release, "go");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (host.Session.Status != PtyHostStatus.Exited && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            host.Session.Status.ShouldBe(PtyHostStatus.Exited);

            var status = await host.Session.GetCustodyAsync(binding, false, CancellationToken.None);
            status.State.ShouldBe(VerificationCustodyState.Unknown);
            status.Reason.ShouldBe("observation_failed");
            status.Receipt.ShouldBeNull();
            containment.Terminations.ShouldBe(0);
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
            File.Exists(store.PathFor(binding.ExecutionId, "producer-seal.json")).ShouldBeFalse();
        }
        finally
        {
            HostCustodyJournal.LinuxNamespaceForTests.Value = null;
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1617 W1. A successful kill that leaves survivors is not repeated, however much time
    /// passes. Red if every explicit-seal read terminates again.
    /// </summary>
    [Test]
    public async Task C1617_Explicit_seal_reads_terminate_once_after_success()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment { KeepPids = true, Result = new(true, 0) };
        var clock = new ManualClock();
        var (host, store, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            for (var read = 0; read < 10; read++)
            {
                if (read > 0)
                    clock.Advance(TimeSpan.FromMinutes(10));
                await ShouldDrainAsync(host, binding);
            }

            containment.Terminations.ShouldBe(1);
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
            File.Exists(store.PathFor(binding.ExecutionId, "linux-release-disposition.json")).ShouldBeFalse();
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1617 W2. A failed kill is retried only after min(2^(k-1) s, 60 s) from the end of
    /// failed attempt k, exactly at the boundary and through the cap. A skipped read still
    /// re-reads accounting, and the receipt carries the last real attempt.
    /// </summary>
    [Test]
    public async Task C1617_Failed_termination_retries_on_capped_backoff()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment { KeepPids = true, Result = new(false, 3) };
        var clock = new ManualClock();
        var (host, store, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            for (var read = 0; read < 5; read++)
                await ShouldDrainAsync(host, binding);
            containment.Terminations.ShouldBe(1, "five reads at t=0");

            for (var k = 1; k <= 8; k++)
            {
                var wait = TimeSpan.FromSeconds(Math.Min(1 << (k - 1), 60));
                clock.Advance(wait - TimeSpan.FromMilliseconds(1));
                await ShouldDrainAsync(host, binding);
                containment.Terminations.ShouldBe(k, $"1 ms before backoff {k} ({wait})");
                clock.Advance(TimeSpan.FromMilliseconds(1));
                await ShouldDrainAsync(host, binding);
                containment.Terminations.ShouldBe(k + 1, $"at backoff {k} ({wait})");
            }

            containment.Pids.Clear();
            var final = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
            final.State.ShouldBe(VerificationCustodyState.Exited, final.Reason);
            containment.Terminations.ShouldBe(9);
            var receipt = store.ValidateReceipt(final.Receipt!, binding, final.Host!);
            receipt.ActiveProcesses.ShouldBe(0u);
            receipt.TerminationSucceeded.ShouldBe(false);
            receipt.TerminationError.ShouldBe(3);
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1617 W3. After the one successful kill, a read that finds the tree empty still drains
    /// and receipts the real attempt's outcome. Green before the bound; red if the bound skips
    /// accounting or overwrites the outcome on a skipped read.
    /// </summary>
    [Test]
    public async Task C1617_Bounded_release_still_finalizes()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment { KeepPids = true, Result = new(true, 0) };
        var clock = new ManualClock();
        var (host, store, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            await ShouldDrainAsync(host, binding);

            containment.Pids.Clear();
            var final = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
            final.State.ShouldBe(VerificationCustodyState.Exited, final.Reason);
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeTrue();
            var receipt = store.ValidateReceipt(final.Receipt!, binding, final.Host!);
            receipt.TerminationSucceeded.ShouldBe(true);
            receipt.TerminationError.ShouldBe(0);
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1617 W4. A kill that throws is swallowed by the host's stop recorder and counts as a
    /// failed attempt, so it backs off too. Red if the bound is keyed on a non-null outcome.
    /// </summary>
    [Test]
    public async Task C1617_Throwing_termination_counts_as_failed_attempt()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment { KeepPids = true, Throw = new IOException("c1617") };
        var clock = new ManualClock();
        var (host, _, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            await ShouldDrainAsync(host, binding);
            containment.Terminations.ShouldBe(1);

            for (var read = 0; read < 3; read++)
                await ShouldDrainAsync(host, binding);
            containment.Terminations.ShouldBe(1, "three more reads at t=0");

            clock.Advance(TimeSpan.FromSeconds(1));
            await ShouldDrainAsync(host, binding);
            containment.Terminations.ShouldBe(2);
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1685 S08. An explicit seal whose tree read throws is Unknown, never a clean zero, and
    /// writes no receipt. Red if the seal path reads an unreadable tree as empty.
    /// </summary>
    [Test]
    public async Task C1685_Explicit_seal_unreadable_tree_stays_unknown()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment();
        var clock = new ManualClock();
        var (host, store, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            containment.Missing = true;
            for (var read = 0; read < 2; read++)
            {
                var status = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
                status.State.ShouldBe(VerificationCustodyState.Unknown, $"read {read}");
                status.Reason.ShouldBe("observation_failed");
                status.Receipt.ShouldBeNull();
            }

            containment.Terminations.ShouldBe(1);
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1685 S02. A tree that is empty before the drain but repopulated after it is Draining
    /// and writes no receipt. Red if the post-drain count is not a real re-read.
    /// </summary>
    [Test]
    public async Task C1685_Tree_repopulated_after_drain_stays_draining()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment();
        var clock = new ManualClock();
        var (host, store, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            // Arm inside the kill: only the explicit seal kills, so a background ordinary read
            // cannot take the repopulation. The seal's first read counts 0, then the tree refills.
            containment.DuringTerminate = () => containment.AfterRead = () =>
            {
                containment.AfterRead = null;
                containment.Pids.Add(424243);
            };
            var status = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
            status.State.ShouldBe(VerificationCustodyState.Draining, status.Reason);
            status.Reason.ShouldBe("descendants_running");
            status.Receipt.ShouldBeNull();
            containment.AfterRead.ShouldBeNull("the seal read the tree before the drain");
            containment.Terminations.ShouldBe(1);
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1685 S11. The failure count is clamped before the shift: past k = 32 the wait stays
    /// 60 s, never negative or 1 s. Red if the shift is taken first and capped after.
    /// </summary>
    [Test]
    public async Task C1685_Large_failure_count_keeps_sixty_second_backoff()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment { KeepPids = true, Result = new(false, 3) };
        var clock = new ManualClock();
        var (host, _, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            await ShouldDrainAsync(host, binding);
            containment.Terminations.ShouldBe(1);

            for (var k = 1; k <= 40; k++)
            {
                var wait = k < 7 ? TimeSpan.FromSeconds(1 << (k - 1)) : TimeSpan.FromMinutes(1);
                if (k >= 7)
                {
                    clock.Advance(wait - TimeSpan.FromMilliseconds(1));
                    await ShouldDrainAsync(host, binding);
                    containment.Terminations.ShouldBe(k, $"1 ms before backoff {k} ({wait})");
                    clock.Advance(TimeSpan.FromMilliseconds(1));
                }
                else
                    clock.Advance(wait);
                await ShouldDrainAsync(host, binding);
                containment.Terminations.ShouldBe(k + 1, $"at backoff {k} ({wait})");
            }
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1685 S15. The backoff counts from the end of the attempt: a kill that takes 45 s is
    /// not retried 1 s after it started. Red if the wait is measured from the attempt start.
    /// </summary>
    [Test]
    public async Task C1685_Backoff_counts_from_attempt_end()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var clock = new ManualClock();
        var containment = new FakeContainment
        {
            KeepPids = true,
            Result = new(false, 3),
            DuringTerminate = () => clock.Advance(TimeSpan.FromSeconds(45)),
        };
        var (host, _, binding, child) = await ExitedWithSurvivorAsync(containment, clock);
        try
        {
            await ShouldDrainAsync(host, binding);
            containment.Terminations.ShouldBe(1);

            for (var k = 1; k <= 2; k++)
            {
                var wait = TimeSpan.FromSeconds(1 << (k - 1));
                clock.Advance(wait - TimeSpan.FromMilliseconds(1));
                await ShouldDrainAsync(host, binding);
                containment.Terminations.ShouldBe(k, $"45 s + {wait} - 1 ms after attempt {k} started");
                clock.Advance(TimeSpan.FromMilliseconds(1));
                await ShouldDrainAsync(host, binding);
                containment.Terminations.ShouldBe(k + 1, $"{wait} after attempt {k} ended");
            }
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1681. After the root exits the pty master reads EIO; that is end of output, so an
    /// explicit release of an empty tree drains and receipts. Red while EIO is a drain failure.
    /// </summary>
    [Test]
    public async Task C1681_Eio_after_root_exit_drains_to_receipt()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment();
        var host = HostHarness.Start(o => o with
        {
            PtyBackend = "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            LingerTtl = TimeSpan.FromHours(24),
        });
        host.Session.UseContainmentForTests(containment);
        var (store, binding) = Reserve(host, files: null);
        var release = Path.Combine(host.TempDir, "release");
        var child = 0;
        try
        {
            var reply = await host.Session.LaunchAsync(Spec(host, store, binding,
                HoldUntil(release), "printf 'c1681-tail\\n'", "exit 0"), CancellationToken.None);
            reply.ShouldBeOfType<LaunchedMessage>(reply is ErrorMessage error
                ? error.Code + ": " + error.Message : "launch reply");
            child = ((LaunchedMessage)reply).ChildPid;
            File.WriteAllText(release, "go");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (host.Session.Status != PtyHostStatus.Exited && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            host.Session.Status.ShouldBe(PtyHostStatus.Exited);

            var final = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
            final.State.ShouldBe(VerificationCustodyState.Exited, final.Reason);
            var receipt = store.ValidateReceipt(final.Receipt!, binding, final.Host!);
            receipt.ActiveProcesses.ShouldBe(0u);
            receipt.OutputDrained.ShouldBeTrue();
        }
        finally
        {
            KillChild(child);
            await Finish(host);
        }
    }

    /// <summary>
    /// CARD-1681. A live root that closes every slave descriptor also makes the master read EIO.
    /// That is not end of output: the drain fails and no receipt is written, even for an empty
    /// tree. Green before the fix; red if every EIO is read as end of output.
    /// </summary>
    [Test]
    public async Task C1681_Eio_while_root_alive_stays_drain_failure()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var containment = new FakeContainment();
        var host = HostHarness.Start(o => o with
        {
            PtyBackend = "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            LingerTtl = TimeSpan.FromHours(24),
        });
        host.Session.UseContainmentForTests(containment);
        var (store, binding) = Reserve(host, files: null);
        var release = Path.Combine(host.TempDir, "release");
        var detached = Path.Combine(host.TempDir, "detached");
        var child = 0;
        try
        {
            var reply = await host.Session.LaunchAsync(Spec(host, store, binding,
                HoldUntil(release), "exec </dev/null >/dev/null 2>&1", $": > '{detached}'",
                HoldUntil(release + "-exit"), "exit 0"), CancellationToken.None);
            reply.ShouldBeOfType<LaunchedMessage>(reply is ErrorMessage error
                ? error.Code + ": " + error.Message : "launch reply");
            child = ((LaunchedMessage)reply).ChildPid;
            File.WriteAllText(release, "go");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!File.Exists(detached) && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            File.Exists(detached).ShouldBeTrue();
            host.Session.Status.ShouldNotBe(PtyHostStatus.Exited);

            var status = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
            status.State.ShouldBe(VerificationCustodyState.Unknown);
            status.Reason.ShouldBe("observation_failed");
            status.Receipt.ShouldBeNull();
            host.Session.Status.ShouldNotBe(PtyHostStatus.Exited);
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
        }
        finally
        {
            File.WriteAllText(release + "-exit", "go");
            KillChild(child);
            await Finish(host);
        }
    }

    private static async Task ShouldDrainAsync(HostHarness host, VerificationExecutionBinding binding)
    {
        var status = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
        status.State.ShouldBe(VerificationCustodyState.Draining, status.Reason);
        status.Reason.ShouldBe("descendants_running");
        status.Receipt.ShouldBeNull();
    }

    /// <summary>
    /// CARD-1617: the root has exited, a descendant survives in the fake tree and the runner has
    /// persisted its release intent, so every read below is an explicit seal.
    /// </summary>
    private static async Task<(HostHarness Host, VerificationCustodyStore Store,
        VerificationExecutionBinding Binding, int Child)> ExitedWithSurvivorAsync(
        FakeContainment containment, ManualClock clock)
    {
        containment.Pids.Add(424242);
        var host = HostHarness.Start(o => o with
        {
            PtyBackend = "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            LingerTtl = TimeSpan.FromHours(24),
        });
        var child = 0;
        try
        {
            host.Session.UseContainmentForTests(containment);
            host.Session.UseCustodyClockForTests(clock);
            var (store, binding) = Reserve(host, files: null);
            var release = Path.Combine(host.TempDir, "release");
            var reply = await host.Session.LaunchAsync(Spec(host, store, binding,
                HoldUntil(release), "exit 0"), CancellationToken.None);
            reply.ShouldBeOfType<LaunchedMessage>(reply is ErrorMessage error
                ? error.Code + ": " + error.Message : "launch reply");
            child = ((LaunchedMessage)reply).ChildPid;
            File.WriteAllText(release, "go");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (host.Session.Status != PtyHostStatus.Exited && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            host.Session.Status.ShouldBe(PtyHostStatus.Exited);
            containment.Terminations.ShouldBe(0);
            store.WriteRecord(binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
            return (host, store, binding, child);
        }
        catch
        {
            KillChild(child);
            await Finish(host);
            throw;
        }
    }

    private static string HoldUntil(string releasePath) =>
        $"while [ ! -f '{releasePath}' ]; do sleep 0.05; done";

    private static void KillChild(int pid)
    {
        if (pid <= 0)
            return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// The pty read can ignore cancellation until the master closes, and dispose closes that
    /// master only after the read returns. A stuck teardown must not hide the witness.
    /// </summary>
    private static async Task Finish(HostHarness host)
    {
        var dispose = host.DisposeAsync().AsTask();
        var done = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(8)));
        if (done == dispose)
            await dispose;
        else
            _ = dispose.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static (VerificationCustodyStore Store, VerificationExecutionBinding Binding) Reserve(
        HostHarness host, IVerificationCustodyFiles? files)
    {
        var store = new VerificationCustodyStore(host.Options.CustodyStoreRoot!, files: files);
        var snapshot = Path.Combine(host.TempDir, "snapshot");
        Directory.CreateDirectory(snapshot);
        var now = DateTime.UtcNow;
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(host.SessionId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
            new(host.TempDir, Path.Combine(host.TempDir, ".git"), snapshot,
                Path.Combine(host.TempDir, ".git", "worktrees", "snapshot"), "feat/c1614", Guid.NewGuid()),
            VerificationCustodyBackends.LinuxCgroup, store.StoreId);
        store.Reserve(binding);
        store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
        return (store, binding);
    }

    private static LaunchMessage Spec(HostHarness host, VerificationCustodyStore store,
        VerificationExecutionBinding binding, params string[] lines)
    {
        var script = Path.Combine(host.TempDir, "payload-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(script, "#!/bin/sh\nset -u\n" + string.Join('\n', lines) + "\n");
        return new LaunchMessage("/bin/sh", [script], new Dictionary<string, string>(),
            binding.Creation.WorktreePath, 80, 24, 0, false, host.AnsiLogPath,
            AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow))
        {
            VerificationBinding = binding,
            RunnerStoreId = store.StoreId,
        };
    }

    private sealed class FakeContainment : IPtyCustodyContainment
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public List<int> Pids { get; } = [];
        public int Terminations { get; private set; }
        public bool Missing { get; set; }
        /// <summary>CARD-1617: survivors stay after a kill.</summary>
        public bool KeepPids { get; set; }
        public PtyTerminationObservation Result { get; set; } = new(true, 0);
        public Exception? Throw { get; set; }
        /// <summary>CARD-1685: runs after each read has taken its count.</summary>
        public Action? AfterRead { get; set; }
        /// <summary>CARD-1685: runs inside each kill, for example to spend clock time.</summary>
        public Action? DuringTerminate { get; set; }
        public PtyTrackedLaunch Place(PtyTrackedLaunch launch) => launch;
        public PtyCustodyPopulation ReadActive()
        {
            if (Missing)
                throw new IOException("verification_custody_tree_unreadable");
            var population = new PtyCustodyPopulation((uint)Pids.Count, Pids);
            AfterRead?.Invoke();
            return population;
        }
        public PtyTerminationObservation Terminate()
        {
            Terminations++;
            DuringTerminate?.Invoke();
            if (Throw is not null)
                throw Throw;
            if (!KeepPids)
                Pids.Clear();
            return Result;
        }
    }

    /// <summary>CARD-1617: a monotonic clock the test moves by hand; one tick is 100 ns.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    private sealed class TrackingRefusal : IVerificationCustodyFiles
    {
        private readonly VerificationCustodyFiles _files = new();
        public int Failures { get; private set; }
        public Process? Child { get; private set; }
        public byte[]? Read(string path) => _files.Read(path);
        public void CommitImmutable(string path, byte[] bytes)
        {
            if (Path.GetFileName(path) == "tracking.json")
            {
                var root = JsonSerializer.Deserialize<CustodyRoot>(bytes,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Child = Process.GetProcessById(root.Pid);
                Failures++;
                throw new IOException("Injected tracking persistence refusal");
            }

            _files.CommitImmutable(path, bytes);
        }
    }

    /// <summary>Records the spawned pid into the fake tree so placement can succeed, then commits.</summary>
    private sealed class RememberTracking : IVerificationCustodyFiles
    {
        private readonly VerificationCustodyFiles _files = new();
        private readonly FakeContainment _containment;
        public RememberTracking(FakeContainment containment) => _containment = containment;
        public byte[]? Read(string path) => _files.Read(path);
        public void CommitImmutable(string path, byte[] bytes)
        {
            if (Path.GetFileName(path) == "tracking.json")
            {
                var root = JsonSerializer.Deserialize<CustodyRoot>(bytes,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                _containment.Pids.Add(root.Pid);
            }

            _files.CommitImmutable(path, bytes);
        }
    }
}
