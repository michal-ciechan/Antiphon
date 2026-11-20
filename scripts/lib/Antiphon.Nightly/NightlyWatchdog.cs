// Launch watchdog admission (CARD-1514 S3, moved from nightly-watchdog-admission.ps1). Detection and refusal only:
// nothing here stops a process. A missing or unmeasured calibration keeps the suite watchdog; a measured one sets the
// launch watchdog from the calibrated error factors (E95, Emax), refusing a launch whose estimate exceeds the
// calibrated bound or whose watchdog would exceed the policy maximum.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;

namespace Antiphon.Nightly
{
    public static class NightlyWatchdog
    {
        private static readonly string[] StratumKeys =
            { "suite", "uidDigest", "costDigest", "executionDigest", "assemblyDigest", "os", "runtime", "host", "loadBand", "fixtureMode", "concurrency" };

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        // $Object.PSObject.Properties[$Name].Value: null when absent.
        private static object Field(object o, string name) => o == null ? null : PSObject.AsPSObject(o).Properties[name]?.Value;

        private static bool TryFloat(object value, out double parsed) =>
            double.TryParse(PsValue.Str(value), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

        // [decimal] arithmetic as PowerShell does it: a double converts to decimal with 15 significant digits.
        private static double CeilTo60(decimal seconds)
        {
            if (seconds <= 0) return (double)seconds;
            return (double)(decimal.Ceiling(seconds / 60m) * 60m);
        }

        private static int NearestRank(int count)
        {
            if (count <= 0) return 0;
            var index = (int)decimal.Ceiling(95m * count / 100m);
            if (index < 1) index = 1;
            if (index > count) index = count;
            return index;
        }

        /// <summary>
        /// Get-NightlyWatchdogErrorFactors: { N, E95, Emax, FailedKept, Censored } from launch samples (Wall, Outcome,
        /// Censored) against the planned seconds. A censored, wall-less or non-positive sample is not counted and marks the
        /// set censored, as does an unknown plan. E95 is the nearest-rank 95th percentile of wall/plan, at least 1; Emax the
        /// largest, at least E95.
        /// </summary>
        public static PSObject ErrorFactors(object samples, object plannedSeconds)
        {
            var planned = 0.0;
            var plannedKnown = false;
            if (plannedSeconds != null && TryFloat(plannedSeconds, out var parsedPlan) && !double.IsNaN(parsedPlan) && !double.IsInfinity(parsedPlan) && parsedPlan > 0)
            {
                planned = parsedPlan;
                plannedKnown = true;
            }
            var ratios = new List<double>();
            var failed = 0;
            var censored = false;
            var complete = 0;
            foreach (var sample in PsValue.AsArray(samples))
            {
                if (sample == null) continue;
                // $flag -eq $true: PowerShell's comparison (the string "true" and 1 count).
                if (PsOps.Eq(Field(sample, "Censored"), true, true)) { censored = true; continue; }
                var wallText = Field(sample, "Wall");
                var wall = 0.0;
                var wallKnown = false;
                if (wallText != null)
                {
                    string invariant = null;
                    var wallBase = PsOps.Base(wallText);
                    if (wallBase is string s) invariant = s;
                    else
                    {
                        try { invariant = Convert.ToString(wallBase, CultureInfo.InvariantCulture); } catch (Exception) { invariant = null; }
                    }
                    if (invariant != null) wallKnown = double.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out wall);
                }
                if (!wallKnown || double.IsNaN(wall) || double.IsInfinity(wall) || wall <= 0) { censored = true; continue; }
                complete++;
                if (PsValue.Str(Field(sample, "Outcome")) == "Failed") failed++;
                if (plannedKnown) ratios.Add(wall / planned);
            }
            if (!plannedKnown) censored = true;
            var e95 = 1.0;
            var emax = 1.0;
            if (ratios.Count > 0)
            {
                ratios.Sort();
                var picked = ratios[NearestRank(ratios.Count) - 1];
                if (picked > e95) e95 = picked;
                foreach (var ratio in ratios) if (ratio > emax) emax = ratio;
                if (emax < e95) emax = e95;
            }
            return Note("N", complete, "E95", e95, "Emax", emax, "FailedKept", failed, "Censored", censored);
        }

