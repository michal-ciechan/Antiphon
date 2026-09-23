using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using System.Diagnostics;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-0604 D-17 (Cut B), V-30. The Linux custody backend against the REAL root-owned cgroup
/// and the real helpers, inside the persistent runner (CP-17's <c>linux-custody</c> shard).
///
/// <para>Windows-executing tests can say what the seam does with a fake
/// (<c>LinuxCgroupContainmentTests</c>), what the runner admits
/// (<c>RunnerCustodyLedgerBackendTests</c>) and what a receipt may claim
/// (<c>CustodyReceiptBackendTests</c>). None of them can say whether a double-forked,
/// <c>setsid</c>'d, reparented descendant is still inside the container, whether
/// <c>--no-new-privs</c> actually makes <c>sudo</c> inert for the tracked tree, or whether the
/// kill helper empties it. Those are kernel behaviours, and CARD-0598 asked for them to be
/// measured rather than asserted. That is why a Linux image run is still not a custody receipt
/// until this shard has executed.</para>
///
/// <para>Every method needs the entrypoint-prepared custody root and the two root-owned helpers,
/// so everything else skips. A skip here is not a pass: CP-17 requires each method to have
/// executed.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("LinuxCustody")]
[ParallelLimiter<ProcessSpawnLimit>]
public class LinuxCgroupCustodyTests
{
    private const string EnterHelper = LinuxCgroupContainment.EnterHelper;
    private const string KillHelper = LinuxCgroupContainment.KillHelper;
    private const string CustodyRootName = LinuxCgroupContainment.CustodyRootName;

