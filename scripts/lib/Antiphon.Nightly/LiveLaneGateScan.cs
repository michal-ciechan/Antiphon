// The live-lane gate scan of live-lane-manifest.ps1 (CARD-1545 S1a, V-3), compiled in W2-nightly S23. A gate is a
// test in tests/Antiphon.Tests or tests/Antiphon.Agents.Pty.Tests that is kept off the ordinary rosters: an
// [Explicit] class or method, or a body that reads ANTIPHON_HEADED_TESTS, calls SkipIfNotEligible or uses
// RealCliStub. The catalog in tests/live-lanes.json gives every gate a disposition (core, policy-excluded or
// diagnostic), an owner and a reason; Matrix checks it (LiveLaneInventoryTests.EveryLiveGateHasDisposition) and
// WriteCatalog rewrites it (antiphon-scripts live-lane-scan-gates).
//
// The file walk, case rules and catalog layout follow the PowerShell implementation:
// - The file walk is Get-ChildItem -Recurse -File -Filter *.cs (PowerShell 7): a directory's files, then each
//   subdirectory in turn, in the file system's order, hidden entries left out and links not followed; a path with a
//   bin, obj or bin-* part (ignoring case) is left out.
// - PowerShell's -match, -eq and @{} keys ignore case and [regex] does not; $s.IndexOf('{', i) is the current
//   culture's IndexOf; Sort-Object is List.Sort with the current culture, ignoring case.
// - The catalog is written as ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 wrote it: two-space indents, the
//   platform's line ends, Newtonsoft's default escaping, a final line end, no byte-order mark (LiveLaneJson.cs, shared
//   with the discovery, roster and export files since W2-nightly S24).
// One deliberate difference: tests/live-lanes.json is read with System.Text.Json, so the core and additiveContract
// rows the catalog carries over are written back as they were read (ConvertFrom-Json turned a date-like string into
// a date first), and a number other than an integer refuses. A non-text field value (a number, list or object where
// the catalog has text) reads as text that decides the same way as PowerShell's [string] of it. ASCII-only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A test identity: project, namespace, class, method and one [Arguments] row's values.</summary>
    public sealed class LiveLaneIdentity
    {
        public LiveLaneIdentity(string project, string @namespace, string @class, string method, params string[] arguments)
        {
            Project = project ?? "";
            Namespace = @namespace ?? "";
            Class = @class ?? "";
            Method = method ?? "";
            Arguments = arguments ?? new string[0];
        }

        public string Project { get; }
        public string Namespace { get; }
        public string Class { get; }
        public string Method { get; }
        public IReadOnlyList<string> Arguments { get; }

        /// <summary>Get-LiveLaneIdentityKey.</summary>
        public string Key => LiveLaneGateScan.IdentityKey(Project, Namespace, Class, Method, Arguments);
    }

    /// <summary>Get-LiveLaneContract: the six live-claude core identities.</summary>
    public static class LiveLaneContract
    {
        public static IReadOnlyList<LiveLaneIdentity> Core { get; } = new[]
        {
            new LiveLaneIdentity("Antiphon.Tests", "Antiphon.Tests.Agents", "ClaudeDialogWatchdogCanaryTests", "C680_LiveNudgeRefusesPermissionAndQuestionDialogs"),
            new LiveLaneIdentity("Antiphon.Agents.Pty.Tests", "Antiphon.Agents.Pty.Tests", "ClaudeSubmitContractLiveTests", "Submitting_via_two_writes_completes_a_turn", "claude"),
            new LiveLaneIdentity("Antiphon.Agents.Pty.Tests", "Antiphon.Agents.Pty.Tests", "ClaudeRemoteControlAtStartupCanaryTests", "Control_auto_connects_a_bridge_session_before_the_first_user"),
            new LiveLaneIdentity("Antiphon.Agents.Pty.Tests", "Antiphon.Agents.Pty.Tests", "ClaudeRemoteControlAtStartupCanaryTests", "Settings_file_prevents_auto_connect_and_keeps_startup_records"),
            new LiveLaneIdentity("Antiphon.Agents.Pty.Tests", "Antiphon.Agents.Pty.Tests", "ClaudeRemoteControlAtStartupCanaryTests", "Remote_control_slash_command_still_arms_after_settings_off"),
            new LiveLaneIdentity("Antiphon.Agents.Pty.Tests", "Antiphon.Agents.Pty.Tests", "ClaudeRemoteControlMenuCanaryTests", "C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt"),
        };
    }

    /// <summary>A gated test the scan found, one per [Arguments] row (none: one gate without arguments).</summary>
    public sealed class LiveLaneGate
    {
        public LiveLaneGate(string project, string @namespace, string @class, string method, string[] arguments, string[] shapes)
        {
            Project = project ?? "";
            Namespace = @namespace ?? "";
            Class = @class ?? "";
            Method = method ?? "";
            Arguments = arguments ?? new string[0];
            Shapes = shapes ?? new string[0];
        }

        public string Project { get; }
        public string Namespace { get; }
        public string Class { get; }
        public string Method { get; }
        public IReadOnlyList<string> Arguments { get; }

        /// <summary>Explicit, ANTIPHON_HEADED_TESTS, SkipIfNotEligible and RealCliStub, in that order, each at most once.</summary>
        public IReadOnlyList<string> Shapes { get; }

        /// <summary>Get-LiveLaneIdentityKey.</summary>
        public string Key => LiveLaneGateScan.IdentityKey(Project, Namespace, Class, Method, Arguments);
    }

    /// <summary>Invoke-LiveLaneGateScan's { Gates; Unresolved }, with the reason behind each unresolved count.</summary>
    public sealed class LiveLaneGateScanResult
    {
        internal LiveLaneGateScanResult(List<LiveLaneGate> gates, List<string> reasons)
        {
            Gates = gates;
            Reasons = reasons;
        }

        public IReadOnlyList<LiveLaneGate> Gates { get; }

        /// <summary>A class or test the scan could not read: an [Explicit] class without a body or tests, a span
        /// without its closing brace, or a [GateShape(...)] test.</summary>
        public int Unresolved => Reasons.Count;

        /// <summary>One "path TAB class [TAB method] TAB why" line per unresolved count, in scan order.</summary>
        public IReadOnlyList<string> Reasons { get; }
    }

    /// <summary>Get-LiveLaneGateDisposition's { Disposition; Owner; Reason }.</summary>
    public sealed class LiveLaneGateDisposition
    {
        internal LiveLaneGateDisposition(string disposition, string owner, string reason)
        {
            Disposition = disposition;
            Owner = owner;
            Reason = reason;
        }

        public string Disposition { get; }
        public string Owner { get; }
        public string Reason { get; }
    }

    /// <summary>Write-LiveLaneGateCatalog's { Count; Unresolved; Path }.</summary>
    public sealed class LiveLaneGateCatalog
    {
        internal LiveLaneGateCatalog(int count, int unresolved, string path)
        {
            Count = count;
            Unresolved = unresolved;
            Path = path;
        }

        public int Count { get; }
        public int Unresolved { get; }
        public string Path { get; }
    }

    public static class LiveLaneGateScan
    {
        // The scanned projects, in scan order.
        private static readonly string[] Projects = { "Antiphon.Tests", "Antiphon.Agents.Pty.Tests" };

        // A file is read only when its raw text names a shape (-notmatch: ignoring case).
        private static readonly Regex Candidate = new Regex("Explicit|ANTIPHON_HEADED_TESTS|SkipIfNotEligible|RealCliStub|GateShape", RegexOptions.IgnoreCase);
        private static readonly Regex NamespaceRx = new Regex(@"namespace\s+([A-Za-z0-9_\.]+)\s*[;{]");
        private static readonly Regex ClassRx = new Regex(@"(?<attrs>(?:\[[^\]]*\]\s*)*)(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|file\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)");
        private static readonly Regex MethodRx = new Regex(@"(?<attrs>(?:\[[^\]]*\]\s*)+)(?:public|internal)\s+(?:sealed\s+|static\s+|async\s+|override\s+|virtual\s+|new\s+|partial\s+|[\w\.]+\s+)*?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(");
        private static readonly Regex ExplicitAttr = new Regex(@"\[Explicit(\(|\]|,)", RegexOptions.IgnoreCase);
        private static readonly Regex TestAttr = new Regex(@"\[Test(\(|\]|,)", RegexOptions.IgnoreCase);
        private static readonly Regex GateShapeAttr = new Regex(@"\[GateShape\(", RegexOptions.IgnoreCase);
        // The source stripper removes ordinary string delimiters. Match the lookup's positional or named variable.
        private static readonly Regex HeadedRead = new Regex(
            @"\bEnvironment\s*\.\s*GetEnvironmentVariable\s*\(\s*(?:variable\s*:\s*)?ANTIPHON_HEADED_TESTS\s*[,)]",
            RegexOptions.IgnoreCase);
        private static readonly Regex SkipIf = new Regex("SkipIfNotEligible", RegexOptions.IgnoreCase);
        private static readonly Regex RealCli = new Regex("RealCliStub", RegexOptions.IgnoreCase);
        private static readonly Regex ArgumentsRx = new Regex(@"Arguments\(([^)]*)\)");
        private static readonly Regex Quoted = new Regex("\"([^\"]*)\"");
        private static readonly Regex TestsPath = new Regex(@"tests[/\\]Antiphon\.Tests[/\\]", RegexOptions.IgnoreCase);
        private static readonly Regex TestsName = new Regex(@"Antiphon\.Tests", RegexOptions.IgnoreCase);
        private static readonly Regex PtyName = new Regex(@"Antiphon\.Agents\.Pty\.Tests", RegexOptions.IgnoreCase);

        // Get-ChildItem's listing: Win32 wildcards ignoring case, nothing skipped by the enumerator (hidden entries are
        // left out below), an unreadable directory throws.
        private static readonly EnumerationOptions Listing = new EnumerationOptions
        {
            MatchType = MatchType.Win32,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
        };

        // The matrix's scratch sources (Invoke-LiveLaneGateMatrix), byte for byte.
        private const string NewGateSource = "namespace Scratch;\n[Explicit]\npublic class NewGate {\n  [Test]\n  public void Injected() { }\n}\n";
        private const string OrdinarySource = "namespace Scratch;\npublic class OrdinaryControl {\n  [Test]\n  public void Plain() { int x = 1; }\n}\n";
        private const string UnknownSource = "namespace Scratch;\npublic class Mystery {\n  [Test]\n  [GateShape(\"unresolved\")]\n  public void Odd() { }\n}\n";

        /// <summary>Get-LiveLaneIdentityKey: project|namespace|class|method|arguments joined by line feeds.</summary>
        public static string IdentityKey(string project, string @namespace, string @class, string method, IEnumerable<string> arguments) =>
            (project ?? "") + "|" + (@namespace ?? "") + "|" + (@class ?? "") + "|" + (method ?? "") + "|" + string.Join("\n", arguments ?? new string[0]);

        // ---- the scan ----------------------------------------------------------------------------------------

        /// <summary>
        /// Invoke-LiveLaneGateScan: every gate in the two test projects' sources, then in the extra files that exist
        /// (outside a project: project ""), and the unresolved count.
        /// </summary>
        public static LiveLaneGateScanResult Scan(string repoRoot, params string[] extraFiles) => ScanFiles(Files(repoRoot, extraFiles));

        private static LiveLaneGateScanResult ScanFiles(IEnumerable<string> files)
        {
            var gates = new List<LiveLaneGate>();
            var reasons = new List<string>();
            foreach (var path in files) ScanFile(path, gates, reasons);
            return new LiveLaneGateScanResult(gates, reasons);
        }

        // Full paths: each project's *.cs files, then the extra files (Test-Path: a file or a directory).
        private static List<string> Files(string repoRoot, IEnumerable<string> extraFiles)
        {
            var files = new List<string>();
            foreach (var project in Projects)
            {
                var root = Path.GetFullPath(Path.Combine(repoRoot, "tests", project));
                if (Directory.Exists(root)) Walk(new DirectoryInfo(root), files);
            }
            foreach (var extra in extraFiles ?? new string[0])
                if (!string.IsNullOrEmpty(extra) && (File.Exists(extra) || Directory.Exists(extra))) files.Add(Path.GetFullPath(extra));
            return files;
        }

        private static void Walk(DirectoryInfo directory, List<string> files)
        {
            foreach (var file in directory.EnumerateFiles("*.cs", Listing))
                if ((file.Attributes & FileAttributes.Hidden) == 0 && !BuildPart(file.Name)) files.Add(file.FullName);
            foreach (var sub in directory.EnumerateDirectories("*", Listing))
            {
                var attributes = sub.Attributes;
                if ((attributes & FileAttributes.Hidden) != 0 || BuildPart(sub.Name)) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0 && sub.LinkTarget != null) continue;
                Walk(sub, files);
            }
        }

        // A path part bin, obj or bin-*, ignoring case: build output, never scanned.
        private static bool BuildPart(string part) =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("bin-", StringComparison.OrdinalIgnoreCase);

        private static void ScanFile(string path, List<LiveLaneGate> gates, List<string> reasons)
        {
            var project = "";
            if (TestsPath.IsMatch(path) || TestsName.IsMatch(path)) project = PtyName.IsMatch(path) ? "Antiphon.Agents.Pty.Tests" : "Antiphon.Tests";
            if (PtyName.IsMatch(path)) project = "Antiphon.Agents.Pty.Tests";
            var raw = File.ReadAllText(path);
            if (!Candidate.IsMatch(raw)) return;
            var text = LiveLaneSource.Strip(raw);
            var ns = "";
            var nsMatch = NamespaceRx.Match(text);
            if (nsMatch.Success) ns = nsMatch.Groups[1].Value;
            var classes = ClassRx.Matches(text);
            for (var c = 0; c < classes.Count; c++)
            {
                var cm = classes[c];
                var className = cm.Groups["name"].Value;
                var classExplicit = ExplicitAttr.IsMatch(cm.Groups["attrs"].Value);
                var braceAt = Find(text, "{", cm.Index + cm.Length);
                if (braceAt < 0)
                {
                    if (classExplicit) reasons.Add(path + "\t" + className + "\tno-brace");
                    continue;
                }
                var span = BraceSpan(text, braceAt);
                if (span == null)
                {
                    reasons.Add(path + "\t" + className + "\tclass-span");
                    continue;
                }
                // A nested class is its own entry: its span is blanked out of this one.
                var local = span;
                foreach (Match other in classes)
                {
                    if (other.Index <= cm.Index) continue;
                    var otherBrace = Find(text, "{", other.Index + other.Length);
                    if (otherBrace < braceAt || otherBrace >= braceAt + span.Length) continue;
                    var otherSpan = BraceSpan(text, otherBrace);
                    if (otherSpan == null) continue;
                    var rel = otherBrace - braceAt;
                    if (rel >= 0 && rel + otherSpan.Length <= local.Length)
                        local = local.Remove(rel, otherSpan.Length).Insert(rel, new string(' ', otherSpan.Length));
                }
                var tests = new List<Match>();
                foreach (Match m in MethodRx.Matches(local))
                    if (TestAttr.IsMatch(m.Groups["attrs"].Value)) tests.Add(m);
                if (classExplicit && tests.Count == 0) reasons.Add(path + "\t" + className + "\tno-tests");
                foreach (var mm in tests)
                {
                    var attrs = mm.Groups["attrs"].Value;
                    var method = mm.Groups["name"].Value;
                    if (Same(method, "SkipIfNotEligible")) continue;
                    var sigEnd = mm.Index + mm.Length;
                    var open = Find(local, "{", sigEnd);
                    var arrow = Find(local, "=>", sigEnd);
                    var body = "";
                    if (arrow >= 0 && (open < 0 || arrow < open))
                    {
                        var semi = Find(local, ";", arrow);
                        if (semi < 0) semi = local.Length;
                        body = local.Substring(arrow, semi - arrow);
                    }
                    else if (open >= 0)
                    {
                        var bodySpan = BraceSpan(local, open);
                        if (bodySpan == null)
                        {
                            reasons.Add(path + "\t" + className + "\t" + method + "\tbody-span");
                            continue;
                        }
                        body = bodySpan;
                    }
                    var shapes = new List<string>();
                    if (classExplicit || ExplicitAttr.IsMatch(attrs)) shapes.Add("Explicit");
                    if (HeadedRead.IsMatch(body)) shapes.Add("ANTIPHON_HEADED_TESTS");
                    if (SkipIf.IsMatch(body) && !Same(method, "SkipIfNotEligible")) shapes.Add("SkipIfNotEligible");
                    if (RealCli.IsMatch(body)) shapes.Add("RealCliStub");
                    if (GateShapeAttr.IsMatch(attrs))
                    {
                        reasons.Add(path + "\t" + className + "\t" + method + "\tgate-shape");
                        continue;
                    }
                    if (shapes.Count == 0) continue;
                    var argMatches = ArgumentsRx.Matches(attrs);
                    if (argMatches.Count == 0)
                    {
                        gates.Add(new LiveLaneGate(project, ns, className, method, new string[0], shapes.ToArray()));
                        continue;
                    }
                    foreach (Match am in argMatches)
                        gates.Add(new LiveLaneGate(project, ns, className, method, QuotedArgs(am.Groups[1].Value), shapes.ToArray()));
                }
            }
        }

        // $text.IndexOf('{', $start): String.IndexOf(string, int) compares with the current culture.
        private static int Find(string text, string value, int start) => text.IndexOf(value, start, StringComparison.CurrentCulture);

        // Get-LiveLaneBraceSpan: the text from the brace at openAt through its matching close, or null.
        private static string BraceSpan(string text, int openAt)
        {
            var depth = 0;
            for (var i = openAt; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(openAt, i - openAt + 1);
                }
            }
            return null;
        }

        // Get-LiveLaneQuotedArgs: an [Arguments(...)] row's quoted values, else the whole row trimmed, else none.
        private static string[] QuotedArgs(string inner)
        {
            if (string.IsNullOrWhiteSpace(inner)) return new string[0];
            var matches = Quoted.Matches(inner);
            if (matches.Count == 0) return new[] { inner.Trim() };
            var values = new string[matches.Count];
            for (var i = 0; i < matches.Count; i++) values[i] = matches[i].Groups[1].Value;
            return values;
        }

        // PowerShell's -eq of two strings: the invariant culture, ignoring case.
        private static bool Same(string a, string b) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(a ?? "", b ?? "", CompareOptions.IgnoreCase) == 0;

        // ---- dispositions -------------------------------------------------------------------------------------

        /// <summary>
        /// Get-LiveLaneRcExclusionIndex: the rc profile's exclusions on this platform for every default and rc-required
        /// suite (Sort-Object -Unique order), from the repository's tests/test-execution-policy.json.
        /// </summary>
        public static ExclusionRow[] RcExclusions(string repoRoot)
        {
            var policy = NightlyPolicy.Read(repoRoot, "");
            var platform = OperatingSystem.IsLinux() ? "linux" : "windows";
            var ids = new List<string>();
            foreach (var id in policy.Object.DefaultSuites ?? new string[0]) ids.Add(id ?? "");
            PolicyProfile rc = null;
            if (policy.Object.Profiles != null) policy.Object.Profiles.TryGetValue("rc", out rc);
            foreach (var id in rc?.RequiredSuites ?? new string[0]) ids.Add(id ?? "");
            ids.RemoveAll(string.IsNullOrWhiteSpace);
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            ids.Sort((a, b) => compare.Compare(a, b, CompareOptions.IgnoreCase));
            var rows = new List<ExclusionRow>();
            string last = null;
            foreach (var id in ids)
            {
                if (last != null && compare.Compare(last, id, CompareOptions.IgnoreCase) == 0) continue;
                last = id;
                rows.AddRange(NightlyPolicy.ProfileExclusions(policy.Object, "rc", id, platform));
            }
            return rows.ToArray();
        }

        /// <summary>
        /// Get-LiveLaneGateDisposition: core for a live-claude core identity, else policy-excluded with the first
        /// exclusion row that names the gate's class, else diagnostic.
        /// </summary>
        public static LiveLaneGateDisposition Disposition(LiveLaneGate gate, IReadOnlyList<ExclusionRow> exclusions)
        {
            var key = gate.Key;
            foreach (var want in LiveLaneContract.Core)
                if (Same(want.Key, key)) return new LiveLaneGateDisposition("core", "CARD-1545", "live-claude core roster");
            foreach (var row in exclusions ?? new ExclusionRow[0])
                if (Same(row.Class, gate.Class)) return new LiveLaneGateDisposition("policy-excluded", row.Owner ?? "", row.Reason ?? "");
            return new LiveLaneGateDisposition("diagnostic", "CARD-1545", "live-claude diagnostic gate");
        }

        // ---- the matrix ---------------------------------------------------------------------------------------

        /// <summary>
        /// Invoke-LiveLaneGateMatrix over the repository's tests/live-lanes.json: the failed checks, none when every
        /// scanned gate has exactly one catalog row with the expected disposition, owner and reason, and the scan still
        /// sees an injected gate, ignores an ordinary test, counts a missing owner or reason and a duplicate row, and
        /// leaves a [GateShape] test unresolved. The injected sources are written to a scratch directory under
        /// .antiphon/live-lane-capture and removed.
        /// </summary>
        public static string[] Matrix(string repoRoot)
        {
            repoRoot = Path.GetFullPath(repoRoot);
            using var manifest = ReadManifest(repoRoot);
            var failures = new List<string>();
            var scan = ScanFiles(Files(repoRoot, null));
            var catalog = Rows(manifest.RootElement, "gates");
            var exclusions = RcExclusions(repoRoot);
            var index = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            var ambiguous = 0;
            foreach (var row in catalog)
            {
                var key = RowKey(row);
                if (index.ContainsKey(key)) ambiguous++;
                index[key] = row;
            }
            var invalid = CountInvalid(catalog, -1, "");
            var unmapped = Unmapped(scan, index);
            if (unmapped != 0) failures.Add("C1545 V-3/valid unmappedGateCount == 0");
            if (scan.Unresolved != 0) failures.Add("C1545 V-3/valid unresolvedGateCount == 0");
            if (ambiguous != 0) failures.Add("C1545 V-3/valid ambiguousDispositionCount == 0");
            if (invalid != 0) failures.Add("C1545 V-3/valid invalidDispositionCount == 0");
            foreach (var gate in scan.Gates)
            {
                if (!index.TryGetValue(gate.Key, out var row)) continue;
                var expected = Disposition(gate, exclusions);
                if (!Same(Field(row, "disposition"), expected.Disposition)) { failures.Add("C1545 V-3/valid disposition"); break; }
                if (!Same(Field(row, "owner"), expected.Owner)) { failures.Add("C1545 V-3/valid owner"); break; }
                if (!Same(Field(row, "reason"), expected.Reason)) { failures.Add("C1545 V-3/valid reason"); break; }
            }

            // Each scratch source is scanned on its own and added to the repository's scan: what a second full scan
            // with the extra file would have counted.
            var scratch = Path.Combine(repoRoot, ".antiphon", "live-lane-capture", "gate" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                var newGate = ScanFiles(new[] { WriteScratch(scratch, "NewGate.cs", NewGateSource) });
                if (unmapped + Unmapped(newGate, index) == 0) failures.Add("C1545 V-3/new-gate unmappedGateCount == 0");

                var ordinary = ScanFiles(new[] { WriteScratch(scratch, "Ordinary.cs", OrdinarySource) });
                if (Unmapped(ordinary, index) != 0 || ordinary.Unresolved != 0) failures.Add("C1545 V-3/ordinary-control inventoryAccepted");

                if (catalog.Count > 0)
                {
                    if (CountInvalid(catalog, 0, "owner") != 1) failures.Add("C1545 V-3/missing-owner invalidDispositionCount == 1");
                    if (CountInvalid(catalog, 0, "reason") != 1) failures.Add("C1545 V-3/missing-reason invalidDispositionCount == 1");
                    // The first row added again is always a second row with its key.
                }
                else
                {
                    failures.Add("C1545 V-3/missing-owner invalidDispositionCount == 1");
                    failures.Add("C1545 V-3/missing-reason invalidDispositionCount == 1");
                    failures.Add("C1545 V-3/duplicate-disposition ambiguousDispositionCount == 1");
                }

                var unknown = ScanFiles(new[] { WriteScratch(scratch, "Unknown.cs", UnknownSource) });
                var unknownUnresolved = scan.Unresolved + unknown.Unresolved;
                if (unknownUnresolved != scan.Unresolved + 1 || unknownUnresolved == 0) failures.Add("C1545 V-3/unknown-shape unresolvedGateCount == 1");
            }
            finally
            {
                if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
            }
            return failures.ToArray();
        }

        private static string WriteScratch(string directory, string name, string source)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, source);
            return path;
        }

        private static int Unmapped(LiveLaneGateScanResult scan, Dictionary<string, JsonElement> index)
        {
            var count = 0;
            foreach (var gate in scan.Gates)
                if (!index.ContainsKey(gate.Key)) count++;
            return count;
        }

        // Measure-LiveLaneGateInvalid over the rows (Copy-LiveLaneGateRows), the named field of row blankAt blanked:
        // the rows without an owner, a reason or a disposition.
        private static int CountInvalid(List<JsonElement> rows, int blankAt, string blanked)
        {
            var count = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                string Value(string name) => i == blankAt && name == blanked ? "" : Field(rows[i], name);
                if (string.IsNullOrWhiteSpace(Value("owner")) || string.IsNullOrWhiteSpace(Value("reason")) || string.IsNullOrWhiteSpace(Value("disposition"))) count++;
            }
            return count;
        }

        // ---- the catalog --------------------------------------------------------------------------------------

        /// <summary>Write-LiveLaneGateCatalog: rewrites the repository's tests/live-lanes.json.</summary>
        public static LiveLaneGateCatalog WriteCatalog(string repoRoot) => WriteCatalog(repoRoot, null);

        /// <summary>
        /// Write-LiveLaneGateCatalog to outputPath (the repository's tests/live-lanes.json when null): the manifest's
        /// core and additiveContract rows as they are, and one gates row per scanned gate in project, namespace,
        /// class, method and arguments order, with its shapes and disposition.
        /// </summary>
        public static LiveLaneGateCatalog WriteCatalog(string repoRoot, string outputPath)
        {
            repoRoot = Path.GetFullPath(repoRoot);
            var scan = Scan(repoRoot);
            var exclusions = RcExclusions(repoRoot);
            var gates = new List<LiveLaneGate>(scan.Gates);
            gates.Sort(CatalogOrder);
            var rows = new List<LiveLaneJsonObject>();
            foreach (var gate in gates) rows.Add(GateRow(gate, Disposition(gate, exclusions)));
            using var manifest = ReadManifest(repoRoot);
            var doc = new LiveLaneJsonObject
            {
                { "format", "antiphon-live-lanes-v1" },
                { "core", LiveLaneJson.Rows(manifest.RootElement, "core") },
                { "gates", rows },
                { "additiveContract", LiveLaneJson.Rows(manifest.RootElement, "additiveContract") },
            };
            var path = outputPath ?? Path.Combine(repoRoot, "tests", "live-lanes.json");
            LiveLaneJson.WriteFile(path, doc, finalNewLine: true);
            return new LiveLaneGateCatalog(rows.Count, scan.Unresolved, path);
        }

        // Sort-Object project, namespace, class, method, @{ Expression = { Get-LiveLaneArgumentKey ... } }.
        private static int CatalogOrder(LiveLaneGate a, LiveLaneGate b)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var order = compare.Compare(a.Project, b.Project, CompareOptions.IgnoreCase);
            if (order == 0) order = compare.Compare(a.Namespace, b.Namespace, CompareOptions.IgnoreCase);
            if (order == 0) order = compare.Compare(a.Class, b.Class, CompareOptions.IgnoreCase);
            if (order == 0) order = compare.Compare(a.Method, b.Method, CompareOptions.IgnoreCase);
            if (order == 0) order = compare.Compare(string.Join("\n", a.Arguments), string.Join("\n", b.Arguments), CompareOptions.IgnoreCase);
            return order;
        }

        private static LiveLaneJsonObject GateRow(LiveLaneGate gate, LiveLaneGateDisposition disposition) => new LiveLaneJsonObject
        {
            { "project", gate.Project },
            { "namespace", gate.Namespace },
            { "class", gate.Class },
            { "method", gate.Method },
            { "arguments", gate.Arguments },
            { "shapes", gate.Shapes },
            { "disposition", disposition.Disposition },
            { "owner", disposition.Owner },
            { "reason", disposition.Reason },
        };

        // ---- the manifest -------------------------------------------------------------------------------------

        // Read-LiveLaneManifestObject: tests/live-lanes.json, refused when missing.
        internal static JsonDocument ReadManifest(string repoRoot)
        {
            var path = Path.Combine(repoRoot, "tests", "live-lanes.json");
            if (!File.Exists(path)) throw new InvalidOperationException("manifest-missing");
            return LiveLaneJson.ReadFile(path);
        }

        private static List<JsonElement> Rows(JsonElement document, string name) => LiveLaneJson.Rows(document, name);

        private static bool TryProp(JsonElement row, string name, out JsonElement value) => LiveLaneJson.TryProp(row, name, out value);

        // [string](Get-LiveLaneProp $row name).
        private static string Field(JsonElement row, string name) => LiveLaneJson.Field(row, name);

        private static string Text(JsonElement value) => LiveLaneJson.Str(value);

        // Get-LiveLaneIdentityKey of a catalog row: its type when the class is blank.
        private static string RowKey(JsonElement row)
        {
            var cls = Field(row, "class");
            if (string.IsNullOrWhiteSpace(cls)) cls = Field(row, "type");
            var arguments = new List<string>();
            if (TryProp(row, "arguments", out var value) && value.ValueKind != JsonValueKind.Null)
            {
                if (value.ValueKind == JsonValueKind.Array)
                    foreach (var item in value.EnumerateArray()) arguments.Add(Text(item));
                else arguments.Add(Text(value));
            }
            return IdentityKey(Field(row, "project"), Field(row, "namespace"), cls, Field(row, "method"), arguments);
        }
    }
}
