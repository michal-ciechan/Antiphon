// Launch watchdog admission (CARD-1514 S3, moved from nightly-watchdog-admission.ps1). Detection and refusal only:
// nothing here stops a process. A missing or unmeasured calibration keeps the suite watchdog; a measured one sets the
// launch watchdog from the calibrated error factors (E95, Emax), refusing a launch whose estimate exceeds the
// calibrated bound or whose watchdog would exceed the policy maximum.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Antiphon.Nightly
{
    public static class NightlyWatchdog
    {
        private static readonly string[] StratumKeys =
            { "suite", "uidDigest", "costDigest", "executionDigest", "assemblyDigest", "os", "runtime", "host", "loadBand", "fixtureMode", "concurrency" };

        private static object Note(params object[] pairs)
        {
            var properties = new (string Name, object Value)[pairs.Length / 2];
            for (var i = 0; i < pairs.Length; i += 2) properties[i / 2] = ((string)pairs[i], pairs[i + 1]);
            return PsValue.Record(properties);
        }

        // $Object.PSObject.Properties[$Name].Value: null when absent.
        private static object Field(object o, string name) => o == null ? null : PsValue.Property(o, name, out _);

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
        public static object ErrorFactors(object samples, object plannedSeconds)
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
            if (!PsValue.IsTrue(watchdogs)) return fallback;
            // $watchdogs.'' (a blank key) reads nothing.
            if (string.IsNullOrEmpty(key)) return fallback;
            var value = PsValue.Prop(watchdogs, key);
            if (!PsValue.IsTrue(value)) return fallback;
            return PsOps.ToInt(value);
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

        private static object Decision(bool admitted, double watchdogSeconds, string reason, double e95, double emax, int n) =>
            Note("Admitted", admitted, "WatchdogSeconds", watchdogSeconds, "WatchdogMilliseconds", Convert.ToInt32(Math.Round(watchdogSeconds * 1000.0)),
                "Reason", reason ?? "", "E95", e95, "Emax", emax, "N", n);

        private static double Double(object value) => PsValue.Double(value);

        /// <summary>
        /// Resolve-NightlyLaunchWatchdog: { Admitted, WatchdogSeconds, WatchdogMilliseconds, Reason, E95, Emax, N }. Unmeasured
        /// (no plan, or no eligible calibration): the suite watchdog, refused over it. Measured (basis measured, at least 20
        /// uncensored samples bound to this policy hash and launch stratum, sane factors): the calibrated watchdog,
        /// refused over the calibrated bound or when it would exceed the policy maximum.
        /// </summary>
        public static object ResolveLaunch(string suiteId, object plannedSeconds, string basis, string policyHash, object launchStratum,
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
        public static object TimeoutDisposition(bool trxPresent, bool terminalAccounting, bool completedButHung) =>
            Note("CoverageComplete", false, "TestsPassed", false, "Incomplete", true, "Reason", completedButHung ? "completed-but-hung" : "watchdog-timeout", "Pass", false);

        // ---- completion and hang evidence ------------------------------------------------------------------------

        /// <summary>
        /// Read-NightlyTrxRunSummary: { Present, Outcome, Total, Executed, Passed, Failed, NotExecuted } from the TRX
        /// ResultSummary (read while the writer may still hold it). Not present (counters -1) for a missing or unreadable
        /// file, or without total, executed and notExecuted counters.
        /// </summary>
        public static object TrxRunSummary(string path)
        {
            object Missing() => Note("Present", false, "Outcome", "", "Total", -1, "Executed", -1, "Passed", -1, "Failed", -1, "NotExecuted", -1);
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return Missing();
            var doc = new System.Xml.XmlDocument { XmlResolver = null };
            try
            {
                using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    doc.Load(stream);
            }
            catch (Exception) { return Missing(); }
            var read = NightlyTrx.RunSummary(doc);
            if (!read.Present) return Missing();
            return Note("Present", true, "Outcome", read.Outcome, "Total", read.Total, "Executed", read.Executed, "Passed", read.Passed,
                "Failed", read.Failed, "NotExecuted", read.NotExecuted);
        }

        /// <summary>Read-NightlyTextTail: the last byteCount bytes of the file as UTF-8 ("" when missing, empty or unreadable).</summary>
        public static string TextTail(string path, int byteCount)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return "";
            try
            {
                using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                {
                    var length = stream.Length;
                    if (length <= 0) return "";
                    var take = byteCount > length ? (int)length : byteCount;
                    if (take <= 0) return "";
                    stream.Seek(length - take, System.IO.SeekOrigin.Begin);
                    var buffer = new byte[take];
                    var read = stream.Read(buffer, 0, take);
                    return read <= 0 ? "" : System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch (Exception) { return ""; }
        }

        private static readonly System.Text.RegularExpressions.Regex TotalRan =
            new System.Text.RegularExpressions.Regex(@"total ran:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.RightToLeft);

        /// <summary>
        /// Test-NightlyExecutionFinished: the TRX says Completed with every test executed, and the diagnostic's tail holds
        /// "request execute stop" and a last "total ran: N" equal to the TRX total.
        /// </summary>
        public static bool ExecutionFinished(string trxPath, string diagnosticPath)
        {
            var summary = TrxRunSummary(trxPath);
            if (!(bool)PsValue.Prop(summary, "Present")) return false;
            if ((string)PsValue.Prop(summary, "Outcome") != "Completed") return false;
            var total = (int)PsValue.Prop(summary, "Total");
            if ((int)PsValue.Prop(summary, "Executed") != total || (int)PsValue.Prop(summary, "NotExecuted") != 0) return false;
            var tail = TextTail(diagnosticPath, 16384);
            if (string.IsNullOrEmpty(tail)) return false;
            // $tail.IndexOf('...'): String.IndexOf(string), current culture, as PowerShell calls it.
            if (tail.IndexOf("request execute stop", StringComparison.CurrentCulture) < 0) return false;
            var match = TotalRan.Match(tail);
            if (!match.Success) return false;
            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ran) && ran == total;
        }

        /// <summary>
        /// Format-NightlyHangChildren: "pid=P/image=I" per child identity ("pid:start", the pid part) and image, comma
        /// separated; the root alone when there are none and it is known; "" otherwise.
        /// </summary>
        public static string HangChildren(object identities, object images, int rootPid)
        {
            var ids = PsValue.AsArray(identities);
            var imgs = PsValue.AsArray(images);
            var parts = new List<string>();
            for (var i = 0; i < ids.Length; i++)
            {
                var text = PsValue.Str(ids[i]);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var pidText = text;
                var colon = text.IndexOf(':');
                if (colon > 0) pidText = text.Substring(0, colon);
                var image = i < imgs.Length && imgs[i] != null ? PsValue.Str(imgs[i]) : "";
                parts.Add("pid=" + pidText + "/image=" + image);
            }
            if (parts.Count == 0) return rootPid > 0 ? "pid=" + rootPid.ToString(CultureInfo.CurrentCulture) + "/image=" : "";
            return string.Join(",", parts);
        }

        /// <summary>
        /// Resolve-NightlyCompletedHang: { CompletedButHung, Reason } - a launch that timed out after its tests finished is
        /// completed-but-hung (with its leftover children), anything else a watchdog timeout.
        /// </summary>
        public static object CompletedHang(bool timedOut, bool finished, string suiteId, string children)
        {
            if (timedOut && finished)
            {
                var childText = string.IsNullOrWhiteSpace(children) ? "pid=0/image=" : children;
                return Note("CompletedButHung", true, "Reason", "completed-but-hung " + (suiteId ?? "") + " children=" + childText);
            }
            return Note("CompletedButHung", false, "Reason", "watchdog-timeout");
        }

        /// <summary>Write-NightlyHangCapture: the completed-hang capture file (suite, children, root pid; handles not enumerated).</summary>
        public static void WriteHangCapture(string path, string suiteId, string children, int rootPid)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) System.IO.Directory.CreateDirectory(dir);
            var lines = new[]
            {
                "completed-hang-capture", "suite=" + (suiteId ?? ""), "children=" + (children ?? ""),
                "root pid=" + rootPid.ToString(CultureInfo.CurrentCulture), "handles=not-enumerated",
            };
            System.IO.File.WriteAllText(path, string.Join("\n", lines) + "\n");
        }

        /// <summary>Get-NightlyHarnessHangField: completed-but-hung, or "".</summary>
        public static string HarnessHangField(bool completedButHung) => completedButHung ? "completed-but-hung" : "";
    }
}