        /// <summary>
        /// Get-NightlyWatchdogSeconds: max(600, 2 * E95 * planned, Emax * planned + margin), rounded up to a whole minute.
        /// </summary>
        public static double WatchdogSeconds(double e95, double emax, double plannedSeconds, double marginSeconds)
        {
            var paced = 2m * (decimal)e95 * (decimal)plannedSeconds;
            var tailed = (decimal)emax * (decimal)plannedSeconds + (decimal)marginSeconds;
            var raw = 600m;
            if (paced > raw) raw = paced;
            if (tailed > raw) raw = tailed;
            return CeilTo60(raw);
        }

        /// <summary>Get-NightlyWatchdogMs: the policy's watchdogs.key milliseconds when set (truthy), else the fallback.</summary>
        public static int PolicyWatchdogMilliseconds(object policyObject, string key, int fallback)
        {
            var watchdogs = PsValue.Prop(policyObject, "watchdogs");
            if (!LanguagePrimitives.IsTrue(watchdogs)) return fallback;
            // $watchdogs.'' (a blank key) reads nothing.
            if (string.IsNullOrEmpty(key)) return fallback;
            var value = PsValue.Prop(watchdogs, key);
            if (!LanguagePrimitives.IsTrue(value)) return fallback;
            return (int)LanguagePrimitives.ConvertTo(value, typeof(int), CultureInfo.InvariantCulture);
        }

        /// <summary>Get-NightlySuiteWatchdogSeconds: the policy's suite watchdog, else 3600 s (7200 s for e2e).</summary>
        public static double SuiteWatchdogSeconds(object policyObject, string suiteId)
        {
            var fallbackMs = string.Equals(suiteId, "e2e", StringComparison.OrdinalIgnoreCase) ? 7200000 : 3600000;
            if (policyObject == null) return fallbackMs / 1000.0;
            return PolicyWatchdogMilliseconds(policyObject, suiteId, fallbackMs) / 1000.0;
        }

        /// <summary>Get-NightlyLaunchEstimateCeilingSeconds: 1800 s for e2e, else 2400 s.</summary>
        public static double LaunchEstimateCeilingSeconds(string suiteId) =>
            string.Equals(suiteId, "e2e", StringComparison.OrdinalIgnoreCase) ? 1800.0 : 2400.0;

        /// <summary>Test-NightlyWatchdogStratumMatch: every stratum key present on both sides and equal ([string], ordinal).</summary>
        public static bool StratumMatch(object launch, object bound)
        {
            if (launch == null || bound == null) return false;
            foreach (var key in StratumKeys)
            {
                var left = Field(launch, key);
                var right = Field(bound, key);
                if (left == null || right == null) return false;
                if (PsValue.Str(left) != PsValue.Str(right)) return false;
            }
            return true;
        }

        private static bool FiniteNumber(object value) =>
            value != null && TryFloat(value, out var parsed) && !double.IsNaN(parsed) && !double.IsInfinity(parsed);

        private static PSObject Decision(bool admitted, double watchdogSeconds, string reason, double e95, double emax, int n) =>
            Note("Admitted", admitted, "WatchdogSeconds", watchdogSeconds, "WatchdogMilliseconds", Convert.ToInt32(Math.Round(watchdogSeconds * 1000.0)),
                "Reason", reason ?? "", "E95", e95, "Emax", emax, "N", n);

        private static double Double(object value) => (double)LanguagePrimitives.ConvertTo(value, typeof(double), CultureInfo.InvariantCulture);

