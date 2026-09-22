using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-4 (harness half). Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION prerequisite probe, the production
/// native-classification rule, the production child-environment builder and the production MTP
/// argument builder.
///
/// <para>The decisive behaviours: a missing Playwright browser or a missing client bundle is red
/// or incomplete and is named, never a skip; <c>e2e</c> classifies as native so it serialises with
/// every other native suite; headed, distiller and live-Telegram variables are cleared for an E2E
/// child; and the executor launches only the eligible class roster with fresh TRX plus discovery
/// and execution diagnostics.</para>
///
/// <para>The real isolated fixtures - owned app, database, runner and browser - are
/// <c>ReleaseGateIsolationTests</c> in <c>Antiphon.E2E</c>; this class covers the harness side.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateE2EEvidenceTests
{
    [Test]
    public Task C599_Prerequisites() => RunCaseAsync("C599_Prerequisites", 10,
        "C599 Prerequisites a missing playwright script is not ok",
        "C599 Prerequisites the missing browser installer is named",
        "C599 Prerequisites the missing client bundle is named",
        "C599 Prerequisites the rc profile requires e2e before any prerequisite check",
        "C599 Prerequisites e2e is classified native so it serialises",
        "C599 Prerequisites a failed prerequisite is a named red reason");

    [Test]
    public Task C599_EnvironmentIsolation() => RunCaseAsync("C599_EnvironmentIsolation", 6,
        "C599 EnvironmentIsolation ANTIPHON_HEADED_TESTS is cleared for an e2e child",
        "C599 EnvironmentIsolation ANTIPHON_DISTILLER_APPLY_CANARY is cleared for an e2e child",
        "C599 EnvironmentIsolation ANTIPHON_TG_TEST_TOKEN is cleared for an e2e child",
        "C599 EnvironmentIsolation an unrelated variable survives",
        "C599 EnvironmentIsolation the broker opt-in IS set for messaging");

    [Test]
    public Task C599_ExecutionArtifacts() => RunCaseAsync("C599_ExecutionArtifacts", 10,
        "C599 ExecutionArtifacts the executor requests a fresh TRX",
        "C599 ExecutionArtifacts the executor requests MTP diagnostics",
        "C599 ExecutionArtifacts the executor launches only the eligible roster",
        "C599 ExecutionArtifacts the trx filename is passed as a basename",
        "C599 ExecutionArtifacts the executor waits for owned cleanup",
        "C599 ExecutionArtifacts an unjoined owned child is an error",
        "C599 ExecutionArtifacts the suite row records the expanded census");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
