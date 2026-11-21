using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1389 and CARD-1396, in-process since W2-nightly S18: the reclaim of a dead owner's run.lock through one
/// exclusive open (Antiphon.Nightly/NightlyRunLock.cs). Two attempts are two NightlyRunLock objects with their own
/// handles and process ids; file sharing is per handle, so they race as two processes did.
/// </summary>
[Category("Integration")]
public sealed class NightlyReclaimRaceTests
{
    [Test]
    public void C1389_LoserReclaimRecord()
    {
        using var fx = NightlyLockFixture.Create();
        fx.Plant(424298, "stale-run", parentPid: 424290, bootId: "boot-current", hostId: "host-current");
        var snapshot = NightlyLockRecord.Read(fx.LockPath);
        using var loser = new NightlyRunLock(fx.Host);
        var first = fx.Lock.EnterReclaimed(fx.State, fx.LockPath, snapshot, "winner-run", "dead-owner-legacy", "boot-current", "host-current", NightlyParent.None);
        var second = loser.EnterReclaimed(fx.State, fx.LockPath, snapshot, "loser-run", "dead-owner-legacy", "boot-current", "host-current", NightlyParent.None);
        fx.Lock.Dispose();
        var lockRun = fx.LockRunId();
        var newRunId = fx.ReclaimNewRunId();
        var detail = "winner=winner-run loser=loser-run first " + NightlyLockFixture.Detail(first) + " second " + NightlyLockFixture.Detail(second)
            + " newRunId=" + newRunId + " lockRun=" + lockRun;
        (!second.Ok && lockRun == "winner-run").ShouldBeTrue("C1389 Record loser does not own the lock " + detail);
        newRunId.ShouldBe("winner-run", "C1389 Record winner newRunId remains " + detail);
    }

    [Test]
    public void C1389_DoubleReclaim()
    {
        using var fx = NightlyLockFixture.Create();
        File.WriteAllText(fx.LockPath, NightlyLockFixture.PlantJson(424301, "stale-run", parentPid: 424302, bootId: "boot-current", hostId: "host-current"));
        // Both attempts hold the snapshot taken before either mutates.
        var snapshot = NightlyLockRecord.Read(fx.LockPath);
        var tableA = new FakeProcessTable { CurrentPid = 5101, Boot = "boot-current", Host = "host-current" };
        var tableB = new FakeProcessTable { CurrentPid = 5102, Boot = "boot-current", Host = "host-current" };
        using var a = new NightlyRunLock(fx.Fixture.Host(processes: tableA));
        using var b = new NightlyRunLock(fx.Fixture.Host(processes: tableB));
        var resultA = a.EnterReclaimed(fx.State, fx.LockPath, snapshot, "A-run", "dead-owner-legacy", "boot-current", "host-current", NightlyParent.None);
        var resultB = b.EnterReclaimed(fx.State, fx.LockPath, snapshot, "B-run", "dead-owner-legacy", "boot-current", "host-current", NightlyParent.None);
        var owners = new List<string>();
        if (resultA.Ok) owners.Add("A-run");
        if (resultB.Ok) owners.Add("B-run");
        var lockRun = fx.LockRunId();
        var detail = "A-run " + NightlyLockFixture.Detail(resultA) + " B-run " + NightlyLockFixture.Detail(resultB) + " lockRun=" + lockRun;
        owners.Count.ShouldBe(1, "C1389 Race one attempt owns the lock " + detail);
        (owners.Count == 1 && lockRun == owners[0]).ShouldBeTrue("C1389 Race lock run id is that owner " + detail);
        resultB.Reason.ShouldBe("live-owner", "the loser re-reads a live owner " + detail);
    }

    [Test]
    public void C1389_BootSourcePinned()
    {
        // The registry BootId is the only source: once it is unreadable the boot id is empty, never a LastBootUpTime.
        var calls = 0;
        var registry = new NightlyProcessTable((key, name) =>
        {
            if (name != "BootId") return null;
            calls++;
            if (calls == 1) return 157;
            throw new IOException("registry BootId unreadable");
        });
        var firstBoot = registry.BootId();
        var secondBoot = registry.BootId();
        var single = calls >= 2 && (secondBoot == "" || secondBoot == "157");
        var bootDetail = calls == 0 ? "stub-not-hit" : "count=" + calls + " first=" + firstBoot + " second=" + secondBoot;
        single.ShouldBeTrue("C1389 Boot registry failure keeps a single source " + bootDetail);

        // A live owner whose boot id cannot be read now is never reclaimed as rebooted.
        using var fx = NightlyLockFixture.Create();
        var processes = new NightlyDelegateProcessTable(4242, pid => pid == 424299, _ => null, _ => NightlyParent.None, registry.BootId, () => "host-current");
        using var thief = new NightlyRunLock(fx.Fixture.Host(processes: processes));
        fx.Plant(424299, "planted-run", parentPid: 424300, bootId: "157", hostId: "host-current");
        var result = thief.Enter(fx.State, "thief-run");
        result.Reason.ShouldBe("live-owner", "C1389 Boot live owner stays live-owner " + NightlyLockFixture.Detail(result) + " second=" + secondBoot);
        (fx.RunIdKept("planted-run") && fx.NoReclaim()).ShouldBeTrue("C1389 Boot live owner writes no reclaim record " + fx.LockText());
    }

