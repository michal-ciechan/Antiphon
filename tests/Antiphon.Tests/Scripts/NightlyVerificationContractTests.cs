using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0544 / CARD-0545 verification contract. The notification/outage/recipient controls (PC-78..86, 95..98) moved into
/// the CARD-0545 watchdog and run in-process on <see cref="C545World"/> (see NightlyVerificationContractTests.C545.cs),
/// beside the wrappers for the PowerShell harness cases that are still PowerShell (test-nightly-run.ps1,
/// test-deploy-nightly-watchdog.ps1). The readiness evaluator's cases (C544_*, C545_JobResultFetch, C545_WatchdogFresh,
/// C545_WatchdogStale, C545_ReadinessRouting) moved in-process into Nightly.NightlyHealthTests with
/// scripts/test-nightly-health.ps1 (W2-nightly S05).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class NightlyVerificationContractTests
{
    /// <summary>Forwards to <see cref="ScriptHarness.RunHarnessCaseAsync"/> (CARD-0585 S6 lifted the body out).</summary>
    private static Task RunHarnessCaseAsync(string harness, string prefix, string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync(harness, prefix, caseName, expectedRows, requiredRows);
}
