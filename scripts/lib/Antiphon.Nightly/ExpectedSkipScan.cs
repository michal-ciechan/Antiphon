// The expected-platform-skip source checks (moved from nightly-coverage.ps1): source gaps (a roster test whose class or
// method is absent from tests/), reason gaps (the test's reachable skip text is not its reasonPrefix) and roster drift
// (WindowsUnitSkipRoster.cs disagrees with the roster). They read only tests/ and the roster, never a chunk result,
// so a nightly run starts them as soon as the roster is loaded (Start) and takes each result at the end of the run;
// a caller with no started scan for its arguments runs that one check now. A failed check rethrows at its own call.
// The compiled method index is one per repository and process, used by a started scan alone until its join.
using System;
using System.Collections.Generic;
using System.IO;

namespace Antiphon.Nightly
{
    /// <summary>A started scan and the arguments it was started for.</summary>
    public sealed class ExpectedSkipScan
    {
        internal ExpectedSkipScan(NightlySkipScanTask task, string repoRoot, string platform, object entries)
        {
            Task = task;
            RepoRoot = repoRoot;
            Platform = platform;
            Entries = entries;
        }

        public NightlySkipScanTask Task { get; }
        public string RepoRoot { get; }
        public string Platform { get; }
        public object Entries { get; }
    }

    public static class NightlyExpectedSkips
    {
        private static readonly object Gate = new object();
        private static string _indexRoot;
        private static NightlyCsMethodIndex _index;
        private static string _foldRoot;
        private static bool _fold;
        private static NightlySkipScanTask _pending;

        private static string TestsRoot(string repoRoot) => Path.GetFullPath(Path.Combine(repoRoot ?? "", "tests"));

        private static string DriftPath(string testsRoot) => Path.Combine(testsRoot, "Antiphon.Tests", "Scripts", "WindowsUnitSkipRoster.cs");

        private static string FoldPath(string testsRoot) => Path.Combine(testsRoot, "Shared", "LiveClaude", "LiveClaudeLaunchContext.cs");

        // The run's compiled method index for this repository, made by its first user.
        private static NightlyCsMethodIndex Index(string repoRoot)
        {
            lock (Gate)
            {
                if (_index == null || !string.Equals(_indexRoot, repoRoot, StringComparison.OrdinalIgnoreCase))
                {
                    _indexRoot = repoRoot;
                    _index = new NightlyCsMethodIndex(TestsRoot(repoRoot));
                }
                return _index;
            }
        }

