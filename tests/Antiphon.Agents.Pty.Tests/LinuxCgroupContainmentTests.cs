using System.Diagnostics;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

// CARD-0604 D-17. The Linux containment's three jobs, driven on Windows through its operations
// seam: rewrite the launch so the child lands inside a root-owned cgroup it cannot leave, read
// what that cgroup itself says is alive, and terminate the whole tree through the kill helper.
//
// The argv shape is the security property here. `sudo -n <shim> <id> -- <exe> <args>` is what
// makes placement happen BEFORE the child's first instruction -- the shim writes its own pid into
// the tree and then execs, so there is no window in which a started process is outside custody.
// Losing the `--`, or letting the class reach for a relative `antiphon-custody-enter` a session
// could shadow on PATH, would each quietly undo that.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LinuxCgroupContainmentTests
{
    [Test]
    public void Place_prefixes_the_root_owned_shim_with_a_separator()
    {
        var id = Guid.NewGuid();
        var containment = new LinuxCgroupContainment(id, new FakeOperations());

        var placed = containment.Place(new PtyTrackedLaunch("/usr/local/bin/pwsh", ["-NoProfile", "-File", "run.ps1"]));

        placed.App.ShouldBe("sudo");
        placed.CommandLine.ShouldBe([
            "-n",
            "/usr/local/bin/antiphon-custody-enter",
            id.ToString("D"),
            "--",
            "/usr/local/bin/pwsh",
            "-NoProfile",
            "-File",
            "run.ps1",
        ]);
    }

    // The tracked command's own arguments must never be read as the shim's. An argument that
    // looks like a flag is the case that proves the separator is doing work.
    [Test]
    public void Place_keeps_a_flag_shaped_child_argument_on_the_child_side()
    {
        var containment = new LinuxCgroupContainment(Guid.NewGuid(), new FakeOperations());

        var placed = containment.Place(new PtyTrackedLaunch("/bin/sh", ["--probe", "-c", "echo hi"]));

        var separator = placed.CommandLine.ToList().IndexOf("--");
        separator.ShouldBe(3);
        placed.CommandLine.Skip(separator + 1).ShouldBe(["/bin/sh", "--probe", "-c", "echo hi"]);
    }

    [Test]
    public void Read_active_reports_the_trees_own_population()
    {
        var operations = new FakeOperations { Pids = [41, 42, 43] };
        var containment = new LinuxCgroupContainment(Guid.NewGuid(), operations);

        var population = containment.ReadActive();

        population.Count.ShouldBe(3u);
        population.Pids.ShouldBe([41, 42, 43]);
        operations.AskedFor.ShouldBe(containment.ContainerId);
    }

    // An unreadable tree is unknown, never an empty one: a fabricated zero here is the whole
    // failure CARD-0598 exists to prevent, because the seal would turn it into terminal evidence.
    [Test]
    public void An_unreadable_tree_throws_rather_than_reporting_zero()
    {
        var containment = new LinuxCgroupContainment(Guid.NewGuid(),
            new FakeOperations { ReadFailure = new IOException("verification_custody_tree_unreadable") });

        Should.Throw<IOException>(() => containment.ReadActive());
    }

    [Test]
    public void Terminate_goes_through_the_kill_helper_and_reports_the_outcome()
    {
        var operations = new FakeOperations { Termination = new PtyTerminationObservation(false, 4) };
        var containment = new LinuxCgroupContainment(Guid.NewGuid(), operations);

        var observation = containment.Terminate();

        observation.Succeeded.ShouldBeFalse();
        observation.ErrorCode.ShouldBe(4);
        operations.Killed.ShouldBe(containment.ContainerId);
    }

    // The container id is the cgroup's directory name and the receipt's ContainerId. An empty
    // GUID would name the custody root itself.
    [Test]
    public void An_empty_container_id_is_refused()
    {
        Should.Throw<ArgumentException>(() => new LinuxCgroupContainment(Guid.Empty, new FakeOperations()));
    }

    [Test]
    public void C1614_Namespace_identity_cannot_cross_container_roots()
    {
        var execution = Guid.NewGuid();
        var current = Guid.NewGuid();
        var other = Guid.NewGuid();
        var currentPath = LinuxCustodyOperations.ResolveProcsPath(current, execution);
        var otherPath = LinuxCustodyOperations.ResolveProcsPath(other, execution);
        var legacyPath = LinuxCustodyOperations.ResolveProcsPath(Guid.Empty, execution);

        currentPath.ShouldContain(current.ToString("D"));
        otherPath.ShouldContain(other.ToString("D"));
        currentPath.ShouldNotBe(otherPath);
        legacyPath.ShouldNotBe(currentPath);
        LinuxCgroupContainment.ExecutionProcsPath(current, execution).ShouldBe(currentPath);
    }

    // The production reader must use the runtime namespace. A legacy tree at the old
    // global root is a different container and must not be reported as this one.
    [Test]
    public void C1614_Production_reader_uses_namespace_and_refuses_unknown()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1614-reader-" + Guid.NewGuid().ToString("N"));
        var ns = Guid.NewGuid();
        var execution = Guid.NewGuid();
        var marker = Path.Combine(root, "run", "namespace");
        try
        {
            WriteProcs(root, Path.Combine(ns.ToString("D"), execution.ToString("D")), "41\n42\n");
            WriteProcs(root, execution.ToString("D"), "99\n");
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, ns.ToString("D") + "\n");

            var operations = new LinuxCustodyOperations(new LinuxCustodyLayout(root, marker, null));
            operations.ReadTreePids(execution).ShouldBe([41, 42]);
            // test.PENDING slice B (CP-34): a missing tree directory, a missing
            // custody root, or a marker that names a different namespace still
            // returns an empty list. No Linux custody caller reaches ReadTreePids
            // while qualification is false: Detect returns null before a tracked
            // launch, and the entrypoint never sets LinuxCustodyQualified. Slice B
            // owns the strict-read Unknown. This empty result is not that witness.
            operations.ReadTreePids(Guid.NewGuid()).ShouldBeEmpty();

            var malformed = Guid.NewGuid();
            WriteProcs(root, Path.Combine(ns.ToString("D"), malformed.ToString("D")), "nope\n");
            Should.Throw<IOException>(() => operations.ReadTreePids(malformed))
                .Message.ShouldBe("verification_custody_tree_unreadable");

            File.WriteAllText(marker, "not-a-guid\n");
            Should.Throw<IOException>(() => operations.ReadTreePids(execution))
                .Message.ShouldBe("verification_custody_tree_unreadable");
            File.WriteAllText(marker, Guid.Empty.ToString("D") + "\n");
            Should.Throw<IOException>(() => operations.ReadTreePids(execution))
                .Message.ShouldBe("verification_custody_tree_unreadable");

            File.Delete(marker);
            Should.Throw<IOException>(() => operations.ReadTreePids(execution))
                .Message.ShouldBe("verification_custody_tree_unreadable");

            var record = Path.Combine(root, "linux-namespace.json");
            File.WriteAllText(record,
                "{\"revision\":1,\"namespaceId\":\"" + ns.ToString("D") + "\",\"containerId\":\"" + execution.ToString("D") + "\"}");
            var fromRecord = new LinuxCustodyOperations(new LinuxCustodyLayout(root, marker, record));
            fromRecord.ReadTreePids(execution).ShouldBe([41, 42]);

            File.WriteAllText(marker, ns.ToString("D") + "\n");
            var missingProcs = Guid.NewGuid();
            Directory.CreateDirectory(Path.Combine(
                root, "pids", "antiphon-custody", ns.ToString("D"), missingProcs.ToString("D"), "tree"));
            Should.Throw<IOException>(() => operations.ReadTreePids(missingProcs))
                .Message.ShouldBe("verification_custody_tree_unreadable");

            if (OperatingSystem.IsLinux())
            {
                var elsewhere = Path.Combine(root, "elsewhere");
                File.WriteAllText(elsewhere, Guid.NewGuid().ToString("D") + "\n");
                var link = Path.Combine(root, "run", "linked-namespace");
                File.CreateSymbolicLink(link, elsewhere);
                var linked = new LinuxCustodyOperations(new LinuxCustodyLayout(root, link, record));
                Should.Throw<IOException>(() => linked.ReadTreePids(execution))
                    .Message.ShouldBe("verification_custody_tree_unreadable");

                var hidden = Guid.NewGuid();
                var hiddenProcs = Path.Combine(root, "pids", "antiphon-custody", ns.ToString("D"),
                    hidden.ToString("D"), "tree", "cgroup.procs");
                Directory.CreateDirectory(Path.GetDirectoryName(hiddenProcs)!);
                File.WriteAllText(hiddenProcs, "7\n");
                File.SetUnixFileMode(hiddenProcs, UnixFileMode.None);
                try
                {
                    Should.Throw<IOException>(() => operations.ReadTreePids(hidden))
                        .Message.ShouldBe("verification_custody_tree_unreadable");
                }
                finally
                {
                    File.SetUnixFileMode(hiddenProcs, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // The installed enter helper writes the namespaced tree. The production
    // reader must return that pid, not a decoy left at the pre-namespace path.
    [Test]
    public async Task C1614_Helper_written_tree_is_read_by_production_reader()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The custody helper tree is created with unshare on Linux only.");

        var root = Path.Combine(Path.GetTempPath(), "c1614-joint-" + Guid.NewGuid().ToString("N"));
        var ns = Guid.NewGuid();
        var execution = Guid.NewGuid();
        var cgroup = Path.Combine(root, "cgroup");
        var marker = Path.Combine(root, "run", "antiphon-custody", "namespace");
        var tree = Path.Combine(cgroup, "pids", "antiphon-custody", ns.ToString("D"), execution.ToString("D"), "tree");
        var procs = Path.Combine(tree, "cgroup.procs");
        try
        {
            Directory.CreateDirectory(tree);
            Directory.CreateDirectory(Path.Combine(
                cgroup, "freezer", "antiphon-custody", ns.ToString("D"), execution.ToString("D"), "tree"));
            File.WriteAllText(procs, "");
            File.WriteAllText(Path.Combine(
                cgroup, "freezer", "antiphon-custody", ns.ToString("D"), execution.ToString("D"), "tree", "cgroup.procs"), "");
            var decoyDir = Path.Combine(cgroup, "pids", "antiphon-custody", execution.ToString("D"), "tree");
            Directory.CreateDirectory(decoyDir);
            File.WriteAllText(Path.Combine(decoyDir, "cgroup.procs"), "99\n");
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, ns.ToString("D") + "\n");
            var usr = Path.Combine(root, "usr", "lib");
            Directory.CreateDirectory(usr);
            File.Copy(CustodyLibrary(), Path.Combine(usr, "antiphon-custody-common.sh"));

            var placed = await PlaceTreeAsync(root, execution);
            placed.ExitCode.ShouldBe(0, placed.Text);
            placed.Text.ShouldContain("enter_rc=");
            File.ReadAllText(procs).Trim().ShouldNotBe("");

            var operations = new LinuxCustodyOperations(new LinuxCustodyLayout(cgroup, marker, null));
            var pids = operations.ReadTreePids(execution);
            pids.Count.ShouldBe(1);
            pids[0].ShouldBeGreaterThan(0);
            pids.ShouldNotContain(99);
            int.Parse(File.ReadAllText(procs).Trim(), System.Globalization.CultureInfo.InvariantCulture)
                .ShouldBe(pids[0]);
            LinuxCustodyOperations.ResolveProcsPath(ns, execution).ShouldContain(ns.ToString("D"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string CustodyLibrary()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "docker", "session-runner-grok", "antiphon-custody-common.sh");
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("antiphon-custody-common.sh");
    }

    private static async Task<(int ExitCode, string Text)> PlaceTreeAsync(string root, Guid execution)
    {
        var libraryDir = Path.GetDirectoryName(CustodyLibrary())!;
        var enter = Path.Combine(libraryDir, "antiphon-custody-enter.sh");
        var psi = new ProcessStartInfo("unshare")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-rm");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("""
            set -euo pipefail
            host=$1
            enter=$2
            id=$3
            mount --bind "$host/cgroup" /sys/fs/cgroup
            mount --bind "$host/run" /run
            mount --bind "$host/usr" /usr/local
            set +e
            bash "$enter" "$id" -- /bin/true
            echo "enter_rc=$?"
            exit 0
            """);
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add(root);
        psi.ArgumentList.Add(enter);
        psi.ArgumentList.Add(execution.ToString("D"));
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("unshare");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new System.TimeoutException("antiphon-custody-enter");
        }

        var text = (await stdout) + (await stderr);
        return (process.ExitCode, text);
    }

    private static void WriteProcs(string root, string relative, string body)
    {
        var procs = Path.Combine(root, "pids", "antiphon-custody", relative, "tree", "cgroup.procs");
        Directory.CreateDirectory(Path.GetDirectoryName(procs)!);
        File.WriteAllText(procs, body);
    }

    [Test]
    public void The_helper_paths_are_absolute_and_image_owned()
    {
        LinuxCgroupContainment.EnterHelper.ShouldBe("/usr/local/bin/antiphon-custody-enter");
        LinuxCgroupContainment.KillHelper.ShouldBe("/usr/local/bin/antiphon-custody-kill");
        LinuxCgroupContainment.CustodyRootName.ShouldBe("antiphon-custody");
    }

    private sealed class FakeOperations : ILinuxCustodyOperations
    {
        public IReadOnlyList<int> Pids { get; init; } = [];
        public Exception? ReadFailure { get; init; }
        public PtyTerminationObservation Termination { get; init; } = new(true, 0);
        public Guid AskedFor { get; private set; }
        public Guid Killed { get; private set; }

        public IReadOnlyList<int> ReadTreePids(Guid containerId)
        {
            AskedFor = containerId;
            if (ReadFailure is not null) throw ReadFailure;
            return Pids;
        }

        public PtyTerminationObservation Kill(Guid containerId)
        {
            Killed = containerId;
            return Termination;
        }
    }
}
