// CARD-1782 and successors: the pre-land impacted-classes selector, the antiphon-scripts impacted-classes command. The
// 2026-11-22 plan (2026-11-22-remaining-ps1-to-csharp.md, unit 4) ported it from scripts/impacted-classes.ps1, and its
// -ReleaseEvidence envelope from scripts/lib/release-impact.ps1; both are deleted, and the inputs are in
// ImpactedClassesInputs.cs, the envelope in ReleaseImpactEvidence.cs.
//
// Read-only. It maps changed paths (--range through git diff --name-only, --path, --file-list) to test classes through
// the hand table tests/impacted-classes.json, the derived impact index tests/derived-impact-index.json (Code/Review
// output only) and the windows lanes, and prints one treenodeFilter row per test project. --merge-subset always emits the
// fixed high-radius classes and methods and every row the paths select, with CHECKPOINT row ids, land classes split four
// to a row (CARD-1783, CARD-1801 D-1). --audit checks every glob matches a tracked file and every class exists. No cache:
// every run reads the table, the index, git and the tests tree, so maximum staleness is zero.
//
// Exit 0 selected; 1 a table, catalog or index that cannot be used (it names the row), a fixed class missing, or the
// audit failed; 2 usage (--range with --path or --file-list, --merge-subset with --audit, no mode), git failed, or a
// release-evidence refusal. With --json the refusals print an ok=false object on stdout too. ANTIPHON_SELECTOR_TIMING=1
// adds one SELECTOR_TIMING line on stderr. Selector identity (SelectorFiles): these three sources, embedded in the
// assembly; the release evidence binds them. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The impacted-classes command's options (the parameters of the deleted scripts/impacted-classes.ps1).</summary>
    public sealed class ImpactedClassesOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "impacted-classes --range <git-range> | --file-list <file> | --path <path,path> | --merge-subset [--range <git-range> | --file-list <file> | --path <path,path>]"
            + " | --repository-invariants [--json] [--audit] [--repo <dir>] [--table <json>] | --release-evidence --baseline-sha <sha> --candidate-sha <sha> --target-platform <linux|windows> --json [--repo <dir>]";

        private static readonly string[] Valued = { "--range", "--file-list", "--path", "--repo", "--table", "--baseline-sha", "--candidate-sha", "--target-platform" };
        private static readonly string[] Switches = { "--json", "--audit", "--merge-subset", "--repository-invariants", "--release-evidence" };

        /// <summary>A git range whose changed paths are selected (and whose added lines the literal rows read); null when not given.</summary>
        public string Range { get; init; }

        /// <summary>A file of changed paths, one per line; null when not given.</summary>
        public string FileList { get; init; }

        /// <summary>Changed repository-relative paths; null when not given.</summary>
        public IReadOnlyList<string> Paths { get; init; }

        /// <summary>The repository read; empty: SourceRoot.</summary>
        public string Repo { get; init; }

        /// <summary>The table (relative to the repository); null when not given: tests/impacted-classes.json.</summary>
        public string Table { get; init; }

        /// <summary>The selection (or audit) as JSON instead of the text lines.</summary>
        public bool Json { get; init; }

        /// <summary>Check the table against the tree instead of selecting.</summary>
        public bool Audit { get; init; }

        /// <summary>The Merge batch's subset: the fixed classes and every row the paths select.</summary>
        public bool MergeSubset { get; init; }
        public bool RepositoryInvariants { get; init; }

        /// <summary>The release-evidence envelope (CARD-1820 S1R1) instead of a selection.</summary>
        public bool ReleaseEvidence { get; init; }

        /// <summary>Release evidence: the baseline commit (a full lowercase id); null when not given.</summary>
        public string BaselineSha { get; init; }

        /// <summary>Release evidence: the candidate commit; null when not given.</summary>
        public string CandidateSha { get; init; }

        /// <summary>Release evidence: linux or windows; null when not given.</summary>
        public string TargetPlatform { get; init; }

        /// <summary>The checkout this selector was built from: the default repository and the release algorithm's source.</summary>
        public string SourceRoot { get; init; }

        /// <summary>Where a relative --repo or --file-list is resolved; empty: this process's directory.</summary>
        public string CurrentDirectory { get; init; }

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line.</summary>
        public static ImpactedClassesOptions Parse(IReadOnlyList<string> arguments, string currentDirectory, string sourceRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var paths = flags.Value("--path");
            return new ImpactedClassesOptions
            {
                Range = flags.Value("--range"), FileList = flags.Value("--file-list"), Repo = flags.Value("--repo", ""), Table = flags.Value("--table"),
                // A comma-separated list; each entry trimmed, a blank one kept (and skipped by the selection).
                Paths = paths == null ? null : paths.Split(',').Select(path => path.Trim()).ToArray(),
                Json = flags.Switch("--json"), Audit = flags.Switch("--audit"), MergeSubset = flags.Switch("--merge-subset"), RepositoryInvariants = flags.Switch("--repository-invariants"),
                ReleaseEvidence = flags.Switch("--release-evidence"), BaselineSha = flags.Value("--baseline-sha"), CandidateSha = flags.Value("--candidate-sha"),
                TargetPlatform = flags.Value("--target-platform"), SourceRoot = sourceRoot, CurrentDirectory = currentDirectory,
            };
        }
    }

    /// <summary>The impacted-classes selector and its command.</summary>
    public static class ImpactedClasses
    {
        /// <summary>The hand table.</summary>
        public const string TablePath = "tests/impacted-classes.json";

        /// <summary>The derived impact index.</summary>
        public const string IndexPath = "tests/derived-impact-index.json";

        /// <summary>The selection catalog whose remainderProjects are test projects of their own.</summary>
        public const string CatalogPath = "tests/test-selection-policy.json";

        private static readonly string[] Selector =
        {
            "scripts/lib/Antiphon.Nightly/ImpactedClasses.cs", "scripts/lib/Antiphon.Nightly/ImpactedClassesInputs.cs",
            "scripts/lib/Antiphon.Nightly/ReleaseImpactEvidence.cs",
        };

        /// <summary>The selector identity: the sources the release evidence's algorithm digest binds (each embedded as impacted/&lt;file name&gt;).</summary>
        public static string[] SelectorFiles => (string[])Selector.Clone();

        /// <summary>The running selector's own bytes of a SelectorFiles path.</summary>
        public static byte[] RunningSource(string path)
        {
            if (Array.IndexOf(Selector, path) < 0) throw new ArgumentException("not a selector source: " + path, nameof(path));
            using var stream = typeof(ImpactedClasses).Assembly.GetManifestResourceStream("impacted/" + Path.GetFileName(path))
                ?? throw new InvalidOperationException("selector source unavailable: " + path);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }

        /// <summary>The fixed high-radius classes every Merge batch runs (CARD-1783, CARD-1801).</summary>
        public static readonly IReadOnlyList<string> FixedClasses = new[]
        {
            "AgentTaskLandHeldNotificationTests", "AgentTaskLandNotificationParkFollowUpTests", "AgentTaskLandNotificationPersistenceTests",
            "AgentTaskLandNotificationReconcileLoadTests", "AgentTaskLandNotificationRecoveryTests", "AgentTaskLandNotificationRescanTests",
            "AgentTaskLandReceiptWatermarkSafetyTests", "AgentTaskLandReceiptWatermarkTests", "AppDbContextModelTests", "CheckpointReceiptSafetyTests",
            "ContractSnapshotTests", "HostCleanupCheckpointReceiptTests", "ReceiptScanDestinationIntegrationTests", "TestTimingOperationLabelTests",
        };

        /// <summary>The fixed methods: the A5 pair, the Linux-only census cases (CARD-1801).</summary>
        public static readonly IReadOnlyList<string> FixedMethods = new[]
        {
            "ReleaseGatePlatformTests/C1686_LinuxOnlyCensus", "ReleaseGatePlatformTests/LinuxOnlyWindowsExclusionPolicy",
        };

        /// <summary>The selection, audit or release envelope for the options; stdout and stderr on the host. The exit code.</summary>
        public static int Run(ImpactedClassesOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var run = new ImpactedClassesRun(options, host);
            try
            {
                return run.Execute();
            }
            catch (ImpactedClassesStop stop)
            {
                return stop.Code;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                host.Error.WriteLine("impacted-classes failed: " + e.Message);
                return 1;
            }
        }
    }

    /// <summary>The end of a run with its exit code, from inside a step (the script's exit).</summary>
    internal sealed class ImpactedClassesStop : Exception
    {
        public ImpactedClassesStop(int code) : base("exit " + code.ToString(CultureInfo.InvariantCulture))
        {
            Code = code;
        }

        public int Code { get; }
    }

    /// <summary>One filter row: its CHECKPOINT id (merge rows), its project and its treenodeFilter.</summary>
    internal sealed record ImpactedRow(string Id, string Project, string Filter);

    /// <summary>One owed capped path: the classes beyond the cap.</summary>
    internal sealed record ImpactedOwed(string Path, int Count, IReadOnlyList<string> First);

    /// <summary>The derived selection of the changed paths (CARD-1820).</summary>
    internal sealed class ImpactedDerived
    {
        public HashSet<string> Whole { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Windows { get; } = new HashSet<string>(StringComparer.Ordinal);
        public List<string> Methods { get; } = new List<string>();
        public List<string> Derived { get; set; } = new List<string>();
        public List<string> Skipped { get; set; } = new List<string>();
        public List<ImpactedOwed> Owed { get; set; } = new List<ImpactedOwed>();
    }

    /// <summary>One run of the selector: the script's body, step by step.</summary>
    internal sealed class ImpactedClassesRun
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex LineBreak = new Regex("\r?\n", RegexOptions.CultureInvariant);
        private static readonly Regex AuditScript = new Regex(@"^scripts/audit-land-verification.*\.ps1$", RegexOptions.CultureInvariant);

        private readonly ImpactedClassesOptions _options;
        private readonly NightlyHost _host;
        private readonly bool _json;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<KeyValuePair<string, double>> _timing = new List<KeyValuePair<string, double>>
        {
            new KeyValuePair<string, double>("tableMs", 0), new KeyValuePair<string, double>("censusMs", 0), new KeyValuePair<string, double>("gitMs", 0),
            new KeyValuePair<string, double>("jsonInputMs", 0), new KeyValuePair<string, double>("jsonOutputMs", 0),
        };

        private string _repo;
        private ImpactedClassesCensus _census;
        private HashSet<string> _censusNames;
        private List<string> _projectDirs;
        private ImpactedDerivedIndex _index;
        private List<string> _tracked;
        private string _addedText;

        public ImpactedClassesRun(ImpactedClassesOptions options, NightlyHost host)
        {
            _options = options;
            _host = host;
            _json = options.Json;
        }

        public int Execute()
        {
            var cwd = string.IsNullOrWhiteSpace(_options.CurrentDirectory) ? null : _options.CurrentDirectory;
            var repo = !string.IsNullOrWhiteSpace(_options.Repo) ? _options.Repo : !string.IsNullOrWhiteSpace(_options.SourceRoot) ? _options.SourceRoot : cwd ?? ".";
            _repo = Full(repo, cwd);
            string table;
            if (string.IsNullOrWhiteSpace(_options.Table)) table = Path.Combine(_repo, "tests", "impacted-classes.json");
            else table = Path.IsPathRooted(_options.Table) ? _options.Table : Path.Combine(_repo, _options.Table);

            var releaseArguments = _options.BaselineSha != null || _options.CandidateSha != null || _options.TargetPlatform != null;
            if (!_options.ReleaseEvidence && releaseArguments) RefuseRelease("--baseline-sha, --candidate-sha and --target-platform require --release-evidence");
            if (_options.ReleaseEvidence) return ReleaseEvidence();

            // A switch clash is usage, not a table error, and it is decided before the table is read.
            var rangeSet = !string.IsNullOrWhiteSpace(_options.Range);
            var pathSet = _options.Paths != null && _options.Paths.Any(path => !string.IsNullOrWhiteSpace(path));
            var listSet = !string.IsNullOrWhiteSpace(_options.FileList);
            if (rangeSet && (pathSet || listSet))
            {
                const string message = "impacted-classes refused: --range cannot be combined with --path or --file-list";
                _host.Error.WriteLine(message);
                if (_json) ExitJson(RefusalRecord(message), 2);
                throw new ImpactedClassesStop(2);
            }
            if (_options.MergeSubset && _options.Audit)
            {
                const string message = "impacted-classes refused: --merge-subset cannot be combined with --audit";
                _host.Error.WriteLine(message);
                if (_json)
                {
                    ExitJson(new ImpactedRecord().Add("ok", false).Add("mode", "merge-subset").Add("emptyGlobs", Empty).Add("missingClasses", Empty).Add("kinds", Empty)
                        .Add("globHits", Empty).Add("derivedFiles", Empty).Add("error", message), 2);
                }
                throw new ImpactedClassesStop(2);
            }

            if (!File.Exists(table) && !Directory.Exists(table))
            {
                _host.Error.WriteLine("impacted-classes table was not found: " + table);
                if (_json) ExitJson(RefusalRecord("table not found: " + table), 1);
                throw new ImpactedClassesStop(1);
            }
            var tableClock = Stopwatch.StartNew();
            ImpactedTable parsed;
            JsonDocument document = null;
            try
            {
                var raw = File.ReadAllText(table);
                var jsonClock = Stopwatch.StartNew();
                if (!string.IsNullOrWhiteSpace(raw)) document = JsonDocument.Parse(raw, ImpactedJson.ReadOptions);
                AddTiming("jsonInputMs", jsonClock.Elapsed.TotalMilliseconds);
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                _host.Error.WriteLine("impacted-classes table is not valid JSON: " + e.Message);
                if (_json) ExitJson(RefusalRecord(e.Message), 1);
                throw new ImpactedClassesStop(1);
            }
            using (document)
            {
                try { parsed = ImpactedTable.Parse(document?.RootElement); }
                catch (ImpactedTableRefusal refusal) { throw RefuseTable(refusal.Where, refusal.Problem); }
            }

            _projectDirs = ProjectDirectories();
            SetTiming("tableMs", tableClock.Elapsed.TotalMilliseconds);
            var censusClock = Stopwatch.StartNew();
            // One walk: class declarations and downgrade-pattern files. A file is a derive hit when any pattern matches,
            // so the patterns are one alternation (each in its own group): one pass per file, the same verdict.
            var derive = new List<Regex>();
            if (parsed.DerivePatterns.Count > 0)
                derive.Add(new Regex(string.Join("|", parsed.DerivePatterns.Select(pattern => "(?:" + pattern + ")")), RegexOptions.CultureInvariant | RegexOptions.Compiled));
            SetTiming("censusCompileMs", 0);
            var walkClock = Stopwatch.StartNew();
            _census = ImpactedClassesCensus.Scan(_repo, ImpactedClassesCensus.Declaration, derive, _options.MergeSubset || _options.RepositoryInvariants);
            SetTiming("censusWalkMs", walkClock.Elapsed.TotalMilliseconds);
            SetTiming("censusReadMs", _census.ReadMilliseconds);
            SetTiming("censusDeclarationsMs", _census.DeclarationMilliseconds);
            SetTiming("censusDeriveMs", _census.DeriveMilliseconds);
            SetTiming("censusMs", censusClock.Elapsed.TotalMilliseconds);

            // A named class must be declared (ordinal: the census keys keep their first spelling).
            _censusNames = new HashSet<string>(_census.FilesByClass.Keys, StringComparer.Ordinal);
            foreach (var kind in parsed.Kinds)
            {
                var named = kind.Classes.Concat(kind.Methods.Where(m => m.IndexOf('/') > 0).Select(m => m.Substring(0, m.IndexOf('/'))));
                foreach (var className in named)
                    if (!_censusNames.Contains(className)) throw RefuseTable(kind.Id, "class '" + className + "' was not found");
            }

            if (_options.RepositoryInvariants)
            {
                if (_options.MergeSubset || _options.Audit || rangeSet || pathSet || listSet)
                    throw new NightlyRefusal("repository-invariants-does-not-accept-impact-options");
                return Merge(new List<ImpactedKind>(), new List<string>(), new HashSet<string>(), MethodMap());
            }

            if (_options.Audit) return Audit(parsed);

            var changed = new List<string>();
            var useAdded = false;
            if (!string.IsNullOrWhiteSpace(_options.Range))
            {
                useAdded = true;
                var ran = Git("diff", "--name-only", _options.Range);
                if (ran.Exit != 0)
                {
                    _host.Error.WriteLine("git diff failed: " + ran.Stderr.Trim());
                    if (_json)
                    {
                        ExitJson(new ImpactedRecord().Add("ok", false).Add("kinds", Empty).Add("classes", Empty).Add("files", Empty).Add("treenodeFilter", "")
                            .Add("filters", Empty).Add("error", ran.Stderr.Trim()), 2);
                    }
                    throw new ImpactedClassesStop(2);
                }
                changed.AddRange(SplitLines(ran.Stdout).Select(line => line.Replace('\\', '/')));
            }
            else if (_options.MergeSubset && string.IsNullOrWhiteSpace(_options.FileList) && (_options.Paths == null || _options.Paths.Count == 0))
            {
                // The fixed subset only. A batch diff adds every row it selects.
            }
            else if (!string.IsNullOrWhiteSpace(_options.FileList) || (_options.Paths != null && _options.Paths.Count > 0))
            {
                if (!string.IsNullOrWhiteSpace(_options.FileList))
                {
                    var list = Full(_options.FileList, cwd);
                    if (!File.Exists(list) && !Directory.Exists(list))
                    {
                        _host.Error.WriteLine("file list was not found: " + _options.FileList);
                        throw new ImpactedClassesStop(2);
                    }
                    changed.AddRange(SplitLines(File.ReadAllText(list)).Select(line => line.Replace('\\', '/')));
                }
                if (_options.Paths != null)
                    changed.AddRange(_options.Paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Replace('\\', '/')));
            }
            else
            {
                _host.Error.WriteLine("usage: antiphon-scripts " + ImpactedClassesOptions.Usage);
                throw new ImpactedClassesStop(2);
            }

            var matched = new List<ImpactedKind>();
            foreach (var kind in parsed.Kinds)
            {
                var pathHit = changed.Any(path => kind.Regexes.Any(regex => regex.IsMatch(path)));
                var literalHit = false;
                if (useAdded && kind.AddedRegex != null && kind.AddedLiterals.Count > 0 && changed.Any(path => kind.AddedRegex.IsMatch(path)))
                    literalHit = AddedText().Split('\n').Any(line => LiteralLine(line, kind));
                if (pathHit || literalHit) matched.Add(kind);
            }

            var derived = ResolveDerived(changed);

            // The Windows lane: required and only both land here; only is left out of the Linux selection.
            var windowsWhole = new HashSet<string>(StringComparer.Ordinal);
            var windowsMethods = MethodMap();
            var kindIds = new List<string>();
            foreach (var kind in matched)
            {
                if (!string.IsNullOrWhiteSpace(kind.Id)) kindIds.Add(kind.Id);
                if (kind.Windows.Length > 0) AddKindSelection(kind, windowsWhole, windowsMethods, null);
            }
            var kinds = Sorted(kindIds);

            if (_options.MergeSubset) return Merge(matched, kinds, windowsWhole, windowsMethods);

            var whole = new HashSet<string>(StringComparer.Ordinal);
            var methodsByClass = MethodMap();
            var fileSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kind in matched)
            {
                if (kind.Windows == "only") continue;
                AddKindSelection(kind, whole, methodsByClass, fileSet);
            }
            // A hand method row stays: a derived whole class does not erase it.
            foreach (var className in derived.Whole)
                if (!methodsByClass.ContainsKey(className)) whole.Add(className);
            foreach (var className in derived.Windows) windowsWhole.Add(className);
            foreach (var method in derived.Methods) AddMethodEntry(method, methodsByClass, null);
            // A hand row can name a class the index has since classified windowsOnly: the Linux filter drops it.
            var index = Index();
            foreach (var className in whole.ToList())
            {
                if (index.LinuxOnly.Contains(className)) continue;
                if (index.WindowsOnly.Contains(className))
                {
                    whole.Remove(className);
                    windowsWhole.Add(className);
                }
            }

            var rows = FilterRows(whole, methodsByClass, "", null);
            var windowsRows = FilterRows(windowsWhole, windowsMethods, "", null);
            var top = TopFilter(rows);
            var classes = ClassNames(whole, methodsByClass);
            var files = Sorted(fileSet);
            var payload = new ImpactedRecord()
                .Add("kinds", kinds).Add("classes", classes).Add("files", files).Add("derivedClasses", derived.Derived)
                .Add("keywordIds", Empty).Add("rosterIds", Empty).Add("skippedByCap", derived.Skipped)
                .Add("owed", derived.Owed.Select(row => new ImpactedRecord().Add("path", row.Path).Add("count", row.Count).Add("first", row.First)).ToList())
                .Add("treenodeFilter", top).Add("filters", Rows(rows, false)).Add("windowsFilters", Rows(windowsRows, false));
            if (_json) ExitJson(payload, 0);
            _host.Out.WriteLine("kinds=" + string.Join(",", kinds));
            _host.Out.WriteLine("treenodeFilter=" + top);
            foreach (var row in windowsRows) _host.Out.WriteLine("windowsFilter=" + row.Project + " " + row.Filter);
            foreach (var row in derived.Owed) _host.Out.WriteLine("owed/capped " + row.Path + " count=" + row.Count.ToString(CultureInfo.InvariantCulture) + " first=" + string.Join(",", row.First));
            foreach (var relative in files) _host.Out.WriteLine(relative);
            return 0;
        }

        // ---- release evidence (CARD-1820 S1R1) -------------------------------------------------------------------------

        private int ReleaseEvidence()
        {
            foreach (var (given, flag) in new[]
                     {
                         (_options.Range != null, "--range"), (_options.Paths != null, "--path"), (_options.FileList != null, "--file-list"),
                         (_options.MergeSubset, "--merge-subset"), (_options.RepositoryInvariants, "--repository-invariants"), (_options.Audit, "--audit"), (_options.Table != null, "--table"),
                     })
                if (given) RefuseRelease("--release-evidence cannot be combined with " + flag);
            if (!_json) RefuseRelease("--release-evidence requires --json");
            if (string.IsNullOrWhiteSpace(_options.BaselineSha) || string.IsNullOrWhiteSpace(_options.CandidateSha) || string.IsNullOrWhiteSpace(_options.TargetPlatform))
                RefuseRelease("--baseline-sha, --candidate-sha and --target-platform are required");
            string text;
            try
            {
                var sourceRoot = string.IsNullOrWhiteSpace(_options.SourceRoot) ? _repo : _options.SourceRoot;
                text = ReleaseImpactEvidence.Json(ReleaseImpactEvidence.Envelope(_repo, _options.BaselineSha, _options.CandidateSha, _options.TargetPlatform, sourceRoot));
            }
            catch (ImpactedClassesStop)
            {
                throw;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                RefuseRelease(e.Message);
                throw;
            }
            _host.Out.WriteLine(text);
            return 0;
        }

        private void RefuseRelease(string problem)
        {
            var message = "release-impact refused: " + problem;
            _host.Error.WriteLine(message);
            if (_json)
            {
                ExitJson(new ImpactedRecord().Add("ok", false).Add("complete", false).Add("incompleteReasons", new[] { "invalid-release-request" })
                    .Add("fallback", "Full").Add("error", message), 2);
            }
            throw new ImpactedClassesStop(2);
        }

        // ---- audit -----------------------------------------------------------------------------------------------------

        private int Audit(ImpactedTable table)
        {
            var empty = new List<string>();
            var hits = new List<(string Glob, int Count)>();
            var seenGlobs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var glob in table.AllGlobs)
            {
                if (!seenGlobs.Add(glob)) continue;
                var regex = ImpactedGlob.Regex(glob);
                var count = Tracked().Count(path => regex.IsMatch(path));
                hits.Add((glob, count));
                if (count < 1) empty.Add(glob);
            }
            var missing = new List<string>();
            var seenClasses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kind in table.Kinds)
            {
                foreach (var className in kind.Classes.Concat(kind.Methods.Select(m => m.IndexOf('/') > 0 ? m.Substring(0, m.IndexOf('/')) : m)))
                {
                    if (!seenClasses.Add(className)) continue;
                    if (!_census.FilesByClass.ContainsKey(className)) missing.Add(className);
                }
            }
            var emptySorted = Sorted(empty);
            var missingSorted = Sorted(missing);
            var kindSorted = Sorted(table.Kinds.Select(kind => kind.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
            var ok = emptySorted.Count == 0 && missingSorted.Count == 0;
            // Sort-Object -Property glob: the current culture, ignoring case.
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var globHits = hits.OrderBy(hit => hit.Glob, Comparer<string>.Create((x, y) => compare.Compare(x, y, CompareOptions.IgnoreCase)))
                .Select(hit => new ImpactedRecord().Add("glob", hit.Glob).Add("count", hit.Count)).ToList();
            var payload = new ImpactedRecord().Add("ok", ok).Add("emptyGlobs", emptySorted).Add("missingClasses", missingSorted).Add("kinds", kindSorted)
                .Add("globHits", globHits).Add("derivedFiles", Sorted(_census.DerivedFiles));
            var code = ok ? 0 : 1;
            if (!ok) _host.Error.WriteLine("impacted-classes audit failed. emptyGlobs=" + string.Join(",", emptySorted) + " missingClasses=" + string.Join(",", missingSorted));
            if (_json) ExitJson(payload, code);
            _host.Out.WriteLine("ok=" + (ok ? "True" : "False"));
            _host.Out.WriteLine("emptyGlobs=" + string.Join(",", emptySorted));
            _host.Out.WriteLine("missingClasses=" + string.Join(",", missingSorted));
            return code;
        }

        private List<string> Tracked()
        {
            if (_tracked != null) return _tracked;
            var ran = Git("ls-files", "-c");
            if (ran.Exit != 0)
            {
                _host.Error.WriteLine("git ls-files failed: " + ran.Stderr.Trim());
                ExitJson(RefusalRecord(ran.Stderr.Trim()), 2);
            }
            return _tracked = SplitLines(ran.Stdout);
        }

        // ---- the merge subset ------------------------------------------------------------------------------------------

        private (List<ImpactedRow> Portable, List<ImpactedRow> Windows, HashSet<(string Project, string Class)> Covered) RepositoryInvariantRows()
        {
            var portable = new List<ImpactedRow>();
            var windows = new List<ImpactedRow>();
            var covered = new HashSet<(string Project, string Class)>();
            var declarations = _census.InvariantDeclarations;
            if (declarations.Count == 0) return (portable, windows, covered);
            var policy = NightlyPolicy.Read(_repo, null).Object;
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (var suite in policy.Suites.Values.Where(s => !string.IsNullOrWhiteSpace(s.Project)).OrderBy(s => s.Project, StringComparer.Ordinal))
            {
                var project = suite.Project.Replace('\\', '/');
                var projectDirectory = Path.GetDirectoryName(project).Replace('\\', '/');
                var projectPath = Path.Combine(_repo, project);
                var includes = XDocument.Load(projectPath).Descendants().Where(x => x.Name.LocalName == "Compile")
                    .Select(x => (string)x.Attribute("Include")).Where(path => !string.IsNullOrEmpty(path))
                    .Select(path => path.Replace('\\', '/')).Where(path => !path.Contains('*') && !path.Contains('$'))
                    .Select(path => Path.GetRelativePath(_repo, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath), path)))
                        .Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
                var declarationsInProject = declarations.Where(d => d.File.StartsWith(projectDirectory + "/", StringComparison.Ordinal)
                    || includes.Contains(d.File)).ToArray();
                if (declarationsInProject.Length == 0) continue;
                if (!NightlyPolicy.ProfileSuiteRunnable(policy, "rc", suite.Id))
                    throw new NightlyRefusal("repository-invariant-manual-owner:" + project);
                var linux = NightlyPolicy.ProfileExclusions(policy, "rc", suite.Id, "linux");
                var win = NightlyPolicy.ProfileExclusions(policy, "rc", suite.Id, "windows");
                var portableForProject = new List<ImpactedRow>();
                var categoryAdmitsAll = true;
                foreach (var type in declarationsInProject)
                {
                    owners.Add(type.File);
                    if (!covered.Add((projectDirectory, type.Class)))
                        throw new NightlyRefusal("repository-invariant-ambiguous-short-name:" + project + ":" + type.Class);
                    var id = ImpactedProjects.Slug(projectDirectory) + "-" + type.Class;
                    foreach (var method in type.Methods)
                    {
                        var filter = "/*/" + type.Namespace + "/" + type.Class + "/" + method.Name;
                        var excludedOnLinux = Excluded(linux, type, method.Name);
                        var excludedOnWindows = Excluded(win, type, method.Name);
                        if (method.OptIn && !(excludedOnLinux && excludedOnWindows))
                            throw new NightlyRefusal("repository-invariant-live-method:" + project + ":" + type.Class + "." + method.Name);
                        if (!excludedOnLinux) portableForProject.Add(new ImpactedRow("CP-invariants-" + id + "-" + method.Name, projectDirectory, filter));
                        else
                        {
                            categoryAdmitsAll = false;
                            if (!excludedOnWindows) windows.Add(new ImpactedRow("CP-invariants-windows-" + id + "-" + method.Name, projectDirectory, filter));
                        }
                    }
                }
                if (categoryAdmitsAll && portableForProject.Count > 0)
                    portable.Add(new ImpactedRow("CP-invariants-" + ImpactedProjects.Slug(projectDirectory),
                        projectDirectory, NightlyTestsPlan.RepositoryInvariantFilter));
                else
                    portable.AddRange(portableForProject);
            }
            var missingOwner = declarations.FirstOrDefault(d => !owners.Contains(d.File));
            if (missingOwner != null) throw new NightlyRefusal("repository-invariant-project-unresolved:" + missingOwner.File);
            return (portable, windows, covered);

            static bool Excluded(IEnumerable<ExclusionRow> rows, ImpactedInvariantDeclaration type, string method) =>
                rows.Any(row => (string.Equals(row.Class, type.Class, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Class, type.Namespace + "." + type.Class, StringComparison.OrdinalIgnoreCase))
                    && (row.Methods.Length == 0 || row.Methods.Contains(method, StringComparer.OrdinalIgnoreCase)));
        }

        private int Merge(List<ImpactedKind> matched, List<string> kinds, HashSet<string> windowsWhole, Dictionary<string, List<string>> windowsMethods)
        {
            var fixedClassNames = _options.RepositoryInvariants ? Enumerable.Empty<string>() : ImpactedClasses.FixedClasses.Concat(ImpactedClasses.FixedMethods.Select(m => m.Substring(0, m.IndexOf('/'))));
            foreach (var fixedName in fixedClassNames)
            {
                if (_censusNames.Contains(fixedName)) continue;
                var message = "merge-subset refused: class '" + fixedName + "' was not found";
                _host.Error.WriteLine(message);
                if (_json) ExitJson(new ImpactedRecord().Add("ok", false).Add("mode", "merge-subset").Add("migrations", false).Add("error", message), 1);
                throw new ImpactedClassesStop(1);
            }
            var wholeSubset = new HashSet<string>(ImpactedClasses.FixedClasses, StringComparer.Ordinal);
            var subsetMethods = MethodMap();
            foreach (var method in ImpactedClasses.FixedMethods) AddMethodEntry(method, subsetMethods, null);
            var migrations = false;
            foreach (var kind in matched)
            {
                if (ImpactedProjects.PsEq(kind.Id, "migrations")) migrations = true;
                if (kind.Windows == "only") continue;
                AddKindSelection(kind, wholeSubset, subsetMethods, null);
            }
            var (portable, windows, covered) = RepositoryInvariantRows();
            var subsetRows = portable;
            var windowsRows = windows;
            if (!_options.RepositoryInvariants)
            {
                subsetRows.AddRange(FilterRows(wholeSubset, subsetMethods, "CP-merge-subset-", ImpactedClasses.FixedClasses,
                    (project, testClass) => !covered.Contains((project, testClass))));
                // A portable run cannot discharge a diff-driven native Windows obligation.
                windowsRows.AddRange(FilterRows(windowsWhole, windowsMethods, "CP-merge-windows-", ImpactedClasses.FixedClasses));
            }
            var top = TopFilter(subsetRows);
            var fixedOut = Sorted(ImpactedClasses.FixedClasses);
            var fixedMethodOut = Sorted(ImpactedClasses.FixedMethods);
            var classes = ClassNames(wholeSubset, subsetMethods);
            var payload = new ImpactedRecord().Add("ok", true).Add("mode", "merge-subset").Add("migrations", migrations).Add("kinds", kinds)
                .Add("fixedClasses", fixedOut).Add("fixedMethods", fixedMethodOut).Add("classes", classes).Add("treenodeFilter", top)
                .Add("filters", Rows(subsetRows, true)).Add("windowsFilters", Rows(windowsRows, true));
            if (_json) ExitJson(payload, 0);
            _host.Out.WriteLine("mode=merge-subset");
            _host.Out.WriteLine("migrations=" + (migrations ? "True" : "False"));
            _host.Out.WriteLine("kinds=" + string.Join(",", kinds));
            _host.Out.WriteLine("fixedClasses=" + string.Join(",", fixedOut));
            _host.Out.WriteLine("fixedMethods=" + string.Join(",", fixedMethodOut));
            _host.Out.WriteLine("classes=" + string.Join(",", classes));
            _host.Out.WriteLine("treenodeFilter=" + top);
            foreach (var row in subsetRows) _host.Out.WriteLine("filter=" + row.Project + " " + row.Filter);
            foreach (var row in windowsRows) _host.Out.WriteLine("windowsFilter=" + row.Project + " " + row.Filter);
            return 0;
        }

        // ---- the hand rows ---------------------------------------------------------------------------------------------

        // Class name to its method names; names are compared ignoring case, as the script's hashtable did.
        private static Dictionary<string, List<string>> MethodMap() => new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private void AddClassFile(string className, HashSet<string> fileSet)
        {
            if (fileSet == null || !_census.FilesByClass.TryGetValue(className, out var files)) return;
            foreach (var relative in files) fileSet.Add(relative);
        }

        private void AddMethodEntry(string method, Dictionary<string, List<string>> methodsByClass, HashSet<string> fileSet)
        {
            var slash = method.IndexOf('/');
            if (slash <= 0 || slash >= method.Length - 1) return;
            var className = method.Substring(0, slash);
            if (!methodsByClass.TryGetValue(className, out var names)) methodsByClass[className] = names = new List<string>();
            names.Add(method.Substring(slash + 1));
            AddClassFile(className, fileSet);
        }

        // One kind's whole classes, derived downgrade callers and method entries.
        private void AddKindSelection(ImpactedKind kind, HashSet<string> whole, Dictionary<string, List<string>> methodsByClass, HashSet<string> fileSet)
        {
            foreach (var className in kind.Classes)
            {
                if (className.EndsWith("Tests", StringComparison.Ordinal)) whole.Add(className);
                AddClassFile(className, fileSet);
            }
            if (kind.HasDerive)
            {
                if (fileSet != null) foreach (var relative in _census.DerivedFiles) fileSet.Add(relative);
                foreach (var pair in _census.FilesByClass)
                    if (pair.Value.Any(_census.DerivedFiles.Contains) && pair.Key.EndsWith("Tests", StringComparison.Ordinal)) whole.Add(pair.Key);
            }
            foreach (var method in kind.Methods) AddMethodEntry(method, methodsByClass, fileSet);
        }

        private static bool LiteralLine(string line, ImpactedKind kind)
        {
            var isAdded = line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal);
            var isRemoved = kind.AddedRemoved && line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal);
            if (!isAdded && !isRemoved) return false;
            return kind.AddedLiterals.Any(literal => line.Contains(literal, StringComparison.Ordinal));
        }

        private string AddedText()
        {
            if (_addedText != null) return _addedText;
            var ran = Git("diff", "-U0", "--no-color", _options.Range, "--", "tests/");
            if (ran.Exit != 0)
            {
                _host.Error.WriteLine("git diff failed: " + ran.Stderr.Trim());
                throw new ImpactedClassesStop(2);
            }
            return _addedText = ran.Stdout;
        }

        // ---- the projects and filter rows ------------------------------------------------------------------------------

        private List<string> ProjectDirectories()
        {
            var catalogPath = Path.Combine(_repo, "tests", "test-selection-policy.json");
            if (!File.Exists(catalogPath) && !Directory.Exists(catalogPath)) return ImpactedProjects.Directories(null);
            try
            {
                return ImpactedProjects.Directories(File.ReadAllText(catalogPath));
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                var message = "impacted-classes catalog refused: tests/test-selection-policy.json: " + e.Message;
                _host.Error.WriteLine(message);
                if (_json) ExitJson(RefusalRecord(message), 1);
                throw new ImpactedClassesStop(1);
            }
        }

        // CARD-1843. A class name declared in several projects (ProcessSpawnLimitTests) runs in each of them.
        private List<string> ClassProjects(string className)
        {
            var projects = _census.FilesByClass.TryGetValue(className, out var files)
                ? Sorted(files.Select(relative => ImpactedProjects.Of(_projectDirs, relative)))
                : new List<string>();
            if (projects.Count == 0) projects.Add(ImpactedProjects.Default);
            return projects;
        }

        // Class rows first, one per project in ordinal order, then one method row per class (a whole class wins over a
        // method row of the same class). Under --merge-subset (firstLand set) a project row naming more than four land
        // classes (a class name containing "Land", case-sensitive, as the checkpoint importer counts) is split: its land
        // classes go four at a time, in ordinal order, to <id>-land-<n> rows, the fixed ones (firstLand) first so a batch
        // class never shifts a fixed row; the others stay in the project row, which is left out when none remain. Without
        // it (the Code or Review output) each project keeps its whole selection in one row (CARD-1801 R2-D-1).
        private List<ImpactedRow> FilterRows(HashSet<string> whole, Dictionary<string, List<string>> methodsByClass, string idPrefix, IReadOnlyList<string> firstLand,
            Func<string, string, bool> included = null)
        {
            var rows = new List<ImpactedRow>();
            var byProject = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var className in whole)
            {
                foreach (var project in ClassProjects(className))
                {
                    if (included != null && !included(project, className)) continue;
                    if (!byProject.TryGetValue(project, out var names)) byProject[project] = names = new List<string>();
                    names.Add(className);
                }
            }
            foreach (var project in Sorted(byProject.Keys))
            {
                var projectId = idPrefix + ImpactedProjects.Slug(project);
                var sortedNames = Sorted(byProject[project]);
                var first = new List<string>();
                var later = new List<string>();
                var other = new List<string>();
                foreach (var className in sortedNames)
                {
                    if (!className.Contains("Land", StringComparison.Ordinal)) other.Add(className);
                    else if (firstLand != null && firstLand.Contains(className, StringComparer.Ordinal)) first.Add(className);
                    else later.Add(className);
                }
                if (firstLand == null || first.Count + later.Count <= 4)
                {
                    rows.Add(new ImpactedRow(projectId, project, ClassFilter(sortedNames)));
                    continue;
                }
                if (other.Count > 0) rows.Add(new ImpactedRow(projectId, project, ClassFilter(other)));
                var landRow = 0;
                foreach (var group in new[] { first, later })
                {
                    for (var at = 0; at < group.Count; at += 4)
                    {
                        landRow++;
                        rows.Add(new ImpactedRow(projectId + "-land-" + landRow.ToString(CultureInfo.InvariantCulture), project, ClassFilter(group.Skip(at).Take(4))));
                    }
                }
            }
            var methodRows = new List<ImpactedRow>();
            foreach (var className in Sorted(methodsByClass.Keys))
            {
                if (whole.Contains(className)) continue;
                var methodProjects = ClassProjects(className);
                foreach (var project in methodProjects)
                {
                    if (included != null && !included(project, className)) continue;
                    var methodId = idPrefix + className;
                    if (methodProjects.Count > 1) methodId += "-" + ImpactedProjects.Slug(project);
                    methodRows.Add(new ImpactedRow(methodId, project, MethodFilter(className, methodsByClass[className])));
                }
            }
            // Sort-Object Project, Filter: the current culture, ignoring case.
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var culture = Comparer<string>.Create((x, y) => compare.Compare(x, y, CompareOptions.IgnoreCase));
            rows.AddRange(methodRows.OrderBy(row => row.Project, culture).ThenBy(row => row.Filter, culture));
            return rows;
        }

        private static string ClassFilter(IEnumerable<string> names)
        {
            var parts = Sorted(names).Select(name => "(" + name + "*)").ToList();
            return parts.Count == 0 ? "" : "/*/*/" + string.Join("|", parts) + "/*";
        }

        private static string MethodFilter(string className, IEnumerable<string> methods)
        {
            var parts = Sorted(methods).Select(name => "(" + name + "*)").ToList();
            return parts.Count == 0 ? "" : "/*/*/" + className + "/" + string.Join("|", parts);
        }

        private static List<string> ClassNames(HashSet<string> whole, Dictionary<string, List<string>> methodsByClass) =>
            Sorted(whole.Concat(methodsByClass.Keys.Where(className => !whole.Contains(className))));

        // The tests/Antiphon.Tests class row, else its first row, else the first row.
        private static string TopFilter(List<ImpactedRow> rows)
        {
            foreach (var row in rows)
                if (ImpactedProjects.PsEq(row.Project, ImpactedProjects.Default) && row.Filter.StartsWith("/*/*/(", StringComparison.Ordinal)) return row.Filter;
            foreach (var row in rows)
                if (ImpactedProjects.PsEq(row.Project, ImpactedProjects.Default)) return row.Filter;
            return rows.Count > 0 ? rows[0].Filter : "";
        }

        private static List<ImpactedRecord> Rows(List<ImpactedRow> rows, bool withId) =>
            rows.Select(row => withId
                ? new ImpactedRecord().Add("id", row.Id).Add("project", row.Project).Add("treenodeFilter", row.Filter)
                : new ImpactedRecord().Add("project", row.Project).Add("treenodeFilter", row.Filter)).ToList();

        // ---- the derived selection (CARD-1820) -------------------------------------------------------------------------

        private ImpactedDerivedIndex Index()
        {
            if (_index != null) return _index;
            var clock = Stopwatch.StartNew();
            var path = Path.Combine(_repo, "tests", "derived-impact-index.json");
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                SetTiming("indexHydrationMs", clock.Elapsed.TotalMilliseconds);
                return _index = new ImpactedDerivedIndex();
            }
            try
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { throw new ImpactedIndexRefusal(e.Message); }
                var jsonClock = Stopwatch.StartNew();
                if (string.IsNullOrWhiteSpace(text)) throw new ImpactedIndexRefusal("expected an object");
                _index = ImpactedDerivedIndex.Parse(text);
                AddTiming("jsonInputMs", jsonClock.Elapsed.TotalMilliseconds);
            }
            catch (ImpactedIndexRefusal refusal)
            {
                var message = "derived-impact index refused: " + refusal.Message;
                _host.Error.WriteLine(message);
                if (_json)
                {
                    ExitJson(new ImpactedRecord().Add("ok", false).Add("error", message).Add("derivedClasses", Empty).Add("keywordIds", Empty)
                        .Add("rosterIds", Empty).Add("skippedByCap", Empty), 1);
                }
                throw new ImpactedClassesStop(1);
            }
            SetTiming("indexHydrationMs", clock.Elapsed.TotalMilliseconds);
            return _index;
        }

        // Only the land-graph audit tool or its scripts changed: LandGraphEvaluationTests runs its budget method alone.
        private static bool LandGraphToolOnly(IEnumerable<string> paths)
        {
            var any = false;
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                any = true;
                var norm = path.Replace('\\', '/');
                if (!norm.StartsWith("tools/Antiphon.LandGraphAudit.Core/", StringComparison.Ordinal) && !AuditScript.IsMatch(norm)) return false;
            }
            return any;
        }

        // LinuxOnly wins a double label (the declaration witness forbids that pair).
        private static void AddDerivedClass(ImpactedDerivedIndex index, string className, HashSet<string> whole, HashSet<string> windows)
        {
            if (index.LinuxOnly.Contains(className)) whole.Add(className);
            else if (index.WindowsOnly.Contains(className)) windows.Add(className);
            else whole.Add(className);
        }

        // A changed tests file selects the classes it declares; the index adds each path's reverse edges. A capped path
        // keeps its most specific classes and owes the rest; an allowlisted path derives nothing.
        private ImpactedDerived ResolveDerived(List<string> paths)
        {
            var result = new ImpactedDerived();
            var skipped = new HashSet<string>(StringComparer.Ordinal);
            var index = Index();
            var toolOnly = LandGraphToolOnly(paths);
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var norm = path.Replace('\\', '/');
                var isTestFile = norm.StartsWith("tests/", StringComparison.Ordinal) && norm.EndsWith(".cs", StringComparison.Ordinal);
                if (isTestFile && _census.ClassesByFile.TryGetValue(norm, out var declared))
                    foreach (var className in declared)
                        if (className.EndsWith("Tests", StringComparison.Ordinal)) AddDerivedClass(index, className, result.Whole, result.Windows);
                index.Capped.TryGetValue(norm, out var cappedRow);
                var allow = index.Allow.Contains(norm) || (cappedRow != null && ImpactedProjects.PsEq(cappedRow.Reason, "allowlist"));
                if (allow) skipped.Add(norm);
                if (cappedRow != null && !allow && cappedRow.Owed > 0)
                    result.Owed.Add(new ImpactedOwed(norm, cappedRow.Owed, cappedRow.First.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()));
                if (allow || !index.Edges.TryGetValue(norm, out var edge)) continue;
                foreach (var className in edge)
                {
                    if (!_censusNames.Contains(className)) continue;
                    var methodOnly = toolOnly && ImpactedProjects.PsEq(className, "LandGraphEvaluationTests") && !result.Whole.Contains(className) && !result.Windows.Contains(className);
                    if (methodOnly) result.Methods.Add("LandGraphEvaluationTests/EvaluationBudgetRefusesSlowDotnet");
                    else AddDerivedClass(index, className, result.Whole, result.Windows);
                }
            }
            foreach (var className in result.Windows) result.Whole.Remove(className);
            var derived = Sorted(result.Whole.Concat(result.Windows));
            foreach (var method in result.Methods)
            {
                var slash = method.IndexOf('/');
                if (slash <= 0) continue;
                var className = method.Substring(0, slash);
                if (!result.Whole.Contains(className) && !result.Windows.Contains(className)) derived.Add(className);
            }
            result.Derived = result.Methods.Count > 0 ? Sorted(derived) : derived;
            result.Skipped = Sorted(skipped);
            // Sort-Object Path: the current culture, ignoring case.
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            result.Owed = result.Owed.OrderBy(row => row.Path, Comparer<string>.Create((x, y) => compare.Compare(x, y, CompareOptions.IgnoreCase))).ToList();
            return result;
        }

        // ---- output, git and helpers -----------------------------------------------------------------------------------

        private static readonly string[] Empty = Array.Empty<string>();

        private static ImpactedRecord RefusalRecord(string error) =>
            new ImpactedRecord().Add("ok", false).Add("emptyGlobs", Empty).Add("missingClasses", Empty).Add("kinds", Empty).Add("globHits", Empty)
                .Add("derivedFiles", Empty).Add("error", error);

        private ImpactedClassesStop RefuseTable(string where, string problem)
        {
            var message = "impacted-classes table refused: " + where + ": " + problem;
            _host.Error.WriteLine(message);
            if (_json) ExitJson(RefusalRecord(message), 1);
            return new ImpactedClassesStop(1);
        }

        // Exit-Json: the payload on stdout under --json, the timing line, then the exit.
        private void ExitJson(ImpactedRecord payload, int code)
        {
            if (_json)
            {
                var clock = Stopwatch.StartNew();
                var text = ImpactedJson.Text(payload);
                AddTiming("jsonOutputMs", clock.Elapsed.TotalMilliseconds);
                _host.Out.WriteLine(text);
            }
            WriteTiming();
            throw new ImpactedClassesStop(code);
        }

        // Opt-in diagnostics on stderr; stdout keeps the selection contract.
        private void WriteTiming()
        {
            if (_host.Variables("ANTIPHON_SELECTOR_TIMING") != "1") return;
            SetTiming("scriptMs", _clock.Elapsed.TotalMilliseconds);
            var record = new ImpactedRecord();
            foreach (var pair in _timing) record.Add(pair.Key, pair.Value);
            _host.Error.WriteLine("SELECTOR_TIMING " + ImpactedJson.Text(record, indented: false));
        }

        private void SetTiming(string name, double value)
        {
            var at = _timing.FindIndex(pair => pair.Key == name);
            if (at < 0) _timing.Add(new KeyValuePair<string, double>(name, value));
            else _timing[at] = new KeyValuePair<string, double>(name, value);
        }

        private void AddTiming(string name, double value)
        {
            var at = _timing.FindIndex(pair => pair.Key == name);
            SetTiming(name, (at < 0 ? 0 : _timing[at].Value) + value);
        }

        // Invoke-Git: git -c core.quotepath=false with the arguments in the repository, the inherited environment, both
        // streams read as UTF-8, no timeout.
        private (int Exit, string Stdout, string Stderr) Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("core.quotepath=false");
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var clock = Stopwatch.StartNew();
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            AddTiming("gitMs", clock.Elapsed.TotalMilliseconds);
            return (process.ExitCode, stdout, stderr.GetAwaiter().GetResult());
        }

        // Split-Lines: the non-blank lines, trimmed.
        private static List<string> SplitLines(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var line in LineBreak.Split(text))
                if (!string.IsNullOrWhiteSpace(line)) list.Add(line.Trim());
            return list;
        }

        // Fill-Sorted: the non-blank names, ordinal order, without repeats.
        private static List<string> Sorted(IEnumerable<string> items) =>
            new SortedSet<string>((items ?? Empty).Where(item => !string.IsNullOrWhiteSpace(item)), StringComparer.Ordinal).ToList();

        private static string Full(string path, string currentDirectory) =>
            currentDirectory == null || Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, Path.GetFullPath(currentDirectory));
    }
}
