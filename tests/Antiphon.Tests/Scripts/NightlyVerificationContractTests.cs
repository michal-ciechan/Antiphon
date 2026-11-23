using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0544 / CARD-0545 verification contract. The notification/outage/recipient controls (PC-78..86, 95..98) moved into
/// the CARD-0545 watchdog and run in-process on <see cref="C545World"/> (see NightlyVerificationContractTests.C545.cs).
/// The watchdog deploy's cases run in-process on NightlyWatchdogDeploy (NightlyVerificationContractTests.C545Deploy.cs;
/// unit 10 of 2026-11-22-remaining-ps1-to-csharp.md deleted scripts/test-deploy-nightly-watchdog.ps1, the last PowerShell
/// harness this class ran). The readiness evaluator's cases (C544_*, C545_JobResultFetch, C545_WatchdogFresh,
/// C545_WatchdogStale, C545_ReadinessRouting) moved in-process into Nightly.NightlyHealthTests with
/// scripts/test-nightly-health.ps1 (W2-nightly S05), and C545_ResultLine into Nightly.NightlyRunTests (S19, on NightlyRun.Run).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class NightlyVerificationContractTests
{
}
