// W2-nightly S12: Write-NightlyRunSummary (nightly-tests-impl.ps1, CARD-2005), the one writer of a run's summary.json:
// D-5 keeps each suite's exclusion list once (summary.suiteExclusions), dispatchProgress points at summary.suites instead
// of a second copy of every chunk row, the phase summary runs when a phase plan exists, and the document is streamed by
// NightlyJson.WriteFile (a full run's summary is tens of MB; there is no ConvertTo-Json writer to fall back to). The
// phase summary is a delegate (the caller passes the compiled TestPhaseRunSummary.Run, S09). Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The run summary writer.</summary>
    public static class NightlyRunSummary
    {
        /// <summary>The summary's nesting the writer keeps (Write-NightlyJsonFile -Depth 8).</summary>
        public const int Depth = 8;

        /// <summary>
        /// Write-NightlyRunSummary: dedupes the suites' exclusion lists into summary.suiteExclusions (a suite's list is kept
        /// once and dropped from its rows only when every row's list is the same: the same objects, else the same JSON),
        /// adds the closed dispatchProgress when the schedule's preparation completed, runs phaseSummary (with the log root)
        /// when the phase plan or its index exists, writing the phase-summary-failed marker if it fails, and streams the
        /// summary to summaryPath. A path that is not fully qualified or a value the writer cannot write throws
        /// nightly-json-write-failed and leaves no file.
        /// </summary>
        public static void Write(IDictionary summary, object suiteResults, object dispatchSchedule, string sha, DateTime completedAt, string summaryPath,
            string logRoot, string phaseTimingDirectory, Action<string> phaseSummary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            var suiteExclusions = DedupeExclusions(suiteResults);
            if (suiteExclusions.Count > 0) summary["suiteExclusions"] = suiteExclusions;
            // Bound by the exported summary digest. This diagnostic snapshot can order a later complete attempt; it never
            // supplies pass credit or skips a launch. Its chunk rows are summary.suites' rows that have a chunk: the reader
            // rebuilds them (PriorFailureHistory.SummaryDispatchProgress).
            if (dispatchSchedule != null && PsValue.IsTrue(PsValue.Prop(dispatchSchedule, "preparationComplete")))
            {
                summary["dispatchProgress"] = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "schemaVersion", 1 }, { "liveness", "closed" }, { "sha", sha ?? "" }, { "completedAt", completedAt.ToString("o", CultureInfo.InvariantCulture) },
                    { "canonicalPlanDigest", PsValue.Prop(dispatchSchedule, "canonicalPlanDigest") }, { "suitesFromSummary", true },
                };
            }
            try
            {
                var directory = string.IsNullOrWhiteSpace(phaseTimingDirectory) ? Path.Combine(logRoot ?? "", "timing") : phaseTimingDirectory;
                if (EvidenceFiles.PathExists(Path.Combine(directory, "groups-planned.json")) || EvidenceFiles.PathExists(Path.Combine(directory, "groups-planned-index.json")))
                    phaseSummary?.Invoke(logRoot);
            }
            catch (Exception)
            {
                NightlyChunkRunner.WriteFailureMarker(logRoot, phaseTimingDirectory, "phase-summary-failed");
            }
            WriteJson(summaryPath, summary, Depth);
        }

        /// <summary>
        /// D-5: every chunk row records its suite's exclusions, and with one census per suite the lists are the same - most
        /// of a full run's summary. Per suite id (case-insensitive, in the order Group-Object sorts them), the first row's
        /// list when every row's list is identical, dropped from those rows; rows keep their counts and censusDigest.
        /// </summary>
        public static OrderedDictionary DedupeExclusions(object suiteResults)
        {
            var groups = new List<(string Name, List<IDictionary> Rows)>();
            var byName = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var item in suiteResults == null ? new object[0] : PsValue.AsArray(suiteResults))
            {
                if (!(PsValue.BaseObject(item) is IDictionary row) || !row.Contains("exclusions")) continue;
                var name = PsValue.Str(row["id"]);
                if (!byName.TryGetValue(name, out var at))
                {
                    at = groups.Count;
                    byName[name] = at;
                    groups.Add((name, new List<IDictionary>()));
                }
                groups[at].Rows.Add(row);
            }
            var sorted = groups.OrderBy(g => g.Name, StringComparer.Create(CultureInfo.CurrentCulture, true)).ToList();
            var kept = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            foreach (var group in sorted)
            {
                var firstList = PsValue.AsArray(group.Rows[0]["exclusions"]);
                string firstText = null;
                var same = true;
                foreach (var row in group.Rows)
                {
                    var list = PsValue.AsArray(row["exclusions"]);
                    if (list.Length != firstList.Length) { same = false; break; }
                    if (ExclusionDedupe.SameElementReferences(list, firstList)) continue;
                    firstText = firstText ?? NightlyJson.Text(firstList, Depth);
                    var text = NightlyJson.Text(list, Depth);
                    if (firstText == null || text == null || !string.Equals(text, firstText, StringComparison.Ordinal)) { same = false; break; }
                }
                if (!same) continue;
                kept[group.Name] = firstList;
                foreach (var row in group.Rows) row.Remove("exclusions");
            }
            return kept;
        }

        /// <summary>
        /// Write-NightlyJsonFile: the value streamed to a fully qualified path, or nightly-json-write-failed (path-not-absolute,
        /// unsupported-value-or-depth, or the failure's type) with no file left.
        /// </summary>
        public static void WriteJson(string path, object value, int depth)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) throw new NightlyRefusal("nightly-json-write-failed path-not-absolute " + path);
            var reason = "";
            try { if (!NightlyJson.WriteFile(path, value, depth)) reason = "unsupported-value-or-depth"; }
            catch (Exception ex) { reason = ex.GetBaseException().GetType().Name; }
            if (reason.Length == 0) return;
            try { File.Delete(path); } catch (Exception) { }
            throw new NightlyRefusal("nightly-json-write-failed " + reason + " " + path);
        }
    }
}
