using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// CARD-0604 D-17 / G-28. The runner may advertise linux-cgroup-v1 only when it can actually
// perform it. This is the difference between a SourceLanding Mutation that produces a receipt and
// one that reserves an execution row nothing can ever resolve -- which is the exact failure
// CARD-0598 names. The probe is therefore positive and live; these tests drive every way it can
// fail, on Windows, through the environment seam.
[Category("Unit")]
public sealed class LinuxCustodyProbeTests
{
    [Test]
    public void Backend_is_null_without_delegated_root()
    {
        // The entrypoint creates the custody root as root. Without it the placement shim has
        // nowhere to make a cgroup, so there is nothing to advertise.
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment { RootExists = false }).ShouldBeNull();
    }

    [Test]
    public void Backend_is_null_when_helper_probe_fails()
    {
        // Either helper failing is fatal: placement without termination cannot be sealed, and
        // termination without placement was never containment.
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment { EnterProbeOk = false }).ShouldBeNull();
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment { KillProbeOk = false }).ShouldBeNull();
    }

    // If this process is already under PR_SET_NO_NEW_PRIVS then sudo is inert for it, so the
    // helpers can never be reached -- however healthy everything else looks.
    [Test]
    public void Backend_is_null_when_the_runner_is_already_under_no_new_privs()
    {
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment { NoNewPrivsValue = 1 }).ShouldBeNull();
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment { NoNewPrivsValue = -1 }).ShouldBeNull();
    }

    [Test]
    public void Backend_is_null_off_linux()
    {
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment { Linux = false }).ShouldBeNull();
    }

    [Test]
    public void Backend_is_linux_cgroup_when_all_pass()
    {
        var environment = new FakeEnvironment();
        LinuxCgroupCustodyProbe.Detect(environment).ShouldBe(VerificationCustodyBackends.LinuxCgroup);

        // And it probed BOTH helpers, by their absolute image-owned paths -- not "whatever is on
        // PATH", which a session could shadow.
        environment.Probed.ShouldBe([
            LinuxCgroupCustodyProbe.EnterHelper, LinuxCgroupCustodyProbe.KillHelper]);
        LinuxCgroupCustodyProbe.EnterHelper.ShouldBe("/usr/local/bin/antiphon-custody-enter");
        LinuxCgroupCustodyProbe.KillHelper.ShouldBe("/usr/local/bin/antiphon-custody-kill");
    }

    [Test]
    public void The_probed_root_is_the_entrypoint_root()
    {
        var environment = new FakeEnvironment();
        LinuxCgroupCustodyProbe.Detect(environment);
        environment.RootAsked.ShouldBe("antiphon-custody");
    }

    private sealed class FakeEnvironment : ILinuxCustodyEnvironment
    {
        public bool Linux { get; init; } = true;
        public bool RootExists { get; init; } = true;
        public bool EnterProbeOk { get; init; } = true;
        public bool KillProbeOk { get; init; } = true;
        public int NoNewPrivsValue { get; init; }
        public List<string> Probed { get; } = [];
        public string? RootAsked { get; private set; }

        public bool IsLinux => Linux;
        public int NoNewPrivs => NoNewPrivsValue;

        public bool CustodyRootExists(string rootName)
        {
            RootAsked = rootName;
            return RootExists;
        }

        public bool HelperProbeSucceeds(string helperPath)
        {
            Probed.Add(helperPath);
            return helperPath == LinuxCgroupCustodyProbe.EnterHelper ? EnterProbeOk : KillProbeOk;
        }
    }
}