    [Test]
    public void C1389_ReclaimThenExitReleasesLock()
    {
        using var fx = NightlyLockFixture.Create(boot: "157").Dead(424401, 424402, 4242);
        fx.Plant(424401, "stale-run", parentPid: 424402, bootId: "157", hostId: "host-current");
        var first = fx.Enter("reclaim-run");
        (first.Ok && first.Reason == "reclaimed").ShouldBeTrue("C1389 Exit reclaim owns the lock " + NightlyLockFixture.Detail(first));
        fx.Lock.Exit(fx.State, "reclaim-run");
        File.Exists(fx.LockPath).ShouldBeFalse("C1389 Exit removes the reclaimed lock " + fx.LockText());
        var second = fx.Enter("next-run");
        (second.Ok && second.Reason == "acquired").ShouldBeTrue("C1389 Exit next enter acquires " + NightlyLockFixture.Detail(second));
        if (second.Ok) fx.Lock.Exit(fx.State, "next-run");
        File.Exists(fx.LockPath).ShouldBeFalse("the next run's exit removes its lock");
    }

    [Test]
    public void C1389_HopChildReadsReclaimedLock()
    {
        using var fx = NightlyLockFixture.Create(boot: "157").Dead(424421, 424422);
        fx.Plant(424421, "stale-run", parentPid: 424422, bootId: "157", hostId: "host-current");
        var first = fx.Enter("reclaimed-run");
        (first.Ok && first.Reason == "reclaimed").ShouldBeTrue("C1389 Hop reclaim owns the lock " + NightlyLockFixture.Detail(first));
        // The self-reexec child: another process that reads the record while the parent holds its shared handle.
        var childTable = new FakeProcessTable { CurrentPid = 5201, Boot = "157", Host = "host-current" }
            .Start(fx.Processes.CurrentPid, new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc));
        using var child = new NightlyRunLock(fx.Fixture.Host(processes: childTable));
        var hop = child.Enter(fx.State, "hop-child", "reclaimed-run", fx.Processes.CurrentPid);
        var seen = hop.Record?.RunId ?? "";
        (seen == "reclaimed-run" && hop.Reason == "hop").ShouldBeTrue("C1389 Hop child reads the reclaimed run id reason=" + hop.Reason + " seen=" + seen + " want=reclaimed-run");
        hop.OwnsLock.ShouldBeFalse("the child inherits without owning");
        fx.Lock.Exit(fx.State, "reclaimed-run");
    }

    [Test]
    public void C1389_OldFormatBootIdRefuses()
    {
        using var fx = NightlyLockFixture.Create(boot: "157").Dead(424411);
        fx.Plant(424410, "old-format-run", parentPid: 424411, bootId: "2026-01-02T03:04:05.0000000Z", hostId: "host-current");
        var result = fx.Enter("thief-run");
        (!result.Ok && result.Reason == "lock-held").ShouldBeTrue("C1389 OldFormat boot id refuses " + NightlyLockFixture.Detail(result));
        (fx.RunIdKept("old-format-run") && fx.NoReclaim()).ShouldBeTrue("C1389 OldFormat boot id keeps the lock " + fx.LockText());
    }

    [Test]
    public void C1396_SharedReopenLogged()
    {
        using var fx = NightlyLockFixture.Create(boot: "157").Dead(424431, 424432, 4242);
        fx.Plant(424431, "stale-run", parentPid: 424432, bootId: "157", hostId: "host-current");
        fx.Lock.OpenShared = _ => throw new IOException("shared reopen refused");
        var result = fx.Enter("reopen-run");
        (result.Ok && result.Reason == "reclaimed" && result.OwnsLock).ShouldBeTrue("C1396 Reopen failure keeps reclaim ownership " + NightlyLockFixture.Detail(result));
        fx.Lock.HoldsHandle.ShouldBeFalse("C1396 Reopen failure keeps no shared handle");
        fx.RunIdKept("reopen-run").ShouldBeTrue("C1396 Reopen failure keeps the published run id " + fx.LockText());
        var lines = fx.Fixture.Out.ToString();
        lines.ShouldContain("reclaim-shared-reopen-failed", Case.Sensitive, "C1396 Reopen failure logs reclaim-shared-reopen-failed " + lines);
        var trace = fx.Fixture.Trace.ToString();
        trace.ShouldContain("reclaim-shared-reopen-failed", Case.Sensitive, "C1396 Reopen failure traces reclaim-shared-reopen-failed " + trace);
    }
}
