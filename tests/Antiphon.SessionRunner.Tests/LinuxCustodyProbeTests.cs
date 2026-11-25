using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.PtyHost.Protocol;
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
            (LinuxCgroupCustodyProbe.EnterHelper, "--probe-placement-v1"),
            (LinuxCgroupCustodyProbe.KillHelper, "--probe")]);
        LinuxCgroupCustodyProbe.EnterHelper.ShouldBe("/usr/local/bin/antiphon-custody-enter");
        LinuxCgroupCustodyProbe.KillHelper.ShouldBe("/usr/local/bin/antiphon-custody-kill");
    }

    [Test]
    public void C1614_Unqualified_or_v2_custody_is_unadvertised()
    {
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment { Qualified = false }).ShouldBeNull();
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment
        {
            Qualified = true,
            Hierarchy = LinuxCustodyHierarchy.V2,
        }).ShouldBeNull();
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment
        {
            Qualified = true,
            Hierarchy = LinuxCustodyHierarchy.Unknown,
        }).ShouldBeNull();
        LinuxCgroupCustodyProbe.Detect(new FakeEnvironment
        {
            Qualified = true,
            Hierarchy = LinuxCustodyHierarchy.V1,
        }).ShouldBe(VerificationCustodyBackends.LinuxCgroup);
    }

    [Test]
    public void The_probed_root_is_the_entrypoint_root()
    {
        var environment = new FakeEnvironment();
        LinuxCgroupCustodyProbe.Detect(environment);
        environment.RootAsked.ShouldBe("antiphon-custody");
    }

    internal sealed class FakeEnvironment : ILinuxCustodyEnvironment
    {
        public bool Linux { get; init; } = true;
        public bool RootExists { get; init; } = true;
        public bool EnterProbeOk { get; init; } = true;
        public bool KillProbeOk { get; init; } = true;
        public int NoNewPrivsValue { get; init; }
        public bool Qualified { get; init; } = true;
        public LinuxCustodyHierarchy Hierarchy { get; init; } = LinuxCustodyHierarchy.V1;
        public bool LegacyOnly { get; init; }
        public List<(string Path, string Argument)> Probed { get; } = [];
        public string? RootAsked { get; private set; }

        public bool IsLinux => Linux;
        public int NoNewPrivs => NoNewPrivsValue;

        public bool CustodyRootExists(string rootName)
        {
            RootAsked = rootName;
            return RootExists;
        }

        public bool HelperProbeSucceeds(string helperPath, string probeArgument)
        {
            Probed.Add((helperPath, probeArgument));
            return helperPath == LinuxCgroupCustodyProbe.EnterHelper
                ? EnterProbeOk && (!LegacyOnly || probeArgument == "--probe")
                : KillProbeOk;
        }
    }
}

[Category("Unit")]
public sealed class Card2060PlacementLedgerTests
{
    [Test]
    public void LegacyProbeCannotQualify()
    {
        var environment = new LinuxCustodyProbeTests.FakeEnvironment { LegacyOnly = true };
        environment.HelperProbeSucceeds(LinuxCgroupCustodyProbe.EnterHelper, "--probe").ShouldBeTrue();
        environment.HelperProbeSucceeds(LinuxCgroupCustodyProbe.KillHelper, "--probe").ShouldBeTrue();
        LinuxCgroupCustodyProbe.Detect(environment).ShouldBeNull();
    }

    [Test]
    public async Task DeadHostPlacementAloneStaysUnknown()
    {
        await using var world = await RunnerCustodyReleaseTests.ReleaseWorld.Open(adopt: false);
        var boot = Guid.NewGuid();
        var root = new CustodyRoot(2, 12345, world.Host.HostStartTimeUtc, world.ContainerId, boot, 123u);
        world.Store.WriteRecord(world.Binding, "tracking.json", root);
        world.Store.WriteRecord(world.Binding, "linux-placement.json", new LinuxPlacementRecord(2, root.Pid,
            root.StartTimeUtc, world.ContainerId, world.NamespaceId, world.Store.StoreId,
            world.Binding.ExecutionId, world.Host.HostInstanceId, world.Binding.Generation, boot, 123, 3));
        world.Recovery.Gone = true;
        world.Recovery.Pids = [];

        var status = await world.Runtime.ReadCustodyAsync(world.Binding, false, CancellationToken.None);
        status.State.ShouldBe(VerificationCustodyState.Unknown);
        status.Receipt.ShouldBeNull();
        world.Recovery.Kills.ShouldBeEmpty();
    }

    [Test]
    public async Task ImportRequiresOriginalBytes()
    {
        await using var world = await RunnerCustodyReleaseTests.ReleaseWorld.Open(adopt: false);
        var at = world.Host.HostStartTimeUtc;
        var tracking = new CustodyRoot(2, 12345, at, world.ContainerId);
        world.Store.WriteRecord(world.Binding, "runner-start-intent.json", new CustodyStamp(1, at));
        world.Store.WriteRecord(world.Binding, "tracking.json", tracking);
        world.Store.WriteRecord(world.Binding, "producer-seal.json", new CustodyStamp(3, at));
        var receipt = new VerificationCustodyReceipt(1, world.Binding, world.Host, 4, at, at,
            VerificationCustodyBackends.CgroupProcsEmpty, 0, true, VerificationCustodyState.Exited,
            tracking.Pid, tracking.StartTimeUtc);
        var producer = world.Store.SaveProducer(receipt);
        byte[] altered = [.. producer, (byte)'\n'];
        world.Store.ValidateReceipt(altered, world.Binding, world.Host).ShouldBe(receipt);
        var status = new VerificationCustodyStatus(world.Binding, VerificationCustodyState.Exited,
            null, world.Host, altered);
        Should.Throw<VerificationCustodyException>(() => world.Runtime.CustodyForTests.Accept(status))
            .Code.ShouldBe("verification_custody_integrity_error");
        world.Store.ReadReceipt(world.Binding, world.Host, accepted: true).ShouldBeNull();

        (await world.Runtime.ReadCustodyAsync(world.Binding, false, CancellationToken.None))
            .Receipt.ShouldBe(producer);
        world.Store.ReadReceipt(world.Binding, world.Host, accepted: true).ShouldBe(producer);
    }

    [Test]
    public async Task ExplicitReleaseRefusesLiveCustodian()
    {
        await using var world = await RunnerCustodyReleaseTests.ReleaseWorld.Open(adopt: false);
        world.Recovery.Gone = false;
        world.Recovery.Pids = [];
        var status = await world.Runtime.ReadCustodyAsync(world.Binding, true, CancellationToken.None);
        world.Recovery.Kills.ShouldBeEmpty();
        status.Reason.ShouldBe("custody_host_still_present");
        status.Receipt.ShouldBeNull();
    }

    [Test]
    public async Task OrdinaryReadNeverInvokesRecoveryKill()
    {
        await using var world = await RunnerCustodyReleaseTests.ReleaseWorld.Open(adopt: false);
        world.Store.WriteRecord(world.Binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
        world.Recovery.Gone = true;
        world.Recovery.Pids = [];
        var status = await world.Runtime.ReadCustodyAsync(world.Binding, false, CancellationToken.None);
        world.Recovery.Kills.ShouldBeEmpty();
        status.State.ShouldBe(VerificationCustodyState.Unknown);
        status.Receipt.ShouldBeNull();

        (await world.Runtime.ReadCustodyAsync(world.Binding, true, CancellationToken.None)).Receipt.ShouldBeNull();
        world.Recovery.Kills.ShouldBe([world.ContainerId]);
    }
}
