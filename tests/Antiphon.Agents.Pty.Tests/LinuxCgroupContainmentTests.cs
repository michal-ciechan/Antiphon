using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

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
