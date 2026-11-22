using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1386, in-process since W2-nightly S18: the run core's lock (Antiphon.Nightly/NightlyRunLock.cs) against a planted
/// run.lock, with the process table, boot id and host id Write-C1386Seams set. The cases ran the whole nightly run
/// before; the run core refuses with Phase "lock" and the lock's reason (NightlyRun.cs; nightly-run-impl.ps1 until W2-nightly S20), so "leaves the lock
/// phase" is an Ok lock result and the refusal is its Reason.
/// </summary>
[Category("Integration")]
public sealed class NightlyDeadOwnerLockTests
{
    [Test]
    public void C1386_DeadOwnerReclaimed()
    {
        using (var legacy = NightlyLockFixture.Create().Dead(424201, 424202))
        {
            legacy.Plant(424201, "legacy-run", parentPid: 424202);
            var r = legacy.Enter();
            var detail = NightlyLockFixture.Detail(r) + " reclaim=" + legacy.ReclaimReason();
            r.Ok.ShouldBeTrue("C1386 DeadOwner legacy leaves lock phase " + detail);
            legacy.ReclaimReason().ShouldBe("dead-owner-legacy", "C1386 DeadOwner legacy records dead-owner-legacy " + detail);
            legacy.RunIdGone("legacy-run").ShouldBeTrue("C1386 DeadOwner legacy replaces run id " + legacy.LockText());
        }

        using (var same = NightlyLockFixture.Create().Dead(424203, 424204))
        {
            same.Plant(424203, "same-boot-run", parentPid: 424204, bootId: "boot-current", hostId: "host-current");
            var r = same.Enter();
            var reason = same.ReclaimReason();
            (r.Ok && reason == "dead-owner-same-boot" && same.RunIdGone("same-boot-run"))
                .ShouldBeTrue("C1386 DeadOwner same-boot records dead-owner-same-boot " + NightlyLockFixture.Detail(r) + " reclaim=" + reason);
        }

        // The owner pid is alive again after a reboot (a recycled pid): the registry boot ids differ on the same host.
        using (var reboot = NightlyLockFixture.Create(boot: "157").Dead(424206))
        {
            reboot.Plant(424205, "reboot-run", parentPid: 424206, bootId: "156", hostId: "host-current");
            var r = reboot.Enter();
            var reason = reboot.ReclaimReason();
            var detail = NightlyLockFixture.Detail(r) + " reclaim=" + reason;
            (r.Ok && reboot.RunIdGone("reboot-run")).ShouldBeTrue("C1386 DeadOwner reboot reclaims recycled pid " + detail);
            reason.ShouldBe("dead-owner-rebooted", "C1386 DeadOwner reboot records dead-owner-rebooted " + detail);
        }
    }

    [Test]
    public void C1386_LiveOwnerRefused()
    {
        using (var fx = NightlyLockFixture.Create().Dead(424208))
        {
            fx.Plant(424207, "live-run", parentPid: 424208, bootId: "boot-current", hostId: "host-current");
            var r = fx.Enter();
            var detail = NightlyLockFixture.Detail(r);
            (!r.Ok && r.Reason == "live-owner").ShouldBeTrue("C1386 LiveOwner refusal is live-owner " + detail);
            fx.RunIdKept("live-run").ShouldBeTrue("C1386 LiveOwner keeps planted run id " + fx.LockText());
            fx.NoReclaim().ShouldBeTrue("C1386 LiveOwner writes no reclaim record " + detail);
        }

        // The record's process start no longer matches the live pid: still never stolen.
        using (var fx2 = NightlyLockFixture.Create().Dead(424210))
        {
            fx2.Plant(424209, "live-start-run", parentPid: 424210, bootId: "boot-current", hostId: "host-current",
                processStartedAt: "2001-01-01T00:00:00.0000000Z");
            var r2 = fx2.Enter();
            var kept = !r2.Ok && r2.Reason == "live-owner" && fx2.RunIdKept("live-start-run") && fx2.NoReclaim();
            kept.ShouldBeTrue("C1386 LiveOwner start mismatch stays live-owner " + NightlyLockFixture.Detail(r2) + " lock=" + fx2.LockText());
        }
    }

    [Test]
    public void C1386_AmbiguousOwnerRefused()
    {
        using (var parentAlive = NightlyLockFixture.Create().Dead(424210))
        {
            parentAlive.Plant(424210, "parent-alive-run", parentPid: 424211, bootId: "boot-current", hostId: "host-current");
            var r = parentAlive.Enter();
            (!r.Ok && r.Reason == "lock-held").ShouldBeTrue("C1386 Ambiguous parent-alive refuses lock-held " + NightlyLockFixture.Detail(r));
            (parentAlive.RunIdKept("parent-alive-run") && parentAlive.NoReclaim())
                .ShouldBeTrue("C1386 Ambiguous parent-alive keeps the lock " + parentAlive.LockText());
        }

        using (var foreign = NightlyLockFixture.Create().Dead(424212, 424213))
        {
            foreign.Plant(424212, "foreign-host-run", parentPid: 424213, bootId: "boot-current", hostId: "host-other");
            var r = foreign.Enter();
            (!r.Ok && r.Reason == "lock-held").ShouldBeTrue("C1386 Ambiguous foreign-host refuses lock-held " + NightlyLockFixture.Detail(r));
            (foreign.RunIdKept("foreign-host-run") && foreign.NoReclaim())
                .ShouldBeTrue("C1386 Ambiguous foreign-host keeps the lock " + foreign.LockText());
        }

        using (var missing = NightlyLockFixture.Create().Dead(424214))
        {
            missing.Plant(424214, "missing-parent-run", omitParent: true, bootId: "boot-current", hostId: "host-current");
            var r = missing.Enter();
            (!r.Ok && r.Reason == "lock-held").ShouldBeTrue("C1386 Ambiguous missing-parent refuses lock-held " + NightlyLockFixture.Detail(r));
            (missing.RunIdKept("missing-parent-run") && missing.NoReclaim())
                .ShouldBeTrue("C1386 Ambiguous missing-parent keeps the lock " + missing.LockText());
        }
    }
}
