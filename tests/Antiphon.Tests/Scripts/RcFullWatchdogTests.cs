using System.Collections;
using System.Globalization;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
// The Antiphon.NightlyWatchdog namespace (the independent watchdog) hides the core's NightlyWatchdog here.
using Watchdog = Antiphon.Nightly.NightlyWatchdog;

namespace Antiphon.Tests.Scripts;

// CARD-1514 S3. The hang detector records progress and the launch watchdog
// stays at the suite cap unless a measured stratum covers the admitted plan.
// W2-nightly S10: in-process. The checks of the deleted Fixtures/rc-full-watchdog-witness.ps1 (portable clock and
// matrices, no live test process and no sleep) call the compiled heartbeat (QualificationHeartbeat) and watchdog
// (NightlyWatchdog, NightlyTestsPlan) directly; its timeout mode's two launches run the chunk runner
// (NightlyChunkRunner) on a NightlyChunkWorld instead of a seamed Invoke-AntiphonNightlyTests.
[Category("Integration")]
public sealed class RcFullWatchdogTests
{
    private static readonly DateTime Origin = DateTime.Parse("2026-10-29T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    [Test]
    public void ProgressIgnoresHeartbeatWrites()
    {
        var work = NewWork();
        try
        {
            var diag = Path.Combine(work, "chunk.diag");
            var beatPath = Path.Combine(work, "supervisor-heartbeat.json");
            File.WriteAllText(diag, "TestNodeUid { Value = uid-done } PassedTestNodeStateProperty { Explanation = ok }\n");
            var read1 = QualificationHeartbeat.ReadDiagnosticProgress(diag, 0, beatPath);
            QualificationProcess[] proc = [new(10, 1, 1.0)];
            var first = Beat(Origin, proc, null, 0, 100, read1);
            QualificationHeartbeat.Write(first, beatPath);
            var parsedBeat = QualificationHeartbeat.ReadDiagnosticProgress(beatPath, 0, beatPath);
            var reread = QualificationHeartbeat.ReadDiagnosticProgress(diag, read1.Offset, beatPath);
            var later = Origin.AddMinutes(25);
            var second = Beat(later, proc, first, 0, 100, reread);
            File.AppendAllText(diag, "TestNodeUid { Value = uid-live } InProgressTestNodeStateProperty { Explanation = running }\n");
            var read2 = QualificationHeartbeat.ReadDiagnosticProgress(diag, read1.Offset, beatPath);
            var after = Beat(later.AddMinutes(1), proc, second, 0, 100, read2);

            parsedBeat.Grew.ShouldBeFalse("heartbeat file was read as a diagnostic (heartbeat-write-counted-as-progress)");
            second.LastCompletedUid.ShouldBe("uid-done", "completed-uid-lost");
            (second.IdleMinutes >= 25 && second.HangSuspected).ShouldBeTrue("idle clock reset on a heartbeat write (heartbeat-reset-idle)");
            first.LastDiagGrowthAtUtc.ShouldNotBeNullOrWhiteSpace();
            second.LastDiagGrowthAtUtc.ShouldBe(first.LastDiagGrowthAtUtc, "diagnostic growth stamp moved without a diagnostic (diag-stamp-moved)");
            after.InFlightUid.ShouldBe("uid-live", "inflight-missing");
            after.LastPhase.ShouldBe("InProgress", "phase-missing");
            after.IdleMinutes.ShouldBeLessThan(0.001, "real diagnostic growth did not reset idle");
            after.LastDiagGrowthAtUtc.ShouldNotBe(first.LastDiagGrowthAtUtc, "diagnostic-growth-stamp-missing");
            NoKillToken();
        }
        finally { DeleteWork(work); }
    }

    [Test]
    public void QuietHealthyCaseIsNotKilled()
    {
        QualificationProcess[] proc = [new(10, 1, 3.0)];
        var first = Beat(Origin, proc, null, 1, 50, null);
        var second = Beat(Origin.AddMinutes(35), proc, first, 1, 50, null);
        var assigned = Watchdog.ResolveLaunch("antiphon", 900, "default", "", null, null, null);
        var watchdogSeconds = Num(assigned, "WatchdogSeconds");
        watchdogSeconds.ShouldBe(3600, "assigned-watchdog-is-not-the-suite-cap");
        Watchdog.Killed(2100, watchdogSeconds, false).ShouldBeFalse("quiet healthy case was killed at 35 minutes");
        Watchdog.Killed(2100, 2100, false).ShouldBeTrue("a 35 minute cap would not kill");
        second.HangSuspected.ShouldBeTrue("idle was not recorded");
        (watchdogSeconds >= 1860 && watchdogSeconds <= 2100).ShouldBeFalse("thirty-one-minute-cap");
        NoKillToken();
    }

    [Test]
    public void CapFitsAdmittedPlannedCost()
    {
        var policy = NightlyPlanCases.Policy();
        PsValue.PropertyNames(policy.Object).Any(n => string.Equals(n, "watchdogCalibration", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse("policy-watchdog-changed");
        policy.Object.Watchdogs["antiphon"].ShouldBe(3600000, "policy-watchdog-changed");
        policy.Object.Watchdogs["e2e"].ShouldBe(7200000, "policy-watchdog-changed");
        // W2-nightly S12: the chunk runner, its admission and its timeout disposition are NightlyChunkRunner.cs.
        var impl = File.ReadAllText(Path.Combine(C1514Support.RepoRoot(), "scripts", "lib", "Antiphon.Nightly", "NightlyChunkRunner.cs"));
        var refuseAt = impl.IndexOf("watchdog-admission-refused", StringComparison.Ordinal);
        var timeoutAt = impl.IndexOf("PsValue.Prop(admission, \"WatchdogMilliseconds\")", StringComparison.Ordinal);
        (refuseAt >= 0 && timeoutAt > refuseAt && impl.Contains("NightlyWatchdog.TimeoutDisposition(", StringComparison.Ordinal)).ShouldBeTrue("launch timeout is not the admission result");

        var stratum = Stratum();
        var g103Factors = Watchdog.ErrorFactors(Walls(19, 900, "Passed").Append(Sample(1936.0, "Failed", failureCount: 1)).ToArray(), 900);
        Num(g103Factors, "FailedKept").ShouldBe(1, "failed wall dropped");
        Num(g103Factors, "E95").ShouldBe(1);
        Num(g103Factors, "Emax").ShouldBe(1936d / 900d, 0.0000001);
        var g103 = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum,
            Calibration(Num(g103Factors, "N"), stratum, Num(g103Factors, "E95"), Num(g103Factors, "Emax"), 60, 900, 2040, PsValue.IsTrue(PsValue.Prop(g103Factors, "Censored"))), null);
        Num(g103, "WatchdogSeconds").ShouldBe(2040);
        Watchdog.Killed(1936, Num(g103, "WatchdogSeconds"), true).ShouldBeFalse("1936 second failed wall was killed");
        Admitted(g103).ShouldBeTrue("g103-not-admitted");

        var deadlineFactors = Watchdog.ErrorFactors(Walls(19, 120, "Passed").Append(Sample(900.0, "Failed", failureCount: 3)).ToArray(), 120);
        Num(deadlineFactors, "FailedKept").ShouldBe(1, "deadline failure dropped");
        var deadline = Watchdog.ResolveLaunch("antiphon", 120, "measured", "hash-a", stratum,
            Calibration(Num(deadlineFactors, "N"), stratum, Num(deadlineFactors, "E95"), Num(deadlineFactors, "Emax"), 60, 120, 960, false), null);
        Num(deadline, "WatchdogSeconds").ShouldBe(960);
        Watchdog.Killed(900, Num(deadline, "WatchdogSeconds"), true).ShouldBeFalse("three deadline failures were killed");

        var replay = Watchdog.ResolveLaunch("antiphon", 900, "default", "hash-a", stratum, Calibration(20, stratum, 1.0, 1936.0 / 900.0, 60, 900, 2040, false), null);
        Num(replay, "WatchdogSeconds").ShouldBe(3600);
        Reason(replay).ShouldBe("unmeasured-cost", "unmeasured-replay-not-3600");
        Watchdog.Killed(1936, Num(replay, "WatchdogSeconds"), true).ShouldBeFalse("unmeasured replay was killed");

        foreach (var basis in new[] { "pace", "default", "conservative-bound", "observed-bound" })
        {
            var row = Watchdog.ResolveLaunch("antiphon", 50, basis, "hash-a", stratum, Calibration(20, stratum, 1.0, 1.0, 60, 2400, 4800, false), null);
            Num(row, "WatchdogSeconds").ShouldBe(3600, "basis-reduced " + basis + " (9.2 s/node pace was reduced)");
            Watchdog.Killed(920, Num(row, "WatchdogSeconds"), true).ShouldBeFalse("basis-reduced " + basis + " (920 second pace wall was killed)");
            Reason(row).ShouldBe("unmeasured-cost", "basis-reduced " + basis);
        }
        var unknown = Watchdog.ResolveLaunch("antiphon", 50, "default", "", null, null, null);
        Num(unknown, "WatchdogSeconds").ShouldBe(3600, "unknown cost used 600");
        Watchdog.Killed(700, Num(unknown, "WatchdogSeconds"), false).ShouldBeFalse("unknown cost killed inside the suite watchdog");

        foreach (var key in new[] { "suite", "uidDigest", "costDigest", "executionDigest", "assemblyDigest", "os", "runtime", "host", "loadBand", "fixtureMode", "concurrency" })
        {
            var flipped = Stratum();
            flipped[key] = "other";
            var row = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", flipped,
                Calibration(20, stratum, Num(g103Factors, "E95"), Num(g103Factors, "Emax"), 60, 900, 2040, false), null);
            Num(row, "WatchdogSeconds").ShouldBe(3600, "stratum-mismatch-applied " + key);
        }

        var n19Factors = Watchdog.ErrorFactors(Walls(19, 900, "Passed").ToArray(), 900);
        Num(Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum, Calibration(Num(n19Factors, "N"), stratum, 1.0, 1.0, 60, 900, 2040, false), null), "WatchdogSeconds")
            .ShouldBe(3600, "n19-reduced");
        Num(Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-b", stratum, Calibration(20, stratum, Num(g103Factors, "E95"), Num(g103Factors, "Emax"), 60, 900, 2040, false), null),
            "WatchdogSeconds").ShouldBe(3600, "hash-mismatch-applied");

        var censoredFactors = Watchdog.ErrorFactors(Walls(20, 900, "Passed").Append(Sample(1936.0, "Failed", censored: true)).ToArray(), 900);
        PsValue.IsTrue(PsValue.Prop(censoredFactors, "Censored")).ShouldBeTrue("censored-wall-reduced");
        Num(Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum, Calibration(20, stratum, 1.0, 1.0, 60, 900, 2040, true), null), "WatchdogSeconds")
            .ShouldBe(3600, "censored-wall-reduced");

