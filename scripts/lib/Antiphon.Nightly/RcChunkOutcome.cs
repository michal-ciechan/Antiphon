// CARD-1836 S6b / CARD-2008. The outcome a shadow chunk revision records (moved from rc-run-evidence.ps1).
// Read parses the launch's results (TRX, or the JSON summary) with the results-check inputs: the counters must
// agree with the rows, and a skip passes only when the expected-skip roster explains it. Bind then keeps Passed only
// when the native suite verdict and the log freshness also pass. Unreadable input is NotRun, never Passed. A recorded
// outcome grants no gate credit. Plain C#: no PowerShell types.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>One expected-platform-skip roster entry (tests/expected-platform-skips.json).</summary>
    public sealed class RcSkipEntry
    {
        public RcSkipEntry(string test, string platform, string reasonPrefix, string reasonClass)
        {
            Test = test ?? "";
            Platform = platform ?? "";
            ReasonPrefix = reasonPrefix ?? "";
            ReasonClass = reasonClass ?? "";
        }

        public string Test { get; }
        public string Platform { get; }
        public string ReasonPrefix { get; }
        /// <summary>platform (also when blank) or live-context; anything else makes the roster unreadable.</summary>
        public string ReasonClass { get; }
    }

    /// <summary>Passed, Failed or NotRun, and the reason (empty for Passed).</summary>
    public sealed class RcChunkOutcome
    {
        static readonly Regex TrxStart = new Regex("<TestRun[\\s>]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        RcChunkOutcome(string outcome, string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        public string Outcome { get; }
        public string Reason { get; }

        public static RcChunkOutcome Passed() => new RcChunkOutcome("Passed", "");
        public static RcChunkOutcome Failed(string reason) => new RcChunkOutcome("Failed", reason);
        public static RcChunkOutcome NotRun(string reason) => new RcChunkOutcome("NotRun", reason);

        /// <summary>
        /// The shadow parse of one launch. exitCode null reads as 1. A null roster tolerates no skip: the production
        /// roster is never loaded from the platform alone.
        /// </summary>
        public static RcChunkOutcome Read(int? exitCode, bool outputDrainTimedOut, string resultsPath, RcSkipEntry[] expectedSkipRoster, string platform)
        {
            var exit = exitCode ?? 1;
            if (exit != 0) return Failed("exit-" + exit.ToString(CultureInfo.InvariantCulture));
            if (outputDrainTimedOut) return NotRun("drain-timeout");
            if (string.IsNullOrWhiteSpace(resultsPath) || !File.Exists(resultsPath)) return NotRun("results-missing");
            string text;
            try { text = File.ReadAllText(resultsPath); }
            catch (Exception) { return NotRun("results-unparseable"); }
            var roster = expectedSkipRoster ?? new RcSkipEntry[0];
            if (TrxStart.IsMatch(text)) return Trx(resultsPath, text, roster, platform ?? "");
            if (!string.IsNullOrWhiteSpace(text)) return Json(text, roster, platform ?? "");
            return NotRun("results-unparseable");
        }

        /// <summary>
        /// The recorded outcome: the shadow outcome unless it is Passed and the native suite verdict (testsPassed,
        /// coverageComplete, reasons) or the log freshness (freshOk, freshReason) says otherwise. A missing value
        /// (null) is NotRun results-unreadable.
        /// </summary>
        public static RcChunkOutcome Bind(RcChunkOutcome shadow, bool? testsPassed, bool? coverageComplete, string[] reasons, bool? freshOk, string freshReason)
        {
            if (shadow == null || string.IsNullOrWhiteSpace(shadow.Outcome)) return NotRun("results-unreadable");
            if (!string.Equals(shadow.Outcome, "Passed", StringComparison.Ordinal)) return shadow;
            if (testsPassed == null || coverageComplete == null || freshOk == null) return NotRun("results-unreadable");
            if (testsPassed.Value && coverageComplete.Value && freshOk.Value) return shadow;
            var reason = FirstReason(reasons);
            if (reason.Length == 0 && !string.IsNullOrWhiteSpace(freshReason)) reason = freshReason;
            return Failed(reason.Length == 0 ? "coverage-incomplete" : reason);
        }

        static string FirstReason(IEnumerable<string> reasons)
        {
            if (reasons == null) return "";
            foreach (var reason in reasons)
                if (!string.IsNullOrWhiteSpace(reason)) return reason;
            return "";
        }

        // ---- TRX -------------------------------------------------------------------------------------------------

        static RcChunkOutcome Trx(string path, string text, RcSkipEntry[] roster, string platform)
        {
            var doc = new XmlDocument { XmlResolver = null };
            try { doc.LoadXml(text); }
            catch (Exception) { return NotRun("results-unparseable"); }
            if (doc.DocumentElement == null) return NotRun("results-unparseable");
            var summary = NightlyTrx.RunSummary(doc);
            if (!summary.Present || string.IsNullOrWhiteSpace(summary.Outcome)) return NotRun("results-summary-unreadable");
            if (!(doc.SelectSingleNode("//*[local-name()=\"Counters\"]") is XmlElement counters)
                || string.IsNullOrWhiteSpace(counters.GetAttribute("executed")) || string.IsNullOrWhiteSpace(counters.GetAttribute("failed")))
                return NotRun("results-summary-unreadable");
            // A failed counter that is not digits ("+0", "x") is NotRun, never zero.
            var failedText = counters.GetAttribute("failed");
            if (!AllDigits(failedText) || !int.TryParse(failedText, NumberStyles.None, CultureInfo.InvariantCulture, out var failed))
                return NotRun("results-summary-unreadable");
            if (!string.Equals(summary.Outcome, "Completed", StringComparison.Ordinal)) return Failed("summary-outcome");
            if (TestPhaseFiles.Kind(path, out _) != "regular") return NotRun("results-unreadable");
            var read = NightlyTrx.Identities(doc);
            if (read.Error.Length > 0) return NotRun("results-unreadable");
            return Checked(read.Rows, summary.Total, summary.Executed, failed, true, roster, platform);
        }

        static bool AllDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text)
                if (c < '0' || c > '9') return false;
            return true;
        }

        // ---- JSON summary ------------------------------------------------------------------------------------------

        // {"executed":n,"failed":n,"coverageComplete":bool,"rows":[{"Outcome":..,"Identity":..,"DebugTrace":..}]}.
        // Names match ignoring case, as ConvertFrom-Json's objects did; a later duplicate wins, and names that differ
        // only in case are unparseable.
        static RcChunkOutcome Json(string text, RcSkipEntry[] roster, string platform)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 1024 }); }
            catch (Exception) { return NotRun("results-unparseable"); }
            using (doc)
            {
                if (!CaseDistinct(doc.RootElement)) return NotRun("results-unparseable");
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return NotRun("results-summary-unreadable");
                if (!TryMember(root, "executed", out var executedEl) || !TryMember(root, "failed", out var failedEl)
                    || !TryMember(root, "coverageComplete", out var coverageEl) || !TryMember(root, "rows", out var rowsEl))
                    return NotRun("results-summary-unreadable");
                if (coverageEl.ValueKind != JsonValueKind.True && coverageEl.ValueKind != JsonValueKind.False) return NotRun("results-summary-unreadable");
                if (!WholeNumber(executedEl, out var executed) || !WholeNumber(failedEl, out var failed)) return NotRun("results-summary-unreadable");
                if (rowsEl.ValueKind == JsonValueKind.Null) return NotRun("results-summary-unreadable");
                var rows = new List<TrxIdentity>();
                foreach (var row in rowsEl.ValueKind == JsonValueKind.Array ? Items(rowsEl) : new[] { rowsEl })
                {
                    if (row.ValueKind != JsonValueKind.Object) return NotRun("results-unreadable");
                    if (!TryMember(row, "Outcome", out var outcomeEl) || !TryMember(row, "Identity", out var identityEl)) return NotRun("results-unreadable");
                    var outcome = Text(outcomeEl);
                    var identity = Text(identityEl);
                    if (string.IsNullOrWhiteSpace(outcome) || string.IsNullOrWhiteSpace(identity)) return NotRun("results-unreadable");
                    var trace = TryMember(row, "DebugTrace", out var traceEl) ? Text(traceEl) ?? "" : "";
                    var className = "";
                    var methodName = "";
                    var dot = identity.LastIndexOf('.');
                    if (dot > 0 && dot < identity.Length - 1)
                    {
                        className = identity.Substring(0, dot);
                        methodName = identity.Substring(dot + 1);
                    }
                    rows.Add(new TrxIdentity { Outcome = outcome, Identity = identity, ClassName = className, MethodName = methodName, DebugTrace = trace, Display = identity });
                }
                if (rows.Count == 0) return NotRun("results-summary-unreadable");
                return Checked(rows.ToArray(), rows.Count, executed, failed, coverageEl.ValueKind == JsonValueKind.True, roster, platform);
            }
        }

        static IEnumerable<JsonElement> Items(JsonElement array)
        {
            foreach (var item in array.EnumerateArray()) yield return item;
        }

        static bool CaseDistinct(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    if (!CaseDistinct(item)) return false;
                return true;
            }
            if (element.ValueKind != JsonValueKind.Object) return true;
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (names.TryGetValue(property.Name, out var seen) && !string.Equals(seen, property.Name, StringComparison.Ordinal)) return false;
                names[property.Name] = property.Name;
                if (!CaseDistinct(property.Value)) return false;
            }
            return true;
        }

        // The last property whose name matches ignoring case.
        static bool TryMember(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            var found = false;
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                found = true;
            }
            return found;
        }

        // A string, number or boolean as text; null (and an object or array) as null.
        static string Text(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                default: return null;
            }
        }

        // A whole number in Int32 range: a JSON integer (1.0 counts), or a string that parses as one.
        static bool WholeNumber(JsonElement value, out int parsed)
        {
            parsed = 0;
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out parsed)) return true;
                if (value.TryGetDouble(out var real) && real == Math.Floor(real) && real >= int.MinValue && real <= int.MaxValue)
                {
                    parsed = (int)real;
                    return true;
                }
                return false;
            }
            if (value.ValueKind == JsonValueKind.String)
                return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
            return false;
        }

        // ---- the results check -------------------------------------------------------------------------------------

        // The coverage verdict of the native results check for one launch at exit 0, with no sha, ref or policy
        // expectation, no discovery and no stale-roster warnings; then the reported counters must agree with the rows.
        static RcChunkOutcome Checked(TrxIdentity[] rows, int reportedTotal, int reportedExecuted, int reportedFailed, bool callerCoverageComplete,
            RcSkipEntry[] roster, string platform)
        {
            foreach (var entry in roster)
                if (entry != null && ReasonClassOf(entry) == null) return NotRun("results-unreadable");
            var coverageComplete = true;
            var testsPassed = true;
            var reasons = new List<string>();
            if (rows.Length == 0)
            {
                coverageComplete = false;
                testsPassed = false;
                reasons.Add("zero-terminal-rows");
            }
            var failed = new List<TrxIdentity>();
            var skipped = new List<TrxIdentity>();
            var unknown = new List<TrxIdentity>();
            var unexecuted = new List<TrxIdentity>();
            var passed = 0;
            foreach (var row in rows)
            {
                switch (NightlyClassNames.NormalizedOutcome(row.Outcome))
                {
                    case "Passed": passed++; break;
                    case "Failed": failed.Add(row); break;
                    case "Skipped": skipped.Add(row); break;
                    case "NotExecuted": if (!ExpectedSkip(row, platform, roster)) unexecuted.Add(row); break;
                    default: unknown.Add(row); break;
                }
            }
            if (failed.Count > 0) { testsPassed = false; reasons.Add("failed-row"); }
            if (unexecuted.Count > 0) { coverageComplete = false; reasons.Add("unexecuted-outcome:" + Identities(unexecuted)); }
            if (unknown.Count > 0) { coverageComplete = false; testsPassed = false; reasons.Add("unknown-outcome:" + Identities(unknown)); }
            if (skipped.Count > 0) { coverageComplete = false; reasons.Add("skipped:" + Identities(skipped)); }
            if (reportedExecuted != passed + failed.Count || reportedFailed != failed.Count) return NotRun("results-counter-mismatch");
            if (reportedTotal != rows.Length) return NotRun("results-counter-mismatch");
            if (testsPassed && coverageComplete && callerCoverageComplete && reportedFailed == 0) return Passed();
            var first = FirstReason(reasons);
            return Failed(first.Length == 0 ? "coverage-incomplete" : first);
        }

        static string Identities(List<TrxIdentity> rows)
        {
            var names = new string[rows.Count];
            for (var i = 0; i < names.Length; i++) names[i] = rows[i].Identity;
            return string.Join(",", names);
        }

        // Find-NightlyExpectedSkipEntry: the roster explains this NotExecuted row on this platform - its test identity,
        // a platform-scoped class on the same platform (live-context on any), and the exact skip trace.
        static bool ExpectedSkip(TrxIdentity row, string platform, RcSkipEntry[] roster)
        {
            if (string.IsNullOrWhiteSpace(platform)) return false;
            foreach (var entry in roster)
            {
                if (entry == null || !string.Equals(entry.Test, row.Identity, StringComparison.Ordinal)) continue;
                if (entry.Platform != "windows" && entry.Platform != "linux") continue;
                if (ReasonClassOf(entry) == "platform" && entry.Platform != platform) continue;
                if (string.Equals(row.DebugTrace, "Skipped: " + entry.ReasonPrefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        static string ReasonClassOf(RcSkipEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.ReasonClass)) return "platform";
            return entry.ReasonClass == "platform" || entry.ReasonClass == "live-context" ? entry.ReasonClass : null;
        }
    }
}
