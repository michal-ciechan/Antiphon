// The end-of-run expected-skip scans of a nightly run (source gaps, reason gaps and roster drift;
// NightlyExpectedSkips in ExpectedSkipScan.cs) as one background task. They read only tests/ and the roster, never a
// chunk result, so NightlyExpectedSkips.Start starts this as soon as the roster is loaded and each check takes its
// result at the end of the run (the join); RunNow runs one check on the calling thread. The tests/ tree is the run's pinned checkout: it does not change during the run. Nothing is kept
// across runs: each run starts its own task and the results live only in that run's scan object.
//
// The task never faults. Each stage catches its own exception and reports itself not done; the check for that
// stage rethrows that exception at the join, so a failure surfaces in the check's own place and the warnings
// before it are appended in the same order as before.
//
// The comparisons are PowerShell's, as in the functions: -ceq / -cne on strings are invariant-culture
// (linguistic) comparisons; @{} keys are OrdinalIgnoreCase; [regex] patterns are case-sensitive; string
// .LastIndexOf('.') is the current culture, so the task runs with the caller's culture.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    public sealed class NightlySkipScanTask
    {
        private static readonly Regex ConstRx = new Regex(@"const string ([A-Za-z_][A-Za-z0-9_]*) = ""((?:\\.|[^""\\])*)""");
        private static readonly Regex NewRx = new Regex(@"new\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*-?\d+\s*,\s*(?:([A-Za-z_][A-Za-z0-9_]*)|""((?:\\.|[^""\\])*)"")");
        private const string RefusalFold = "\"20 \" + reason";

        private readonly string _testsRoot;
        private readonly string _driftPath;
        private readonly string _foldPath;
        private int _fold;
        private readonly NightlyCsMethodIndex _index;
        private readonly string _platform;
        private readonly string[] _platforms;
        private readonly string[] _tests;
        private readonly string[] _prefixes;
        private readonly CultureInfo _culture;
        private readonly CultureInfo _uiCulture;
        private Task _task;

        public bool SourceGapsDone { get; private set; }
        public string[] SourceGaps { get; private set; }
        public Exception SourceGapsError { get; private set; }
        public bool ReasonGapsDone { get; private set; }
        public string[] ReasonGaps { get; private set; }
        public Exception ReasonGapsError { get; private set; }
        public bool DriftDone { get; private set; }
        public string[] DriftOverlap { get; private set; }
        public string[] DriftMismatches { get; private set; }
        public Exception DriftError { get; private set; }

        private NightlySkipScanTask(string testsRoot, string driftPath, string foldPath, int foldKnown, NightlyCsMethodIndex index,
            string platform, string[] platforms, string[] tests, string[] prefixes)
        {
            _testsRoot = testsRoot;
            _driftPath = driftPath;
            _foldPath = foldPath;
            _fold = foldKnown;
            _index = index;
            _platform = platform ?? "";
            _platforms = platforms ?? new string[0];
            _tests = tests ?? new string[0];
            _prefixes = prefixes ?? new string[0];
            _culture = CultureInfo.CurrentCulture;
            _uiCulture = CultureInfo.CurrentUICulture;
        }

        /// <summary>
        /// Starts the three scans. The entry arrays are the roster's non-null entries, [string]-converted
        /// (platform, test, reasonPrefix), in roster order. foldKnown: Test-NightlyLiveClaudeRefusalFolds' cached
        /// answer for this repository (0 or 1), or -1 to read foldPath as it would. index: the run's compiled method
        /// index ($script:NightlyCsMethodIndex.Native), used by this task alone until the join.
        /// </summary>
        public static NightlySkipScanTask Start(string testsRoot, string driftPath, string foldPath, int foldKnown, NightlyCsMethodIndex index,
            string platform, string[] platforms, string[] tests, string[] prefixes)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            var scan = new NightlySkipScanTask(testsRoot, driftPath, foldPath, foldKnown, index, platform, platforms, tests, prefixes);
            scan._task = Task.Factory.StartNew(scan.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return scan;
        }

        /// <summary>
        /// One check ("source", "reason" or "drift") now, on the calling thread, for a caller with no started scan for
        /// its arguments. Its result, or its exception, is held as a started scan holds it. index: needed by "reason".
        /// </summary>
        public static NightlySkipScanTask RunNow(string check, string testsRoot, string driftPath, string foldPath, int foldKnown, NightlyCsMethodIndex index,
            string platform, string[] platforms, string[] tests, string[] prefixes)
        {
            var scan = new NightlySkipScanTask(testsRoot, driftPath, foldPath, foldKnown, index, platform, platforms, tests, prefixes);
            switch (check)
            {
                case "source":
                    try { scan.SourceGaps = scan.ScanSourceGaps(); scan.SourceGapsDone = true; }
                    catch (Exception ex) { scan.SourceGapsError = ex; }
                    break;
                case "reason":
                    if (index == null) throw new ArgumentNullException(nameof(index));
                    try { scan.ReasonGaps = scan.ScanReasonGaps(); scan.ReasonGapsDone = true; }
                    catch (Exception ex) { scan.ReasonGapsError = ex; }
                    break;
                case "drift":
                    try { scan.ScanDrift(); scan.DriftDone = true; }
                    catch (Exception ex) { scan.DriftError = ex; }
                    break;
                default:
                    throw new ArgumentException("unknown expected-skip check " + check, nameof(check));
            }
            return scan;
        }

        public bool IsCompleted => _task == null || _task.IsCompleted;

        /// <summary>Waits for the scans. Never throws: every stage's exception is held in its *Error property.</summary>
        public void Join()
        {
            if (_task != null) _task.Wait();
        }

        private static bool PsEquals(string a, string b) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(a ?? "", b ?? "", CompareOptions.None) == 0;

        private void Run()
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = _culture;
                Thread.CurrentThread.CurrentUICulture = _uiCulture;
            }
            catch (Exception) { }
            try { SourceGaps = ScanSourceGaps(); SourceGapsDone = true; }
            catch (Exception ex) { SourceGapsError = ex; }
            try { ReasonGaps = ScanReasonGaps(); ReasonGapsDone = true; }
            catch (Exception ex) { ReasonGapsError = ex; }
            try { ScanDrift(); DriftDone = true; }
            catch (Exception ex) { DriftError = ex; }
        }

        /// <summary>Get-NightlyExpectedSkipSourceGaps' compiled path.</summary>
        private string[] ScanSourceGaps()
        {
            var wanted = new List<string>();
            for (var i = 0; i < _tests.Length; i++)
            {
                if (!PsEquals(_platforms[i], _platform)) continue;
                wanted.Add(_tests[i] ?? "");
            }
            if (wanted.Count == 0) return new string[0];
            return NightlyTestSources.ExpectedSkipSourceGaps(_testsRoot, wanted.ToArray()) ?? new string[0];
        }

        /// <summary>Get-NightlyExpectedSkipReasonGaps over Get-NightlyReachableSkipTexts' compiled path.</summary>
        private string[] ScanReasonGaps()
        {
            var gaps = new List<string>();
            for (var i = 0; i < _tests.Length; i++)
            {
                if (!PsEquals(_platforms[i], _platform)) continue;
                var test = _tests[i] ?? "";
                var prefix = _prefixes[i] ?? "";
                var found = false;
                foreach (var candidate in ReachableSkipTexts(test))
                {
                    if (PsEquals(candidate, prefix)) { found = true; break; }
                }
                if (!found) gaps.Add(test);
            }
            return gaps.ToArray();
        }

        private string[] ReachableSkipTexts(string identity)
        {
            var methodDot = identity.LastIndexOf(".", StringComparison.CurrentCulture);
            if (methodDot < 1) return new string[0];
            var method = identity.Substring(methodDot + 1);
            var head = identity.Substring(0, methodDot);
            var classDot = head.LastIndexOf(".", StringComparison.CurrentCulture);
            if (classDot < 1) return new string[0];
            var className = head.Substring(classDot + 1);
            var ns = head.Substring(0, classDot);
            if (_fold < 0)
            {
                // Test-NightlyLiveClaudeRefusalFolds.
                var folds = false;
                if (File.Exists(_foldPath))
                {
                    var source = File.ReadAllText(_foldPath);
                    if (source.Contains(RefusalFold, StringComparison.Ordinal)) folds = true;
                }
                _fold = folds ? 1 : 0;
            }
            return _index.ReachableSkipTexts(className, method, ns, _fold == 1) ?? new string[0];
        }

        /// <summary>Get-NightlyExpectedSkipRosterDrift.</summary>
        private void ScanDrift()
        {
            var reasonsById = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_driftPath))
            {
                var text = File.ReadAllText(_driftPath);
                var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in ConstRx.Matches(text))
                {
                    constants[match.Groups[1].Value] = NightlyCsMethodIndex.StringLiteral(match.Groups[2].Value);
                }
                foreach (Match match in NewRx.Matches(text))
                {
                    var id = match.Groups[1].Value + "." + match.Groups[2].Value + "." + match.Groups[3].Value;
                    var reason = "";
                    if (match.Groups[4].Success && !string.IsNullOrWhiteSpace(match.Groups[4].Value))
                    {
                        if (constants.TryGetValue(match.Groups[4].Value, out var constant)) reason = constant ?? "";
                    }
                    else if (match.Groups[5].Success)
                    {
                        reason = NightlyCsMethodIndex.StringLiteral(match.Groups[5].Value);
                    }
                    if (!reasonsById.TryGetValue(id, out var reasons))
                    {
                        reasons = new List<string>();
                        reasonsById[id] = reasons;
                    }
                    reasons.Add(reason);
                }
            }
            var overlap = new List<string>();
            var mismatches = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _tests.Length; i++)
            {
                var id = _tests[i] ?? "";
                if (string.IsNullOrWhiteSpace(id) || seen.Contains(id)) continue;
                if (!reasonsById.TryGetValue(id, out var reasons)) continue;
                seen.Add(id);
                overlap.Add(id);
                var prefix = _prefixes[i] ?? "";
                var hit = false;
                foreach (var reason in reasons)
                {
                    if (PsEquals(reason, prefix)) { hit = true; break; }
                }
                if (!hit) mismatches.Add(id);
            }
            DriftOverlap = overlap.ToArray();
            DriftMismatches = mismatches.ToArray();
        }
    }
}
