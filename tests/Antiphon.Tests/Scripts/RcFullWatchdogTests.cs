using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1514 S3. The hang detector records progress and the launch watchdog
// stays at the suite cap unless a measured stratum covers the admitted plan.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFullWatchdogTests
{
    [Test]
    [Timeout(60_000)]
    public async Task ProgressIgnoresHeartbeatWrites(CancellationToken cancellationToken)
    {
        var observed = await Run("progress", cancellationToken);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("heartbeatIgnored").GetBoolean().ShouldBeTrue("heartbeat write counted as test progress");
        observed.GetProperty("heartbeatParsed").GetBoolean().ShouldBeFalse("heartbeat file was read as a diagnostic");
        observed.GetProperty("lastCompletedUid").GetString().ShouldBe("uid-done");
        observed.GetProperty("diagStampStable").GetBoolean().ShouldBeTrue("diagnostic growth stamp moved without a diagnostic");
        observed.GetProperty("idleContinued").GetBoolean().ShouldBeTrue("idle clock reset on a heartbeat write");
        observed.GetProperty("inFlightUid").GetString().ShouldBe("uid-live");
        observed.GetProperty("lastPhase").GetString().ShouldBe("InProgress");
        observed.GetProperty("growthResetIdle").GetBoolean().ShouldBeTrue("real diagnostic growth did not reset idle");
        observed.GetProperty("noKillToken").GetBoolean().ShouldBeTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task QuietHealthyCaseIsNotKilled(CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var observed = await Run("quiet", cancellationToken);
        var elapsed = DateTime.UtcNow - started;
        elapsed.TotalSeconds.ShouldBeLessThan(30, "the 35 minute clock slept");
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("killed").GetBoolean().ShouldBeFalse("quiet healthy case was killed at 35 minutes");
        observed.GetProperty("shortCapKilled").GetBoolean().ShouldBeTrue("a 35 minute cap would not kill");
        observed.GetProperty("assignedWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("hangSuspected").GetBoolean().ShouldBeTrue("idle was not recorded");
        observed.GetProperty("elapsed").GetDouble().ShouldBe(2100);
        observed.GetProperty("noKillToken").GetBoolean().ShouldBeTrue();
    }

    [Test]
    [Timeout(90_000)]
    public async Task CapFitsAdmittedPlannedCost(CancellationToken cancellationToken)
    {
        var observed = await Run("cap", cancellationToken);
        var text = observed.GetProperty("text").GetString();
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, text);
        observed.GetProperty("g103FailedKept").GetInt32().ShouldBe(1, "failed wall dropped");
        observed.GetProperty("g103E95").GetDouble().ShouldBe(1);
        observed.GetProperty("g103Emax").GetDouble().ShouldBe(1936d / 900d, 0.0000001);
        observed.GetProperty("g103Watchdog").GetDouble().ShouldBe(2040);
        observed.GetProperty("g103Killed").GetBoolean().ShouldBeFalse("1936 second failed wall was killed");
        observed.GetProperty("deadlineFailedKept").GetInt32().ShouldBe(1, "deadline failure dropped");
        observed.GetProperty("deadlineWatchdog").GetDouble().ShouldBe(960);
        observed.GetProperty("deadlineKilled").GetBoolean().ShouldBeFalse("three deadline failures were killed");
        observed.GetProperty("replayWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("replayKilled").GetBoolean().ShouldBeFalse("unmeasured replay was killed");
        observed.GetProperty("paceWatchdog").GetDouble().ShouldBe(3600, "9.2 s/node pace was reduced");
        observed.GetProperty("paceKilled").GetBoolean().ShouldBeFalse("920 second pace wall was killed");
        observed.GetProperty("conservativeWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("observedWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("unknownWatchdog").GetDouble().ShouldBe(3600, "unknown cost used 600");
        observed.GetProperty("unknownKilledAt700").GetBoolean().ShouldBeFalse("unknown cost killed inside the suite watchdog");
        observed.GetProperty("stratumMismatchWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("n19Watchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("hashMismatchWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("censoredWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("clampAdmitted").GetBoolean().ShouldBeFalse("watchdog was clamped down to the suite maximum");
        observed.GetProperty("clampWatchdog").GetDouble().ShouldBe(2040);
        observed.GetProperty("pAtAdmitted").GetBoolean().ShouldBeTrue();
        observed.GetProperty("pAboveAdmitted").GetBoolean().ShouldBeFalse();
        observed.GetProperty("e2eWatchdog").GetDouble().ShouldBe(7200);
        observed.GetProperty("e2eAboveAdmitted").GetBoolean().ShouldBeFalse();
        observed.GetProperty("rounding2040").GetBoolean().ShouldBeTrue();
        observed.GetProperty("monotonic").GetBoolean().ShouldBeTrue();
        observed.GetProperty("invalidRefused").GetBoolean().ShouldBeTrue();
        observed.GetProperty("nanRefused").GetBoolean().ShouldBeTrue();
        observed.GetProperty("legacyWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("plannerWatchdog").GetDouble().ShouldBe(3600);
        observed.GetProperty("plannerAdmitted").GetBoolean().ShouldBeTrue();
        observed.GetProperty("policyUntouched").GetBoolean().ShouldBeTrue();
        observed.GetProperty("wired").GetBoolean().ShouldBeTrue("launch timeout is not the admission result");
        observed.GetProperty("suitePairingOk").GetBoolean().ShouldBeTrue(text);
        observed.GetProperty("sessionRunnerAtAdmitted").GetBoolean().ShouldBeTrue("session-runner at its 1800 s watchdog was refused");
        observed.GetProperty("sessionRunnerAboveAdmitted").GetBoolean().ShouldBeFalse("session-runner planned at 2400 s was admitted under an 1800 s watchdog");
        observed.GetProperty("ptyHostAboveAdmitted").GetBoolean().ShouldBeFalse("pty-host planned at 1800 s was admitted under a 1200 s watchdog");
        observed.GetProperty("agentsPtyAboveAdmitted").GetBoolean().ShouldBeFalse("agents-pty planned at 1800 s was admitted under a 1200 s watchdog");
        observed.GetProperty("markdownPdfAboveAdmitted").GetBoolean().ShouldBeFalse("markdown-pdf planned at 1800 s was admitted under a 1200 s watchdog");
        observed.GetProperty("messagingAboveAdmitted").GetBoolean().ShouldBeFalse("messaging planned at 1800 s was admitted under a 1200 s watchdog");
        observed.GetProperty("lowFactorWatchdog").GetDouble().ShouldBe(3600, "E95=0.2 was admitted under the planner ceiling");
        observed.GetProperty("lowFactorReason").GetString().ShouldBe("unmeasured-cost");
        observed.GetProperty("lowFactorAdmitted").GetBoolean().ShouldBeTrue();
        observed.GetProperty("tinyFactorWatchdog").GetDouble().ShouldBe(3600, "E95=0.01 produced a cap below the suite watchdog");
        observed.GetProperty("emaxOrderWatchdog").GetDouble().ShouldBe(3600, "Emax below E95 was treated as measured");
        observed.GetProperty("badWallCensored").GetBoolean().ShouldBeTrue(text);
        observed.GetProperty("badWallWatchdog").GetDouble().ShouldBe(3600, "an unparseable failed wall reduced the cap");
        observed.GetProperty("badWallKilled").GetBoolean().ShouldBeFalse("the g103 replay was killed after a bad wall was dropped");
        observed.GetProperty("zeroWallN").GetInt32().ShouldBe(19, "a zero wall counted as a complete sample");
        observed.GetProperty("zeroWallCensored").GetBoolean().ShouldBeTrue("a zero wall left the calibration valid");
        observed.GetProperty("cultureDoubleKept").GetBoolean().ShouldBeTrue("a de-DE double wall was dropped");
        observed.GetProperty("measuredAboveSuite").GetDouble().ShouldBe(4800, "a measured cap above the suite watchdog was clamped down");
        observed.GetProperty("measuredAboveAdmitted").GetBoolean().ShouldBeTrue();
        observed.GetProperty("boundAboveAdmitted").GetBoolean().ShouldBeFalse("W(C) above Wmax was admitted");
        observed.GetProperty("plannedAboveBoundAdmitted").GetBoolean().ShouldBeFalse("a plan above the calibrated bound was admitted");
        observed.GetProperty("clampedE95").GetDouble().ShouldBeGreaterThanOrEqualTo(1, "p95 below 1 was stored as the error factor");
        observed.GetProperty("ptyPlanRefused").GetBoolean().ShouldBeTrue(text);
        observed.GetProperty("ptyPlanWatchdog").GetDouble().ShouldBe(1200, "a pty-host plan between 1200 and 2400 s was stamped with another suite watchdog");
        observed.GetProperty("smallPlanWatchdog").GetDouble().ShouldBe(600, "a measured 60 s plan dropped the 600 s floor");
        observed.GetProperty("smallPlanReason").GetString().ShouldBe("measured");
        observed.GetProperty("emptyCalibrationWatchdog").GetDouble().ShouldBe(3600, "an empty calibration reduced the cap");
        observed.GetProperty("emptyCalibrationReason").GetString().ShouldBe("unmeasured-cost");
        observed.GetProperty("missingFieldWatchdog").GetDouble().ShouldBe(3600, "a calibration missing E95 reduced the cap");
        observed.GetProperty("missingFieldReason").GetString().ShouldBe("unmeasured-cost");
        observed.GetProperty("missingCensoredFieldAdmitted").GetBoolean().ShouldBeTrue("a calibration with no Censored field was treated as censored");
        observed.GetProperty("invalidPlanCensored").GetBoolean().ShouldBeTrue(text);
        observed.GetProperty("invalidPlanWatchdog").GetDouble().ShouldBe(3600, "an invalid planned cost produced a cap that kills the 1936 s wall");
        observed.GetProperty("invalidPlanKilled").GetBoolean().ShouldBeFalse("the g103 wall was killed after an invalid planned cost");
    }

    [Test]
    [Timeout(60_000)]
    public async Task TimeoutKeepsCoverageIncomplete(CancellationToken cancellationToken)
    {
        var observed = await Run("timeout", cancellationToken);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("coverageComplete").GetBoolean().ShouldBeFalse();
        observed.GetProperty("testsPassed").GetBoolean().ShouldBeFalse();
        observed.GetProperty("incomplete").GetBoolean().ShouldBeTrue();
        observed.GetProperty("notPass").GetBoolean().ShouldBeTrue();
        observed.GetProperty("reason").GetString().ShouldBe("watchdog-timeout");
        observed.GetProperty("partialRetained").GetBoolean().ShouldBeTrue("partial diagnostic was deleted");
        observed.GetProperty("killedAtCap").GetBoolean().ShouldBeTrue();
        observed.GetProperty("trxPresentCoverageComplete").GetBoolean().ShouldBeFalse("a present TRX marked the timeout complete");
        observed.GetProperty("launchTimeoutMs").GetInt32().ShouldBe(1_200_000, "the native launch did not receive the pty-host watchdog");
        observed.GetProperty("launchCoverageComplete").GetBoolean().ShouldBeFalse("the launched timeout marked coverage complete");
        observed.GetProperty("launchTestsPassed").GetBoolean().ShouldBeFalse("the launched timeout marked tests passed");
        observed.GetProperty("refusedLaunchStarted").GetBoolean().ShouldBeFalse("a refused admission started a test process");
        observed.GetProperty("refusedLaunchReasonOk").GetBoolean().ShouldBeTrue(text);
    }

    private static async Task<JsonElement> Run(string mode, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "rcspeed-s3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(C1514Support.RepoRoot(), "tests", "Antiphon.Tests", "Scripts", "Fixtures", "rc-full-watchdog-witness.ps1"));
        start.ArgumentList.Add(C1514Support.RepoRoot());
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(mode);
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        var resultPath = Path.Combine(root, "observation.json");
        if (!File.Exists(resultPath))
            throw new InvalidOperationException("observation missing\n" + ran.Text);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
        return doc.RootElement.Clone();
    }
}
