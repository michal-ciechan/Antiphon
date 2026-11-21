using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-2, V-3 and R-4. Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION credit predicate, the production
/// run core and the production shared lock, using private clone/state/release/coordination roots;
/// C599_SharedLock and C599_Continuation call the compiled shared lock in-process (W2-nightly S18).
///
/// <para>The decisive behaviours: master green and RC green are separate kinds of credit that
/// cannot be exchanged, an RC run leaves all four master state files byte-identical, the pinned
/// SHA and profile survive every wrapper and self-reexec hop, and a live lock owner is never
/// stolen on age - a contended lane records <c>deferred-busy</c> instead.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateRunTests
{
    [Test]
    public Task C599_SelectionHopExitCodes() => RunCaseAsync("C599_SelectionHopExitCodes", 3,
        "C599 SelectionHopExitCodes child exit 0 and spaced argument survive",
        "C599 SelectionHopExitCodes child exit 4 and spaced argument survive",
        "C599 SelectionHopExitCodes child exit 7 and spaced argument survive");

    [Test]
    public Task C599_CreditIdentity() => RunCaseAsync("C599_CreditIdentity", 34,
        "C599 CreditIdentity nightly/scheduled/master earns [master-scheduled]",
        "C599 CreditIdentity rc/rc/release/rc-20260923T083000Z earns [rc-release]",
        "C599 CreditIdentity rc/rc/master earns []",
        "C599 CreditIdentity nightly/scheduled/release/rc-20260923T083000Z earns []",
        "C599 CreditIdentity malformed [rc/rc/release/current] earns nothing",
        "C599 CreditIdentity absent profile is still the master lane");

    [Test]
    public Task C599_CreditVerdicts() => RunCaseAsync("C599_CreditVerdicts", 24,
        "C599 CreditVerdicts control master green earns master credit",
        "C599 CreditVerdicts master green never earns release credit",
        "C599 CreditVerdicts control rc green earns release credit",
        "C599 CreditVerdicts rc green never earns master credit",
        "C599 CreditVerdicts coverageComplete as the STRING \"true\" earns nothing",
        "C599 CreditVerdicts rc sha mismatch earns nothing",
        "C599 CreditVerdicts an invented credit kind earns nothing");

    [Test]
    public Task C599_MasterStateIsolation() => RunCaseAsync("C599_MasterStateIsolation", 11,
        "C599 MasterStateIsolation run starts without child summaries",
        "C599 MasterStateIsolation outer run reaches self-reexec without refusal",
        "C599 MasterStateIsolation exactly one fresh child summary names this run",
        "C599 MasterStateIsolation child consumed the pinned RC identity",
        "C599 MasterStateIsolation child consumed the bound Full selection",
        "C599 MasterStateIsolation child selection matches the immutable bound artifacts",
        "C599 MasterStateIsolation last-run.json is byte-identical after an rc run",
        "C599 MasterStateIsolation last-complete-green.json is byte-identical after an rc run",
        "C599 MasterStateIsolation last-monitor.json is byte-identical after an rc run",
        "C599 MasterStateIsolation interim-qualification-receipt.json is byte-identical after an rc run",
        "C599 MasterStateIsolation rc into the master state root refuses");

    [Test]
    public Task C599_ParameterHops() => RunCaseAsync("C599_ParameterHops", 29,
        "C599 ParameterHops unknown profile refuses at the core",
        "C599 ParameterHops rc with a master ref refuses",
        "C599 ParameterHops rc without ExpectedSha refuses",
        "C599 ParameterHops trigger rc without the rc profile refuses",
        "C599 ParameterHops candidate id is derived from the ref",
        "C599 ParameterHops run starts without child summaries",
        "C599 ParameterHops outer run reaches self-reexec without refusal",
        "C599 ParameterHops exactly one fresh child summary names this run",
        "C599 ParameterHops child consumed the pinned RC identity",
        "C599 ParameterHops child consumed the bound Full selection",
        "C599 ParameterHops child selection matches the immutable bound artifacts",
        "C599 ParameterHops the self-reexec hop forwards -Profile",
        "C599 ParameterHops the tests hop forwards -Profile");

    [Test]
    public Task C599_CloneOwnership()
    {
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("CARD-1340: Windows canonical roots C:\\src\\Antiphon and C:\\Antiphon\\worktrees");
        return RunCaseAsync("C599_CloneOwnership", 5,
            @"C599 CloneOwnership rc refuses C:\src\Antiphon",
            "C599 CloneOwnership coordinator refuses an unmarked clone",
            "C599 CloneOwnership coordinator refuses the shared tree",
            "C599 CloneOwnership control owned clone is accepted");
    }

    /// <summary>
    /// D-4 in-process (W2-nightly S18): the shared native-run lock (Antiphon.Nightly/ReleaseGateNativeLock.cs) on a
    /// private coordination root. The contended run that records deferred-busy is C599_SharedLockDeferredRun: the run
    /// core is still PowerShell.
    /// </summary>
    [Test]
    public void C599_SharedLock()
    {
        using var fixture = NightlyFixture.Create("c599-shared-lock");
        var processes = new FakeProcessTable().Start(4242, new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc));
        var native = new ReleaseGateNativeLock(fixture.Host(processes: processes));
        var coord = fixture.PathOf("coord");
        var lockPath = ReleaseGateNativeLock.LockPath(coord);

        var first = native.Enter(coord, "run-master", "master");
        (first.Ok && first.OwnsLock).ShouldBeTrue("C599 SharedLock first lane acquires " + first.Reason);
        File.Exists(lockPath).ShouldBeTrue("C599 SharedLock the lock file exists on disk");

        // A live owner is never stolen, whatever the other lane is.
        var second = native.Enter(coord, "run-rc", "rc");
        (!second.Ok && second.Reason == "deferred-busy").ShouldBeTrue("C599 SharedLock the second lane gets deferred-busy " + second.Reason);
        second.OwnsLock.ShouldBeFalse("C599 SharedLock the deferred lane owns nothing");

        var record = NightlyLockRecord.Read(lockPath)!;
        record.Pid.ShouldBe(processes.CurrentPid, "C599 SharedLock owner identity records the pid " + record.Pid);
        record.Lane.ShouldBe("master", "C599 SharedLock owner identity records the lane " + record.Lane);
        string.IsNullOrWhiteSpace(record.ProcessStartedAt).ShouldBeFalse("C599 SharedLock owner identity records the process start " + record.ProcessStartedAt);

        native.Exit(coord, "run-master");
        File.Exists(lockPath).ShouldBeFalse("C599 SharedLock release removes the lock file");
        var third = native.Enter(coord, "run-rc2", "rc");
        (third.Ok && third.OwnsLock).ShouldBeTrue("C599 SharedLock the other lane acquires once released " + third.Reason);
        native.Exit(coord, "run-rc2");

        // A dead holder is reported stale and left alone (no reclaim on the shared lock).
        File.WriteAllText(lockPath, """{"pid":424599,"processStartedAt":"","runId":"gone","lane":"master","continuationId":"","acquiredAt":""}""");
        processes.Exit(424599);
        var stale = native.Enter(coord, "run-rc3", "rc");
        (!stale.Ok && stale.Reason == "stale-shared-lock" && !stale.OwnsLock).ShouldBeTrue("a dead holder is stale-shared-lock " + stale.Reason);
        NightlyLockRecord.Read(lockPath)!.RunId.ShouldBe("gone", "the stale lock is not reclaimed");
    }

    [Test]
    public Task C599_SharedLockDeferredRun() => RunCaseAsync("C599_SharedLockDeferredRun", 3,
        "C599 SharedLock control: a foreign owner holds the lock",
        "C599 SharedLock a contended rc run returns deferred-busy",
        "C599 SharedLock the deferred attempt is recorded on disk");

    /// <summary>D-4 in-process (W2-nightly S18): a self-reexec hop inherits the shared lock only with the owner's exact identity.</summary>
    [Test]
    public void C599_Continuation()
    {
        using var fixture = NightlyFixture.Create("c599-continuation");
        var processes = new FakeProcessTable().Start(4242, new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc)).Exit(999999);
        var native = new ReleaseGateNativeLock(fixture.Host(processes: processes));
        var coord = fixture.PathOf("coord");
        var own = native.Enter(coord, "run-1", "master");
        own.Ok.ShouldBeTrue("C599 Continuation control: the parent owns the shared lock");
        var start = own.Record!.ProcessStartedAt;
        var pid = processes.CurrentPid;

        var hop = native.Enter(coord, "run-1", "master", "", pid, start);
        (hop.Ok && !hop.OwnsLock && hop.Reason == "hop").ShouldBeTrue("C599 Continuation a matching continuation inherits without owning " + hop.Reason);
        var wrongRun = native.Enter(coord, "run-2", "master", "", pid, start);
        (!wrongRun.Ok && wrongRun.Reason == "shared-hop-identity").ShouldBeTrue("C599 Continuation a mismatched run id refuses " + wrongRun.Reason);
        var wrongLane = native.Enter(coord, "run-1", "rc", "", pid, start);
        (!wrongLane.Ok && wrongLane.Reason == "shared-hop-identity").ShouldBeTrue("C599 Continuation a mismatched lane refuses " + wrongLane.Reason);
        var wrongStart = native.Enter(coord, "run-1", "master", "", pid, "2001-01-01T00:00:00.0000000Z");
        (!wrongStart.Ok && wrongStart.Reason == "shared-hop-identity").ShouldBeTrue("C599 Continuation a mismatched process start refuses " + wrongStart.Reason);
        var deadParent = native.Enter(coord, "run-1", "master", "", 999999, start);
        deadParent.Ok.ShouldBeFalse("C599 Continuation a dead continuation parent refuses " + deadParent.Reason);
        native.Exit(coord, "run-1");
        File.Exists(ReleaseGateNativeLock.LockPath(coord)).ShouldBeFalse("the owner's exit removes the lock");
        native.Enter(coord, "run-1", "master", "", pid, start).Reason.ShouldBe("missing-shared-lock", "a continuation without a lock refuses");
    }

    [Test]
    public Task C599_ReportLane() => RunCaseAsync("C599_ReportLane", 5,
        "C599 ReportLane the two lanes write different green files",
        "C599 ReportLane master green path is unchanged",
        "C599 ReportLane rc green lands under its candidate root",
        "C599 ReportLane the two lanes never share a credit kind");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
