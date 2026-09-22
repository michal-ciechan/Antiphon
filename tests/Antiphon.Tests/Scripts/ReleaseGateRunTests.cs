using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-2, V-3 and R-4. Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION credit predicate, the production
/// run core and the production shared lock, using private clone/state/release/coordination roots.
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
    public Task C599_MasterStateIsolation() => RunCaseAsync("C599_MasterStateIsolation", 6,
        "C599 MasterStateIsolation last-complete-green.json is byte-identical after an rc run",
        "C599 MasterStateIsolation last-monitor.json is byte-identical after an rc run",
        "C599 MasterStateIsolation interim-qualification-receipt.json is byte-identical after an rc run",
        "C599 MasterStateIsolation rc into the master state root refuses");

    [Test]
    public Task C599_ParameterHops() => RunCaseAsync("C599_ParameterHops", 23,
        "C599 ParameterHops unknown profile refuses at the core",
        "C599 ParameterHops rc with a master ref refuses",
        "C599 ParameterHops rc without ExpectedSha refuses",
        "C599 ParameterHops trigger rc without the rc profile refuses",
        "C599 ParameterHops candidate id is derived from the ref",
        "C599 ParameterHops the self-reexec hop forwards -Profile",
        "C599 ParameterHops the tests hop forwards -Profile");

    [Test]
    public Task C599_CloneOwnership() => RunCaseAsync("C599_CloneOwnership", 5,
        @"C599 CloneOwnership rc refuses C:\src\Antiphon",
        "C599 CloneOwnership coordinator refuses an unmarked clone",
        "C599 CloneOwnership coordinator refuses the shared tree",
        "C599 CloneOwnership control owned clone is accepted");

    [Test]
    public Task C599_SharedLock() => RunCaseAsync("C599_SharedLock", 12,
        "C599 SharedLock first lane acquires",
        "C599 SharedLock the second lane gets deferred-busy",
        "C599 SharedLock owner identity records the lane",
        "C599 SharedLock the other lane acquires once released",
        "C599 SharedLock a contended rc run returns deferred-busy",
        "C599 SharedLock the deferred attempt is recorded on disk");

    [Test]
    public Task C599_Continuation() => RunCaseAsync("C599_Continuation", 6,
        "C599 Continuation a matching continuation inherits without owning",
        "C599 Continuation a mismatched run id refuses",
        "C599 Continuation a mismatched lane refuses",
        "C599 Continuation a mismatched process start refuses",
        "C599 Continuation a dead continuation parent refuses");

    [Test]
    public Task C599_ReportLane() => RunCaseAsync("C599_ReportLane", 5,
        "C599 ReportLane the two lanes write different green files",
        "C599 ReportLane master green path is unchanged",
        "C599 ReportLane rc green lands under its candidate root",
        "C599 ReportLane the two lanes never share a credit kind");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
