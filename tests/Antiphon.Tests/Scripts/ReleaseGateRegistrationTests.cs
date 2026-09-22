using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-5. Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION registration script and a stateful
/// installed-API fixture in which write acceptance is separate from stored state, so only a GET
/// readback proves a definition reached the workspace.
///
/// <para>The decisive behaviours: a preview writes nothing and sends no token; an apply reads
/// every definition back and refuses on content drift; schedules are always created DISABLED and
/// only an explicit <c>-EnableSchedule</c> (which itself requires <c>-Apply</c>) turns one on; a
/// reapply is idempotent and preserves an already-enabled schedule; concurrent cron or timezone
/// drift refuses instead of overwriting; and the Windmill wrappers derive scheduled provenance
/// from the runner's own schedule context rather than a caller-supplied trigger string.</para>
///
/// <para>Nothing here touches a live Windmill workspace. Real registration is an operator-run S5
/// and S6 step.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateRegistrationTests
{
    [Test]
    public Task C599_Preview() => RunCaseAsync("C599_Preview", 12,
        "C599 Preview a plain run is a preview",
        "C599 Preview a preview performs zero writes",
        "C599 Preview the workspace is untouched after a preview",
        "C599 Preview all three definitions are previewed",
        "C599 Preview a preview sends no token",
        "C599 Preview -EnableSchedule without -Apply refuses");

    [Test]
    public Task C599_ApplyReadback() => RunCaseAsync("C599_ApplyReadback", 42,
        "C599 ApplyReadback script u/lndcobra/antiphon_release_candidates is stored",
        "C599 ApplyReadback schedule u/lndcobra/antiphon_release_candidates was created DISABLED",
        "C599 ApplyReadback the rc cron is the two-slot schedule",
        "C599 ApplyReadback the master cron is unchanged",
        "C599 ApplyReadback apply sends the token from the token file",
        "C599 ApplyReadback the token VALUE never reaches the trace",
        "C599 ApplyReadback an explicit selector enables exactly one schedule",
        "C599 ApplyReadback enabling readiness did not enable rc",
        "C599 ApplyReadback a second apply writes nothing (idempotent)",
        "C599 ApplyReadback a reapply PRESERVES an already enabled schedule",
        "C599 ApplyReadback a readback outage fails the apply",
        "C599 ApplyReadback the rc schedule is still disabled after a failed enable");

    [Test]
    public Task C599_Concurrency() => RunCaseAsync("C599_Concurrency", 3,
        "C599 Concurrency concurrent cron drift refuses",
        "C599 Concurrency the drifted schedule was not silently overwritten",
        "C599 Concurrency concurrent timezone drift refuses");

    [Test]
    public Task C599_ScheduleProvenance() => RunCaseAsync("C599_ScheduleProvenance", 14,
        "C599 ScheduleProvenance the master wrapper reads WM_SCHEDULE_PATH",
        "C599 ScheduleProvenance the master wrapper defaults to manual",
        "C599 ScheduleProvenance the master wrapper checks its OWN schedule path",
        "C599 ScheduleProvenance the rc wrapper never claims scheduled master credit",
        "C599 ScheduleProvenance the checked-in rc schedule payload is disabled",
        "C599 ScheduleProvenance a pre-enabled rc payload refuses",
        "C599 ScheduleProvenance a non-mc workspace refuses",
        "C599 ScheduleProvenance an inline token in the profile refuses",
        "C599 ScheduleProvenance a missing token file refuses the apply");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