    /// <summary>
    /// The container the child must not be able to leave has to exist before any of this is
    /// worth measuring. Skipping is the only honest answer off the runner; it is never a pass.
    /// </summary>
    private static void RequireLinuxCustody()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The linux-cgroup-v1 backend executes on Linux only.");
        if (!File.Exists(EnterHelper) || !File.Exists(KillHelper))
            throw new SkipTestException("Requires the root-owned custody helpers (session-testing image).");
        if (!Directory.Exists(CustodyRoot()))
            throw new SkipTestException("Requires the delegated custody root at " + CustodyRoot() + ".");
    }

    // V-30 (a): the child of a tracked launch is inside the tree before it runs, and the pid the
    // host recorded as the tracked root is the pid the container itself reports.
    [Test]
    public async Task Tracked_launch_places_child_in_tree()
    {
        RequireLinuxCustody();
        await using var world = await CustodyWorld.StartAsync();
        var launched = await world.LaunchAsync("echo placed-marker", "sleep 120");
        launched.ChildPid.ShouldBeGreaterThan(0);

        var pids = await world.WaitForTreeAsync(atLeast: 1);
        pids.ShouldContain(launched.ChildPid,
            "the shim writes its own pid before it execs, so the spawned pid is the tracked root");
        // The seam reads the same file the container owns, not a process-name census.
        world.Containment.ReadActive().Count.ShouldBeGreaterThanOrEqualTo(1u);
        (await world.CustodyAsync()).State.ShouldBe(VerificationCustodyState.Tracking);
    }

    // V-30 (b): the escape CARD-0598 named. A double fork plus setsid leaves the process group,
    // the session and the parent behind; cgroup membership is inherited at fork and cannot be
    // dropped, so the descendant is still in the tree.
    [Test]
    public async Task Double_fork_and_setsid_stay_in_tree()
    {
        RequireLinuxCustody();
        await using var world = await CustodyWorld.StartAsync();
        var daemonPidFile = Path.Combine(world.Scratch, "daemon.pid");
        await world.LaunchAsync(
            "setsid sh -c 'echo $$ > \"" + daemonPidFile + "\"; exec sleep 120' < /dev/null > /dev/null 2>&1 &",
            "sleep 120");

        var daemonPid = await WaitForPidFileAsync(daemonPidFile);
        SessionIdOf(daemonPid).ShouldBe(daemonPid, "setsid must actually have made it a session leader");

        var pids = await world.WaitForTreeAsync(atLeast: 2);
        pids.ShouldContain(daemonPid,
            "a descendant that left the session and the process group is still inside the cgroup");
        SessionIdOf(world.RootPid).ShouldNotBe(daemonPid,
            "the descendant really is outside the tracked root's session");
    }

    // V-30 (c): the root's own exit is not the container's. Until the seal empties the tree there
    // is no receipt, and nothing infers one from "the process we launched has gone".
    [Test]
    public async Task Root_exit_with_live_descendants_is_draining_until_seal()
    {
        RequireLinuxCustody();
        await using var world = await CustodyWorld.StartAsync();
        var daemonPidFile = Path.Combine(world.Scratch, "daemon.pid");
        await world.LaunchAsync(
            "setsid sh -c 'echo $$ > \"" + daemonPidFile + "\"; exec sleep 120' < /dev/null > /dev/null 2>&1 &",
            "exit 0");

        var daemonPid = await WaitForPidFileAsync(daemonPidFile);
        await world.WaitForRootExitAsync();
        (await world.ReadTreeAsync()).ShouldContain(daemonPid,
            "the root exited and its orphan is still contained");

        // Nothing was sealed and nothing was certified by the exit alone.
        File.Exists(world.StorePath("producer-seal.json")).ShouldBeFalse();
        File.Exists(world.StorePath("producer-receipt.json")).ShouldBeFalse();

        var observed = await world.CustodyAsync(seal: true);
        observed.State.ShouldBe(VerificationCustodyState.Exited);
        observed.Receipt.ShouldNotBeNull();
        var receipt = world.Validate(observed);
        receipt.ActiveProcesses.ShouldBe(0u);
        receipt.ObservationMethod.ShouldBe(VerificationCustodyBackends.CgroupProcsEmpty);
        (await world.ReadTreeAsync()).ShouldBeEmpty();
    }

    // V-30 (d): the seal is what terminates on Linux, and the zero it reports is read back out of
    // the tree afterwards rather than assumed from the helper's exit code.
    [Test]
    public async Task Seal_terminates_and_observes_zero()
    {
        RequireLinuxCustody();
        await using var world = await CustodyWorld.StartAsync();
        await world.LaunchAsync("echo still-running", "sleep 300");
        var pids = await world.WaitForTreeAsync(atLeast: 1);

        var status = await world.CustodyAsync(seal: true);
        status.State.ShouldBe(VerificationCustodyState.Exited);
        var receipt = world.Validate(status);
        receipt.ActiveProcesses.ShouldBe(0u);
        receipt.OutputDrained.ShouldBeTrue();
        receipt.ObservationMethod.ShouldBe(VerificationCustodyBackends.CgroupProcsEmpty);
        receipt.TerminationSucceeded.ShouldBe(true);
        (await world.ReadTreeAsync()).ShouldBeEmpty();
        foreach (var pid in pids)
            Directory.Exists("/proc/" + pid).ShouldBeFalse("pid " + pid + " survived the seal");
    }

    // V-30 (e): the containment is one the tracked tree cannot undo. --no-new-privs makes every
    // setuid binary inert for the whole descendant tree, so the child cannot reach the helpers,
    // and the root-owned procs files refuse its writes.
    [Test]
    public async Task Child_cannot_sudo_or_move_cgroup()
    {
        RequireLinuxCustody();
        await using var world = await CustodyWorld.StartAsync();
        var report = Path.Combine(world.Scratch, "escape.txt");
        var root = CustodyRoot();
        await world.LaunchAsync(
            "sudo -n true > /dev/null 2>&1; echo sudo=$? >> \"" + report + "\"",
            "sudo -n " + EnterHelper + " --probe > /dev/null 2>&1; echo shim=$? >> \"" + report + "\"",
            "echo $$ > \"" + root + "/cgroup.procs\" 2>/dev/null; echo root=$? >> \"" + report + "\"",
            "echo $$ > /sys/fs/cgroup/cgroup.procs 2>/dev/null; echo unified=$? >> \"" + report + "\"",
            "echo done >> \"" + report + "\"",
            "sleep 120");

        var lines = await WaitForLineAsync(report, "done");
        Outcome(lines, "sudo").ShouldNotBe(0, "no_new_privs must make sudo inert inside the tree");
        Outcome(lines, "shim").ShouldNotBe(0, "the child must not be able to re-enter the shim");
        Outcome(lines, "root").ShouldNotBe(0, "the custody root's procs file is root-owned");
        Outcome(lines, "unified").ShouldNotBe(0, "the child must not be able to move itself out");
        // It is still where it was put, having failed to leave.
        (await world.ReadTreeAsync()).ShouldNotBeEmpty();
    }

    // V-30 (f) / R-12: a host that disappears before it sealed produces no receipt, and no later
    // reader invents one. Unknown is a real answer; a fabricated Exited is the failure mode
    // CARD-0598 exists to prevent.
    [Test]
    public async Task Lost_host_leaves_unknown_not_receipt()
    {
        RequireLinuxCustody();
        var world = await CustodyWorld.StartAsync();
        try
        {
            await world.LaunchAsync("echo tracked", "sleep 300");
            await world.WaitForTreeAsync(atLeast: 1);
            File.Exists(world.StorePath("tracking.json")).ShouldBeTrue();

            await world.Host.DisposeAsync();
            File.Exists(world.StorePath("producer-receipt.json"))
                .ShouldBeFalse("a lost host never leaves terminal evidence");

            // A second host over the same store cannot answer for an execution it never launched.
            await using var successor = HostHarness.Start(o => o with
            {
                PtyBackend = "inbox",
                CustodyStoreRoot = world.Host.Options.CustodyStoreRoot,
                CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            });
            (await Should.ThrowAsync<VerificationCustodyException>(
                () => successor.Session.GetCustodyAsync(world.Binding, CancellationToken.None))).Code
                .ShouldBe("verification_custody_identity_mismatch");
            File.Exists(world.StorePath("producer-receipt.json")).ShouldBeFalse();
            File.Exists(world.StorePath("accepted-receipt.json")).ShouldBeFalse();
        }
        finally { await world.DisposeAsync(); }
    }

    // G-37: the host performs exactly the execution the runner reserved for it. A binding from
    // another session generation is refused before any placement, so no cgroup is created and no
    // start intent is recorded for it.
    [Test]
    public async Task Generation_mismatch_is_refused()
    {
        RequireLinuxCustody();
        await using var world = await CustodyWorld.StartAsync();
        var now = DateTime.UtcNow;
        var foreign = world.Binding with
        {
            ExecutionId = Guid.NewGuid(),
            Generation = new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
        };
        world.Store.Reserve(foreign);
        world.Store.WriteRecord(foreign, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));

        (await world.Host.Session.LaunchAsync(world.Spec(foreign, "sleep 120"), CancellationToken.None))
            .ShouldBeOfType<ErrorMessage>();
        File.Exists(world.StorePath("native-start-intent.json", foreign)).ShouldBeFalse();
        Directory.Exists(Path.Combine(CustodyRoot(), foreign.ExecutionId.ToString("D"))).ShouldBeFalse(
            "a refused binding must never reach the placement shim");
    }

    // The runner retries a custody read it did not get an answer to. The second seal must return
    // the same durable document, not run the kill helper again against a tree that is now gone.
    [Test]
    public async Task Second_seal_is_idempotent()
    {
        RequireLinuxCustody();
        await using var world = await CustodyWorld.StartAsync();
        await world.LaunchAsync("echo idempotent-marker", "sleep 300");
        await world.WaitForTreeAsync(atLeast: 1);

        var first = await world.CustodyAsync(seal: true);
        first.State.ShouldBe(VerificationCustodyState.Exited);
        first.Receipt.ShouldNotBeNull();

        var second = await world.CustodyAsync(seal: true);
        second.State.ShouldBe(VerificationCustodyState.Exited);
        second.Receipt.ShouldBe(first.Receipt, "a re-read returns the stored bytes, not a new observation");
        world.Validate(second).ObservedAtUtc.ShouldBe(world.Validate(first).ObservedAtUtc);
    }

    // --- helpers -------------------------------------------------------------------------

    private static string CustodyRoot() => File.Exists("/sys/fs/cgroup/cgroup.controllers")
        ? "/sys/fs/cgroup/" + CustodyRootName
        : "/sys/fs/cgroup/pids/" + CustodyRootName;

    private static async Task<int> WaitForPidFileAsync(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) && int.TryParse((await File.ReadAllTextAsync(path)).Trim(), out var pid) && pid > 0)
                return pid;
            await Task.Delay(100);
        }

        throw new System.TimeoutException("the payload never wrote " + path);
    }

    private static async Task<string[]> WaitForLineAsync(string path, string sentinel)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var lines = await File.ReadAllLinesAsync(path);
                if (lines.Any(line => line.Trim() == sentinel)) return lines;
            }
            await Task.Delay(100);
        }

        throw new System.TimeoutException("the payload never wrote '" + sentinel + "' to " + path);
    }

    private static int Outcome(string[] lines, string key)
    {
        var line = lines.LastOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the payload reported no '" + key + "' outcome");
        return int.Parse(line[(key.Length + 1)..].Trim());
    }

    /// <summary>Session id from /proc/&lt;pid&gt;/stat, parsed after the comm field's closing paren.</summary>
    private static int SessionIdOf(int pid)
    {
        var stat = File.ReadAllText("/proc/" + pid + "/stat");
        var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // After comm: state(0) ppid(1) pgrp(2) session(3).
        return int.Parse(fields[3]);
    }

    /// <summary>One tracked execution against the real helpers, with its own store and snapshot.</summary>
    private sealed class CustodyWorld : IAsyncDisposable
    {
        private CustodyWorld(HostHarness host, VerificationCustodyStore store,
            VerificationExecutionBinding binding, string scratch)
        {
            Host = host;
            Store = store;
            Binding = binding;
            Scratch = scratch;
        }

        public HostHarness Host { get; }
        public VerificationCustodyStore Store { get; }
        public VerificationExecutionBinding Binding { get; }
        public string Scratch { get; }
        public int RootPid { get; private set; }

        private Guid? TrackedContainerId => Store.ReadRecord<CustodyRoot>(Binding, "tracking.json")?.ContainerId;

        public IPtyCustodyContainment Containment => new LinuxCgroupContainment(
            TrackedContainerId ?? throw new InvalidOperationException("The host has not recorded tracking.json."));

        public static Task<CustodyWorld> StartAsync()
        {
            // The Linux lane spawns through Porta, so the pty backend name here is only the
            // pseudoconsole; the containment is the cgroup, stated separately and independently.
            var host = HostHarness.Start(o => o with
            {
                PtyBackend = "inbox",
                CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
                CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            });
            var store = new VerificationCustodyStore(host.Options.CustodyStoreRoot!);
            var snapshot = Path.Combine(host.TempDir, "snapshot");
            var scratch = Path.Combine(host.TempDir, "scratch");
            Directory.CreateDirectory(snapshot);
            Directory.CreateDirectory(scratch);
            var now = DateTime.UtcNow;
            var binding = new VerificationExecutionBinding(Guid.NewGuid(),
                new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new(host.SessionId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
                new(host.TempDir, Path.Combine(host.TempDir, ".git"), snapshot,
                    Path.Combine(host.TempDir, ".git", "worktrees", "snapshot"), "feat/linux-custody", Guid.NewGuid()),
                VerificationCustodyBackends.LinuxCgroup, RunnerStoreId: store.StoreId);
            store.Reserve(binding);
            store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
            return Task.FromResult(new CustodyWorld(host, store, binding, scratch));
        }

        /// <summary>A tracked launch of a temp shell script; the shim is prepended by the seam.</summary>
        public LaunchMessage Spec(VerificationExecutionBinding binding, params string[] lines)
        {
            var script = Path.Combine(Scratch, "payload-" + Guid.NewGuid().ToString("N") + ".sh");
            File.WriteAllText(script, "#!/bin/sh\nset -u\n" + string.Join('\n', lines) + "\n");
            return new LaunchMessage("/bin/sh", [script], new Dictionary<string, string>(),
                binding.Creation.WorktreePath, 120, 30, MemoryLimitMb: 0, TranscriptEnabled: false,
                Host.AnsiLogPath, AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow))
            {
                VerificationBinding = binding,
                RunnerStoreId = Store.StoreId,
            };
        }

        public async Task<LaunchedMessage> LaunchAsync(params string[] lines)
        {
            var reply = await Host.Session.LaunchAsync(Spec(Binding, lines), CancellationToken.None);
            var launched = reply.ShouldBeOfType<LaunchedMessage>();
            RootPid = launched.ChildPid;
            return launched;
        }

        public Task<VerificationCustodyStatus> CustodyAsync(bool seal = false) =>
            Host.Session.GetCustodyAsync(Binding, seal, CancellationToken.None);

        public string StorePath(string file, VerificationExecutionBinding? binding = null) =>
            Store.PathFor((binding ?? Binding).ExecutionId, file);

        public VerificationCustodyReceipt Validate(VerificationCustodyStatus status) =>
            new VerificationCustodyStore(Store.Root, Store.StoreId)
                .ValidateReceipt(status.Receipt!, Binding, status.Host!);

        public Task<IReadOnlyList<int>> ReadTreeAsync()
        {
            var procs = Path.Combine(CustodyRoot(),
                (TrackedContainerId ?? throw new InvalidOperationException("The host has not recorded tracking.json.")).ToString("D"),
                "tree", "cgroup.procs");
            if (!File.Exists(procs)) return Task.FromResult<IReadOnlyList<int>>([]);
            return Task.FromResult<IReadOnlyList<int>>(File.ReadAllLines(procs)
                .Where(line => line.Length != 0).Select(int.Parse).ToArray());
        }

        public async Task<IReadOnlyList<int>> WaitForTreeAsync(int atLeast)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            IReadOnlyList<int> pids = [];
            while (DateTime.UtcNow < deadline)
            {
                pids = await ReadTreeAsync();
                if (pids.Count >= atLeast) return pids;
                await Task.Delay(100);
            }

            throw new System.TimeoutException("the tree held " + pids.Count + " pids, expected at least " + atLeast);
        }

        public async Task WaitForRootExitAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (Host.Session.Status == PtyHostStatus.Exited) return;
                await Task.Delay(100);
            }

            throw new System.TimeoutException("the tracked root never exited");
        }

        public async ValueTask DisposeAsync()
        {
            // Never leave a populated cgroup behind: the next execution's "exists and is empty"
            // precondition would refuse for a reason nobody could see.
            try
            {
                var containerId = TrackedContainerId;
                if (containerId is not null)
                {
                    using var kill = Process.Start(new ProcessStartInfo(LinuxCgroupContainment.SudoExe)
                    {
                        ArgumentList = { "-n", KillHelper, containerId.Value.ToString("D") },
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    });
                    kill?.WaitForExit(45_000);
                }
            }
            catch
            {
                // Teardown is best-effort; the case's own assertions are the verdict.
            }

            // The lost-host case disposes the harness itself, so a second dispose is expected.
            try { await Host.DisposeAsync(); } catch (ObjectDisposedException) { }
        }
    }
}