        var clamp = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum, Calibration(20, stratum, Num(g103Factors, "E95"), Num(g103Factors, "Emax"), 60, 900, 1800, false), null);
        Admitted(clamp).ShouldBeFalse("watchdog was clamped down to the suite maximum");
        Num(clamp, "WatchdogSeconds").ShouldBe(2040);
        Reason(clamp).ShouldBe("policy-max-mismatch");

        var at = Watchdog.ResolveLaunch("antiphon", 2400, "default", "", null, null, null);
        var above = Watchdog.ResolveLaunch("antiphon", 2401, "default", "", null, null, null);
        var e2eAt = Watchdog.ResolveLaunch("e2e", 1800, "default", "", null, null, null);
        var e2eAbove = Watchdog.ResolveLaunch("e2e", 1801, "default", "", null, null, null);
        var legacy = Watchdog.ResolveLaunch("antiphon", null, "default", "", null, null, null);
        var nan = Watchdog.ResolveLaunch("antiphon", double.NaN, "default", "", null, null, null);
        (Admitted(at) && Num(at, "WatchdogSeconds") == 3600).ShouldBeTrue("ceiling-at-refused");
        Admitted(above).ShouldBeFalse("ceiling-above-admitted");
        (Admitted(e2eAt) && Num(e2eAt, "WatchdogSeconds") == 7200).ShouldBeTrue("e2e-ceiling");
        Admitted(e2eAbove).ShouldBeFalse("e2e-above-admitted");
        (Admitted(legacy) && Num(legacy, "WatchdogSeconds") == 3600).ShouldBeTrue("legacy-estimate-refused");
        Admitted(nan).ShouldBeFalse("nan-admitted");

        var pairing = new List<string>();
        foreach (var suiteName in policy.Object.Watchdogs.Keys.OrderBy(k => k, NightlyPlanCases.SortObject))
        {
            var capSeconds = Watchdog.SuiteWatchdogSeconds(policy.Object, suiteName);
            var atPlan = Math.Min(capSeconds, Watchdog.LaunchEstimateCeilingSeconds(suiteName));
            var rowAt = Watchdog.ResolveLaunch(suiteName, atPlan, "default", "", null, null, policy.Object);
            if (!Admitted(rowAt) || Num(rowAt, "WatchdogSeconds") != capSeconds) pairing.Add("suite-watchdog-pairing " + suiteName);
            if (Admitted(Watchdog.ResolveLaunch(suiteName, capSeconds + 1, "default", "", null, null, policy.Object))) pairing.Add("suite-above-watchdog-admitted " + suiteName);
        }
        pairing.ShouldBeEmpty();
        var sessionAt = Watchdog.ResolveLaunch("session-runner", 1800, "default", "", null, null, policy.Object);
        (Admitted(sessionAt) && Num(sessionAt, "WatchdogSeconds") == 1800).ShouldBeTrue("session-runner at its 1800 s watchdog was refused");
        Admitted(Watchdog.ResolveLaunch("session-runner", 2400, "default", "", null, null, policy.Object)).ShouldBeFalse("session-runner planned at 2400 s was admitted under an 1800 s watchdog");
        foreach (var suiteName in new[] { "pty-host", "agents-pty", "markdown-pdf", "messaging" })
            Admitted(Watchdog.ResolveLaunch(suiteName, 1800, "default", "", null, null, policy.Object)).ShouldBeFalse(suiteName + " planned at 1800 s was admitted under a 1200 s watchdog");

        var lowFactor = Watchdog.ResolveLaunch("antiphon", 2400, "measured", "hash-a", stratum, Calibration(20, stratum, 0.2, 0.2, 0, 2400, 4800, false), policy.Object);
        var tinyFactor = Watchdog.ResolveLaunch("antiphon", 2400, "measured", "hash-a", stratum, Calibration(20, stratum, 0.01, 0.01, 0, 2400, 4800, false), policy.Object);
        var emaxOrder = Watchdog.ResolveLaunch("antiphon", 2400, "measured", "hash-a", stratum, Calibration(20, stratum, 2.0, 1.0, 0, 2400, 4800, false), policy.Object);
        Num(lowFactor, "WatchdogSeconds").ShouldBe(3600, "E95=0.2 was admitted under the planner ceiling");
        Reason(lowFactor).ShouldBe("unmeasured-cost");
        Admitted(lowFactor).ShouldBeTrue();
        Num(tinyFactor, "WatchdogSeconds").ShouldBe(3600, "E95=0.01 produced a cap below the suite watchdog");
        Reason(tinyFactor).ShouldBe("unmeasured-cost");
        Num(emaxOrder, "WatchdogSeconds").ShouldBe(3600, "Emax below E95 was treated as measured");
        Reason(emaxOrder).ShouldBe("unmeasured-cost");

        foreach (var badWall in new object?[] { "1936,5", null, "n/a", "0", "-4" })
        {
            var factors = Watchdog.ErrorFactors(Walls(20, 900, "Passed").Append(Sample(badWall, "Failed")).ToArray(), 900);
            var row = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum,
                Calibration(20, stratum, Num(factors, "E95"), Num(factors, "Emax"), 60, 900, 2040, PsValue.IsTrue(PsValue.Prop(factors, "Censored"))), policy.Object);
            var label = "uncensored-wall " + (badWall ?? "null");
            PsValue.IsTrue(PsValue.Prop(factors, "Censored")).ShouldBeTrue(label);
            Num(factors, "N").ShouldBe(20, label);
            Num(row, "WatchdogSeconds").ShouldBe(3600, label + ": an unparseable failed wall reduced the cap");
            Reason(row).ShouldBe("unmeasured-cost", label);
            Watchdog.Killed(1936, Num(row, "WatchdogSeconds"), true).ShouldBeFalse(label + ": the g103 replay was killed after a bad wall was dropped");
        }
        var zeroFactors = Watchdog.ErrorFactors(Walls(19, 900, "Passed").Append(Sample(0.0, "Passed")).ToArray(), 900);
        Num(zeroFactors, "N").ShouldBe(19, "a zero wall counted as a complete sample");
        PsValue.IsTrue(PsValue.Prop(zeroFactors, "Censored")).ShouldBeTrue("a zero wall left the calibration valid");
        var savedCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var cultureFactors = Watchdog.ErrorFactors(Walls(19, 900, "Passed").Append(Sample(1936.5, "Failed")).ToArray(), 900);
            (!PsValue.IsTrue(PsValue.Prop(cultureFactors, "Censored")) && Num(cultureFactors, "FailedKept") == 1 && Num(cultureFactors, "N") == 20)
                .ShouldBeTrue("a de-DE double wall was dropped");
        }
        finally { Thread.CurrentThread.CurrentCulture = savedCulture; }

        var aboveSuite = Watchdog.ResolveLaunch("antiphon", 2400, "measured", "hash-a", stratum, Calibration(20, stratum, 1.0, 1.0, 0, 2400, 4800, false), policy.Object);
        Num(aboveSuite, "WatchdogSeconds").ShouldBe(4800, "a measured cap above the suite watchdog was clamped down");
        Admitted(aboveSuite).ShouldBeTrue();
        var boundAbove = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum, Calibration(20, stratum, 1.0, 1.0, 60, 2400, 3600, false), policy.Object);
        Admitted(boundAbove).ShouldBeFalse("W(C) above Wmax was admitted");
        Reason(boundAbove).ShouldBe("policy-max-mismatch");
        var overBound = Watchdog.ResolveLaunch("antiphon", 1200, "measured", "hash-a", stratum, Calibration(20, stratum, 1.0, 1.0, 60, 900, 4800, false), policy.Object);
        Admitted(overBound).ShouldBeFalse("a plan above the calibrated bound was admitted");
        Reason(overBound).ShouldBe("launch-estimate-ceiling");
        Num(Watchdog.ErrorFactors(Walls(20, 180, "Passed").ToArray(), 900), "E95").ShouldBeGreaterThanOrEqualTo(1, "p95 below 1 was stored as the error factor");

        var rounded = Watchdog.WatchdogSeconds(1, 1936.0 / 900.0, 900, 60);
        rounded.ShouldBe(2040);
        Watchdog.WatchdogSeconds(1, 1936.0 / 900.0, 100, 60).ShouldBeLessThanOrEqualTo(rounded, "not-monotone");

        var boundNode = new PlainObject(("ClassName", "Antiphon.Tests.Application.BoundTests"), ("Namespace", "Antiphon.Tests.Application"), ("Type", "BoundTests"),
            ("Method", "Run"), ("Uid", "bound"), ("Categories", new object[] { "Integration" }), ("Excluded", false));
        var planned = NightlyTestsPlan.ProfileLaunchPlan([boundNode], NightlyPlanCases.AllChunk(), Array.Empty<object>(), "antiphon", null,
            NightlyClassCosts.Of(new Hashtable { ["Antiphon.Tests.Application.BoundTests"] = 2400.0 }));
        planned.Length.ShouldBe(1, "planner-watchdog-unpaired");
        PsValue.PropertyNames(planned[0]).ShouldContain("watchdogSeconds", "planner-watchdog-unpaired");
        Num(planned[0], "watchdogSeconds").ShouldBe(3600, "planner-watchdog-short");
        PsValue.IsTrue(PsValue.Prop(planned[0], "watchdogAdmitted")).ShouldBeTrue("planner-watchdog-short");

        var ptyNode = new PlainObject(("ClassName", "Antiphon.PtyHost.Tests.PricedTests"), ("Namespace", "Antiphon.PtyHost.Tests"), ("Type", "PricedTests"),
            ("Method", "Run"), ("Uid", "priced"), ("Categories", new object[] { "Integration" }), ("Excluded", false));
        var ptyCosts = NightlyClassCosts.Of(new Hashtable { ["Antiphon.PtyHost.Tests.PricedTests"] = 1500.0 });
        var probe = NightlyTestsPlan.ProfileLaunchPlan([ptyNode], NightlyPlanCases.AllChunk(), Array.Empty<object>(), "antiphon", null, ptyCosts);
        var refusal = Should.Throw<Exception>(() => NightlyTestsPlan.ProfileLaunchPlan([ptyNode], NightlyPlanCases.AllChunk(), Array.Empty<object>(), "pty-host", policy.Object, ptyCosts),
            probe.Length >= 1 && PsValue.IsTrue(PsValue.Prop(probe[0], "watchdogAdmitted")) ? "pty-host-plan-admitted-as-antiphon W=" + Num(probe[0], "watchdogSeconds") : "pty-host-plan-not-refused");
        refusal.Message.ShouldContain("watchdog-admission-refused");
        refusal.Message.ShouldContain("reason=suite-watchdog-ceiling");
        refusal.Message.ShouldMatch(@"watchdogSeconds=1200(\D|$)", "a pty-host plan between 1200 and 2400 s was stamped with another suite watchdog");

        var smallPlan = Watchdog.ResolveLaunch("antiphon", 60, "measured", "hash-a", stratum, Calibration(20, stratum, 1.0, 1.0, 0, 2400, 4800, false), policy.Object);
        (Admitted(smallPlan) && Num(smallPlan, "WatchdogSeconds") == 600).ShouldBeTrue("a measured 60 s plan dropped the 600 s floor");
        Reason(smallPlan).ShouldBe("measured");
        var emptyCalibration = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum, new PlainObject(), policy.Object);
        (Admitted(emptyCalibration) && Num(emptyCalibration, "WatchdogSeconds") == 3600).ShouldBeTrue("an empty calibration reduced the cap");
        Reason(emptyCalibration).ShouldBe("unmeasured-cost");
        var missingE95 = Calibration(20, stratum, 1.0, 1.0, 0, 2400, 4800, false);
        missingE95.Remove("E95");
        var missingField = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum, missingE95, policy.Object);
        (Admitted(missingField) && Num(missingField, "WatchdogSeconds") == 3600).ShouldBeTrue("a calibration missing E95 reduced the cap");
        Reason(missingField).ShouldBe("unmeasured-cost");
        var noCensored = Calibration(20, stratum, 1.0, 1.0, 0, 2400, 4800, false);
        noCensored.Remove("Censored");
        var noCensoredField = Watchdog.ResolveLaunch("antiphon", 2400, "measured", "hash-a", stratum, noCensored, policy.Object);
        (Admitted(noCensoredField) && Reason(noCensoredField) == "measured" && Num(noCensoredField, "WatchdogSeconds") == 4800)
            .ShouldBeTrue("a calibration with no Censored field was treated as censored");

        foreach (var badPlan in new object?[] { null, 0, double.NaN, "1,5" })
        {
            var factors = Watchdog.ErrorFactors(Walls(20, 900, "Passed").Append(Sample(1936.0, "Failed")).ToArray(), badPlan);
            var row = Watchdog.ResolveLaunch("antiphon", 900, "measured", "hash-a", stratum,
                Calibration(Num(factors, "N"), stratum, Num(factors, "E95"), Num(factors, "Emax"), 60, 900, 2040, PsValue.IsTrue(PsValue.Prop(factors, "Censored"))), policy.Object);
            var label = "invalid-plan-uncensored " + (badPlan == null ? "null" : PsValue.Str(badPlan));
            PsValue.IsTrue(PsValue.Prop(factors, "Censored")).ShouldBeTrue(label);
            Num(row, "WatchdogSeconds").ShouldBe(3600, label + ": an invalid planned cost produced a cap that kills the 1936 s wall");
            Reason(row).ShouldBe("unmeasured-cost", label);
            Watchdog.Killed(1936, Num(row, "WatchdogSeconds"), true).ShouldBeFalse(label + ": the g103 wall was killed after an invalid planned cost");
        }
    }

    [Test]
    public void TimeoutKeepsCoverageIncomplete()
    {
        var work = NewWork();
        try
        {
            var partial = Path.Combine(work, "partial.diag");
            File.WriteAllText(partial, "TestNodeUid { Value = uid-partial } InProgressTestNodeStateProperty { Explanation = running }\n");
            var disposition = Watchdog.TimeoutDisposition(false, false, false);
            PsValue.IsTrue(PsValue.Prop(disposition, "CoverageComplete")).ShouldBeFalse("timeout-marked-complete");
            PsValue.IsTrue(PsValue.Prop(disposition, "TestsPassed")).ShouldBeFalse("timeout-marked-complete");
            PsValue.IsTrue(PsValue.Prop(disposition, "Incomplete")).ShouldBeTrue("timeout-marked-complete");
            PsValue.IsTrue(PsValue.Prop(disposition, "Pass")).ShouldBeFalse("timeout-marked-complete");
            Reason(disposition).ShouldBe("watchdog-timeout");
            File.Exists(partial).ShouldBeTrue("partial diagnostic was deleted");
            Watchdog.Killed(3600, 3600, false).ShouldBeTrue("cap-did-not-fire");
            var withTrx = Watchdog.TimeoutDisposition(true, true, false);
            PsValue.IsTrue(PsValue.Prop(withTrx, "CoverageComplete")).ShouldBeFalse("a present TRX marked the timeout complete");
            PsValue.IsTrue(PsValue.Prop(withTrx, "TestsPassed")).ShouldBeFalse("trx-present-marked-complete");
            PsValue.IsTrue(PsValue.Prop(withTrx, "Pass")).ShouldBeFalse("trx-present-marked-complete");
            PsValue.IsTrue(PsValue.Prop(withTrx, "Incomplete")).ShouldBeTrue("trx-present-marked-complete");
        }
        finally { DeleteWork(work); }

        // The pty-host launch: the native host writes its TRX and times out; the launch carries the pty-host watchdog.
        using (var world = new NightlyChunkWorld(NightlyChunkWorld.NativeThen(_ => new NightlyLaunchResult { ExitCode = 1, TimedOut = true, Pid = 7, ChildrenExited = true, CleanupComplete = true }), "s10-timeout"))
        {
            var launched = NightlyChunkRunner.Run(world.Context(), world.Suite("pty-host"), NightlyChunkWorld.Chunk());
            var trxLaunch = world.Launcher.Launches.Where(l => l.Arguments.Contains("--report-trx")).ToArray();
            trxLaunch.Length.ShouldBe(1, "launch-timeout-missing");
            trxLaunch[0].TimeoutMilliseconds.ShouldBe(1_200_000, "the native launch did not receive the pty-host watchdog");
            launched.CoverageComplete.ShouldBeFalse("the launched timeout marked coverage complete");
            launched.TestsPassed.ShouldBeFalse("the launched timeout marked tests passed");
        }

        // A priced pty-host chunk over its watchdog: refused before any test process starts.
        using (var refused = new NightlyChunkWorld(prefix: "s10-refused"))
        {
            var priced = new PlainObject(("id", "priced"), ("classes", new object[] { "Antiphon.PtyHost.Tests.PricedTests" }), ("seconds", 1500));
            var outcome = NightlyChunkRunner.Run(refused.Context(), refused.Suite("pty-host"), priced);
            refused.Launcher.Launches.Any(l => l.Arguments.Contains("--report-trx")).ShouldBeFalse("a refused admission started a test process");
            (outcome.Error?.Message ?? "").ShouldContain("watchdog-admission-refused");
        }
    }

    private static QualificationHeartbeatRecord Beat(DateTime now, QualificationProcess[] processes, QualificationHeartbeatRecord? previous, double trx, long bytes,
        QualificationDiagnosticProgress? progress) =>
        QualificationHeartbeat.Update(new QualificationHeartbeatInput
        {
            NowUtc = now, Processes = processes, ChunkRootPid = 10, Previous = previous, Trx = trx, Bytes = bytes, Newest = "chunk.diag", PlannedSeconds = null,
            DiagnosticProgress = progress,
        });

    private static void NoKillToken()
    {
        var text = File.ReadAllText(RcFullWatchdogHarnessTests.HeartbeatSource, Encoding.UTF8);
        RcFullWatchdogHarnessTests.KillTokens.Where(token => text.Contains(token, StringComparison.Ordinal)).ShouldBeEmpty("kill-token");
    }

    private static PlainObject Stratum() => new(("suite", "antiphon"), ("uidDigest", "uid"), ("costDigest", "cost"), ("executionDigest", "exec"),
        ("assemblyDigest", "asm"), ("os", "linux"), ("runtime", "net9"), ("host", "host"), ("loadBand", "quiet"), ("fixtureMode", "cache"), ("concurrency", "1"));

    private static PlainObject Calibration(double n, PlainObject stratum, double e95, double emax, double m, double c, double wmax, bool censored) =>
        new(("N", (int)n), ("BoundPolicyHash", "hash-a"), ("Stratum", stratum), ("E95", e95), ("Emax", emax), ("M", m), ("C", c), ("Wmax", wmax), ("Censored", censored));

    private static IEnumerable<object> Walls(int count, double wall, string outcome) =>
        Enumerable.Range(0, count).Select(_ => (object)new PlainObject(("Wall", wall), ("Outcome", outcome), ("Censored", false)));

    private static object Sample(object? wall, string outcome, bool censored = false, int? failureCount = null)
    {
        var sample = new PlainObject(("Wall", wall), ("Outcome", outcome), ("Censored", censored));
        if (failureCount is { } failures) sample.Add("FailureCount", failures);
        return sample;
    }

    private static double Num(object? o, string name) => PsValue.Double(PsValue.Prop(o, name));

    private static bool Admitted(object o) => PsValue.IsTrue(PsValue.Prop(o, "Admitted"));

    private static string Reason(object o) => PsValue.Str(PsValue.Prop(o, "Reason"));

    private static string NewWork()
    {
        var root = Path.Combine(Path.GetTempPath(), "rcspeed-s3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteWork(string root)
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