        // Whether LiveClaudeLaunchContext folds a refusal reason into its "20 " skip text.
        private static int Folds(string repoRoot)
        {
            lock (Gate)
            {
                if (_foldRoot == null || !string.Equals(_foldRoot, repoRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var path = FoldPath(TestsRoot(repoRoot));
                    _fold = File.Exists(path) && File.ReadAllText(path).Contains("\"20 \" + reason", StringComparison.Ordinal);
                    _foldRoot = repoRoot;
                }
                return _fold ? 1 : 0;
            }
        }

        // The roster's non-null entries as [string] platform, test and reasonPrefix, in roster order.
        private static void Columns(object entries, out string[] platforms, out string[] tests, out string[] prefixes)
        {
            var p = new List<string>();
            var t = new List<string>();
            var r = new List<string>();
            if (entries != null)
            {
                foreach (var entry in PsValue.AsArray(entries))
                {
                    if (entry == null) continue;
                    p.Add(PsValue.Str(PsValue.Prop(entry, "platform")));
                    t.Add(PsValue.Str(PsValue.Prop(entry, "test")));
                    r.Add(PsValue.Str(PsValue.Prop(entry, "reasonPrefix")));
                }
            }
            platforms = p.ToArray();
            tests = t.ToArray();
            prefixes = r.ToArray();
        }

        /// <summary>Waits for a started scan (never throws: each check's exception stays on the task).</summary>
        public static void Wait(NightlySkipScanTask task)
        {
            if (task == null) return;
            task.Join();
            lock (Gate) { if (ReferenceEquals(_pending, task)) _pending = null; }
        }

        private static void WaitPending()
        {
            NightlySkipScanTask pending;
            lock (Gate) pending = _pending;
            Wait(pending);
        }

        /// <summary>Starts the three checks for this repository, platform and roster (Start-NightlyExpectedSkipScan).</summary>
        public static ExpectedSkipScan Start(string repoRoot, string platform, object entries)
        {
            WaitPending();
            var testsRoot = TestsRoot(repoRoot);
            Columns(entries, out var platforms, out var tests, out var prefixes);
            var task = NightlySkipScanTask.Start(testsRoot, DriftPath(testsRoot), FoldPath(testsRoot), Folds(repoRoot), Index(repoRoot), platform,
                platforms, tests, prefixes);
            lock (Gate) _pending = task;
            return new ExpectedSkipScan(task, repoRoot, platform, entries);
        }

        // The finished scan when it was started for these arguments, else null. Also waits for a scan an earlier run
        // never joined, so no scan still uses the method index when a check runs now.
        private static NightlySkipScanTask Receive(ExpectedSkipScan scan, string repoRoot, string platform, object entries, bool anyPlatform)
        {
            WaitPending();
            if (scan == null) return null;
            Wait(scan.Task);
            if (!string.Equals(scan.RepoRoot, repoRoot, StringComparison.Ordinal)) return null;
            if (!anyPlatform && !string.Equals(scan.Platform ?? "", platform ?? "", StringComparison.Ordinal)) return null;
            if (!ReferenceEquals(PsOps.Base(scan.Entries), PsOps.Base(entries))) return null;
            return scan.Task;
        }

        private static NightlySkipScanTask Now(string check, string repoRoot, string platform, object entries)
        {
            var testsRoot = TestsRoot(repoRoot);
            Columns(entries, out var platforms, out var tests, out var prefixes);
            return NightlySkipScanTask.RunNow(check, testsRoot, DriftPath(testsRoot), FoldPath(testsRoot), Folds(repoRoot),
                check == "reason" ? Index(repoRoot) : null, platform, platforms, tests, prefixes);
        }

        /// <summary>Get-NightlyExpectedSkipSourceGaps: the platform's roster tests whose class or method is absent from tests/.</summary>
        public static string[] SourceGaps(string repoRoot, string platform, object entries, ExpectedSkipScan scan)
        {
            var task = Receive(scan, repoRoot, platform, entries, false) ?? Now("source", repoRoot, platform, entries);
            if (!task.SourceGapsDone) throw task.SourceGapsError;
            return task.SourceGaps;
        }

        /// <summary>Get-NightlyExpectedSkipReasonGaps: the platform's roster tests whose reachable skip text is not their reasonPrefix.</summary>
        public static string[] ReasonGaps(string repoRoot, string platform, object entries, ExpectedSkipScan scan)
        {
            var task = Receive(scan, repoRoot, platform, entries, false) ?? Now("reason", repoRoot, platform, entries);
            if (!task.ReasonGapsDone) throw task.ReasonGapsError;
            return task.ReasonGaps;
        }

        /// <summary>
        /// Get-NightlyExpectedSkipRosterDrift: { Overlap, Mismatches } - the roster tests WindowsUnitSkipRoster.cs also
        /// names, and those of them whose reason there is not the roster's reasonPrefix.
        /// </summary>
        public static object RosterDrift(string repoRoot, object entries, ExpectedSkipScan scan)
        {
            var task = Receive(scan, repoRoot, "", entries, true) ?? Now("drift", repoRoot, "", entries);
            if (!task.DriftDone) throw task.DriftError;
            var o = PsValue.Record(("Overlap", task.DriftOverlap), ("Mismatches", task.DriftMismatches));
            return o;
        }
    }
}