        /// <summary>
        /// Resolve-NightlyLaunchWatchdog: { Admitted, WatchdogSeconds, WatchdogMilliseconds, Reason, E95, Emax, N }. Unmeasured
        /// (no plan, or no eligible calibration): the suite watchdog, refused over it. Measured (basis measured, at least 20
        /// uncensored samples bound to this policy hash and launch stratum, sane factors): the calibrated watchdog,
        /// refused over the calibrated bound or when it would exceed the policy maximum.
        /// </summary>
        public static PSObject ResolveLaunch(string suiteId, object plannedSeconds, string basis, string policyHash, object launchStratum,
            object calibration, object policyObject)
        {
            var suiteSeconds = SuiteWatchdogSeconds(policyObject, suiteId);
            if (plannedSeconds == null) return Decision(true, suiteSeconds, "unmeasured-cost", 1, 1, 0);
            if (!TryFloat(plannedSeconds, out var planned) || double.IsNaN(planned) || double.IsInfinity(planned) || planned <= 0)
                return Decision(false, suiteSeconds, "watchdog-seconds-invalid", 1, 1, 0);
            if (planned > LaunchEstimateCeilingSeconds(suiteId)) return Decision(false, suiteSeconds, "launch-estimate-ceiling", 1, 1, 0);
            var eligible = false;
            double e95 = 1.0, emax = 1.0, margin = 0.0, bound = 0.0, wmax = 0.0;
            var n = 0;
            if ((basis ?? "") == "measured" && calibration != null)
            {
                var censored = Field(calibration, "Censored");
                var nKnown = int.TryParse(PsValue.Str(Field(calibration, "N")), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nParsed);
                var boundHash = PsValue.Str(Field(calibration, "BoundPolicyHash"));
                var hashOk = !string.IsNullOrWhiteSpace(boundHash) && boundHash == (policyHash ?? "");
                var stratumOk = StratumMatch(launchStratum, Field(calibration, "Stratum"));
                var e95Value = Field(calibration, "E95");
                var emaxValue = Field(calibration, "Emax");
                var marginValue = Field(calibration, "M");
                var boundValue = Field(calibration, "C");
                var wmaxValue = Field(calibration, "Wmax");
                var numbersOk = FiniteNumber(e95Value) && FiniteNumber(emaxValue) && FiniteNumber(marginValue) && FiniteNumber(boundValue) && FiniteNumber(wmaxValue);
                if (numbersOk)
                {
                    e95 = Double(e95Value);
                    emax = Double(emaxValue);
                    margin = Double(marginValue);
                    bound = Double(boundValue);
                    wmax = Double(wmaxValue);
                }
                // $censored -ne $true: PowerShell's comparison.
                if (PsOps.Ne(censored, true, true) && nKnown && nParsed >= 20 && hashOk && stratumOk && numbersOk && emax >= e95 && e95 >= 1 && margin >= 0 && bound > 0 && wmax > 0)
                {
                    eligible = true;
                    n = nParsed;
                }
            }
            if (!eligible)
            {
                if (planned > suiteSeconds) return Decision(false, suiteSeconds, "suite-watchdog-ceiling", e95, emax, n);
                return Decision(true, suiteSeconds, "unmeasured-cost", e95, emax, n);
            }
            if (planned > bound) return Decision(false, suiteSeconds, "launch-estimate-ceiling", e95, emax, n);
            var watchdog = WatchdogSeconds(e95, emax, planned, margin);
            var atCeiling = WatchdogSeconds(e95, emax, bound, margin);
            if (watchdog > wmax || atCeiling > wmax) return Decision(false, watchdog, "policy-max-mismatch", e95, emax, n);
            return Decision(true, watchdog, "measured", e95, emax, n);
        }

        /// <summary>Test-NightlyWatchdogKilled: the watchdog fired, unless the launch ended before it.</summary>
        public static bool Killed(double elapsedSeconds, double watchdogSeconds, bool terminal) =>
            !(terminal && elapsedSeconds < watchdogSeconds) && elapsedSeconds >= watchdogSeconds;

        /// <summary>
        /// Get-NightlyWatchdogTimeoutDisposition: a timed-out launch is incomplete and not passed (reason watchdog-timeout,
        /// or completed-but-hung when the tests had finished).
        /// </summary>
        public static PSObject TimeoutDisposition(bool trxPresent, bool terminalAccounting, bool completedButHung) =>
            Note("CoverageComplete", false, "TestsPassed", false, "Incomplete", true, "Reason", completedButHung ? "completed-but-hung" : "watchdog-timeout", "Pass", false);
    }
}
