// CARD-1820 S3 (moved from rc-input-reader-census.ps1): the mechanical runtime-reader census. Broadening is off, so an
// unresolved mask cannot hide an under-selected pair; any under-selected pair, unanalysable listing or no-op conflict
// fails. The literal scan is a lower bound. A second pass binds directory walks (EnumerateFiles, GetFiles,
// GetDirectories, SourceFiles) to a tracked directory of at least two segments. scripts/lib/rc-input-walks.json records
// filtered listings the line scan cannot bind, and names whole-repo walks. Unowned paths select nothing: they are
// printed, never a failure. Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class RcInputCensus
    {
        private static readonly Regex Strings = new Regex("@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.Compiled);
        private static readonly Regex Gap = new Regex("^[\\s,+()]*$", RegexOptions.Compiled);
        private static readonly Regex CompileItem = new Regex("<Compile\\s+Include=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex WalkWord = new Regex("EnumerateFiles|GetFiles|GetDirectories|SourceFiles|GetManifestResourceNames|GetManifestResourceStream", RegexOptions.CultureInvariant);
        private static readonly Regex Segment = new Regex(@"^[A-Za-z0-9_.@+-]+$", RegexOptions.CultureInvariant);

        private static bool Excluded(string source, string literal, string path)
        {
            if (source.EndsWith("ServerGitAssemblyTests.cs", StringComparison.Ordinal) && literal == "Antiphon.Server.csproj" && path == "server/Antiphon.Server.csproj") return true;
            if (source.EndsWith("PhaseJournalDiagnostic.cs", StringComparison.Ordinal) && literal == "Antiphon.Tests.csproj" && path.EndsWith("Antiphon.Tests.csproj", StringComparison.Ordinal)) return true;
            if (literal == "combined.pdf") return true;
            if (source.EndsWith("TestClassificationMetadata.cs", StringComparison.Ordinal) && path.EndsWith("slow-tests-allowlist.txt", StringComparison.Ordinal)) return true;
            return false;
        }

        private static string Unquote(string raw) =>
            raw.StartsWith("@\"", StringComparison.Ordinal) ? raw.Substring(2, raw.Length - 3).Replace("\"\"", "\"")
                : raw.Substring(1, raw.Length - 2).Replace("\\\\", "/").Replace("\\\"", "\"").Replace('\\', '/');

        // A run of adjacent string literals ("a" + "/b") is also read as its joined path.
        private static List<string> Literals(string line)
        {
            var found = new List<string>();
            var run = new List<string>();
            var last = 0;
            var started = false;
            foreach (Match match in Strings.Matches(line))
            {
                var text = Unquote(match.Value);
                if (started && !Gap.IsMatch(line.Substring(last, match.Index - last))) Flush(run, found);
                started = true;
                last = match.Index + match.Length;
                if (text.Length == 0 || text == "/") continue;
                run.Add(text);
            }
            Flush(run, found);
            return found;
        }

        private static void Flush(List<string> run, List<string> found)
        {
            if (run.Count == 1) found.Add(run[0]);
            else if (run.Count >= 2)
            {
                found.Add(string.Join("/", run));
                foreach (var part in run) if (part.IndexOf('/') >= 0) found.Add(part);
            }
            run.Clear();
        }

        // Suite \t source membership -> suite \t tracked path pairs named by a literal (exact path, a suffix of at least two
        // segments, or a base name with at most three tracked matches).
        private static string[] Pairs(string root, IReadOnlyList<string> tracked, IReadOnlyList<string> membership)
        {
            var trackedSet = new HashSet<string>(tracked, StringComparer.Ordinal);
            var byBase = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var bySuffix = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in tracked)
            {
                var slash = path.LastIndexOf('/');
                var baseName = slash < 0 ? path : path.Substring(slash + 1);
                if (!byBase.TryGetValue(baseName, out var list)) byBase[baseName] = list = new List<string>();
                list.Add(path);
                var start = 0;
                while (true)
                {
                    var next = path.IndexOf('/', start);
                    if (next < 0) break;
                    start = next + 1;
                    if (start >= path.Length || path.IndexOf('/', start) < 0) break;
                    var suffix = path.Substring(start);
                    if (!bySuffix.TryGetValue(suffix, out list)) bySuffix[suffix] = list = new List<string>();
                    list.Add(path);
                }
            }
            var cache = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<string>();
            var bySuite = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var row in membership)
            {
                var tab = row.IndexOf('\t');
                if (tab <= 0) continue;
                var suite = row.Substring(0, tab);
                if (!bySuite.TryGetValue(suite, out var members)) bySuite[suite] = members = new List<string>();
                members.Add(row.Substring(tab + 1));
            }
            foreach (var entry in bySuite)
                foreach (var source in entry.Value)
                {
                    if (!cache.TryGetValue(source, out var parsed))
                    {
                        parsed = new List<string>();
                        foreach (var line in File.ReadLines(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar))))
                        {
                            if (line.IndexOf('"') < 0) continue;
                            foreach (var literal in Literals(line))
                            {
                                if (literal.Length < 5 || literal.Length > 240) continue;
                                if (literal.IndexOf(' ') >= 0 || literal.IndexOf('*') >= 0 || literal.IndexOf('?') >= 0 || literal.IndexOf('$') >= 0) continue;
                                if (literal[0] == '/' || literal[0] == '.') continue;
                                parsed.Add(literal);
                            }
                        }
                        cache[source] = parsed;
                    }
                    foreach (var literal in parsed)
                    {
                        List<string> hits = null;
                        if (trackedSet.Contains(literal)) hits = new List<string> { literal };
                        else if (literal.IndexOf('/') >= 0) bySuffix.TryGetValue(literal, out hits);
                        else if (literal.IndexOf('.') >= 0 && byBase.TryGetValue(literal, out var bases) && bases.Count <= 3) hits = bases;
                        if (hits == null) continue;
                        foreach (var hit in hits)
                        {
                            if (Excluded(source, literal, hit) || !seen.Add(entry.Key + "\n" + hit)) continue;
                            rows.Add(entry.Key + "\t" + hit);
                        }
                    }
                }
            return rows.ToArray();
        }

        // A suite's sources: tracked .cs under its project directory and the literal <Compile Include> items of its project.
        private static List<string> Sources(RcInputInventory.GitReader git, string sha, string project, IReadOnlyList<string> tracked, HashSet<string> trackedSet)
        {
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var directory = project.Substring(0, project.LastIndexOf('/') + 1);
            foreach (var path in tracked)
                if (path.StartsWith(directory, StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal) && seen.Add(path)) files.Add(path);
            foreach (Match match in CompileItem.Matches(git.Blob(sha + ":" + project) ?? ""))
            {
                var value = match.Groups[1].Value.Replace('\\', '/');
                if (value.IndexOfAny(new[] { '$', '*', '?' }) >= 0) continue;
                var parts = new List<string>();
                foreach (var part in (directory.TrimEnd('/') + "/" + value).Split('/'))
                {
                    if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                    else if (part.Length > 0 && part != ".") parts.Add(part);
                }
                var resolved = string.Join("/", parts);
                if (resolved.EndsWith(".cs", StringComparison.Ordinal) && trackedSet.Contains(resolved) && seen.Add(resolved)) files.Add(resolved);
            }
            return files;
        }

        /// <summary>
        /// Runs the census over the repository at HEAD (refusing tests and census inputs that differ from HEAD). Lines go
        /// to output; the unowned list goes to .antiphon/rc-unowned-paths.txt. Returns 1 when any pair is under-selected,
        /// a listing is unanalysable or a no-op conflicts with a reader or an owner root; otherwise 0.
        /// </summary>
        public static int Run(string repositoryRoot, bool emitRows, bool scanOnly, TextWriter output)
        {
            var root = Path.GetFullPath(repositoryRoot);
            using var git = new RcInputInventory.GitReader(root);
            var sha = git.Run("rev-parse", "HEAD").Trim();
            var dirty = git.Run("diff", "--name-only", "HEAD", "--", "tests", "scripts/lib/rc-input-owners.json", "scripts/lib/rc-input-noop.json", "scripts/lib/rc-input-walks.json",
                "scripts/lib/Antiphon.Nightly/RcInputCensus.cs").Trim();
            if (dirty.Length > 0) throw new RcInputRefusal("census-dirty-tree:" + dirty);
            var tracked = git.Run("ls-files", "-z").Split('\0').Where(l => l.Length > 0).Select(l => l.Replace('\\', '/')).ToList();
            var trackedSet = new HashSet<string>(tracked, StringComparer.Ordinal);
            var suites = tracked.Where(p => p.StartsWith("tests/", StringComparison.Ordinal) && p.EndsWith(".csproj", StringComparison.Ordinal)).ToList();
            var membership = new List<string>();
            foreach (var suite in suites) foreach (var source in Sources(git, sha, suite, tracked, trackedSet)) membership.Add(suite + "\t" + source);
            var pairSeen = new HashSet<string>(StringComparer.Ordinal);
            var refs = new List<(string Suite, string Path)>();
            void Add(string suite, string path) { if (pairSeen.Add(suite + "\n" + path)) refs.Add((suite, path)); }
            foreach (var line in Pairs(root, tracked, membership))
            {
                var parts = line.Split('\t', 2);
                Add(parts[0], parts[1]);
            }
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in tracked)
                for (var next = path.IndexOf('/'); next >= 0; next = path.IndexOf('/', next + 1)) prefixes.Add(path.Substring(0, next));
            var walkedSuites = new HashSet<string>(StringComparer.Ordinal);
            var repoWalkSuites = new List<string>();
            const string walkPath = "scripts/lib/rc-input-walks.json";
            if (trackedSet.Contains(walkPath))
            {
                using var walkDoc = JsonDocument.Parse(git.Blob(sha + ":" + walkPath) ?? "{}");
                var walks = walkDoc.RootElement;
                if (walks.TryGetProperty("repoWalkSuites", out var repoWalk) && repoWalk.ValueKind == JsonValueKind.Array)
                    foreach (var id in repoWalk.EnumerateArray())
                        if (id.ValueKind == JsonValueKind.String && id.GetString().Length > 0) { repoWalkSuites.Add(id.GetString()); walkedSuites.Add(id.GetString()); }
                if (walks.TryGetProperty("walks", out var walkRows) && walkRows.ValueKind == JsonValueKind.Array)
                    foreach (var walk in walkRows.EnumerateArray())
                    {
                        var suite = walk.TryGetProperty("suite", out var s) ? s.GetString() ?? "" : "";
                        walkedSuites.Add(suite);
                        if (!walk.TryGetProperty("globs", out var globs) || globs.ValueKind != JsonValueKind.Array) continue;
                        foreach (var glob in globs.EnumerateArray())
                        {
                            var pattern = RcInputInventory.Pattern(glob.GetString());
                            foreach (var path in tracked) if (pattern.IsMatch(path)) Add(suite, path);
                        }
                    }
            }
            foreach (var id in repoWalkSuites) foreach (var path in tracked) Add(id, path);
            var unanalysable = new List<string>();
            foreach (var row in membership)
            {
                var parts = row.Split('\t', 2);
                var (suite, source) = (parts[0], parts[1]);
                var sawListing = false;
                var sawRepoRoot = false;
                foreach (var line in File.ReadAllLines(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar))))
                {
                    if (line.Contains("ls-files")) sawListing = true;
                    if (line.Contains("WorkingDirectory = repoRoot") || line.Contains("WorkingDirectory = repositoryRoot")) sawRepoRoot = true;
                    if (!WalkWord.IsMatch(line)) continue;
                    var segments = new List<string>();
                    var slashLiterals = new List<string>();
                    foreach (Match match in Strings.Matches(line))
                    {
                        var text = Unquote(match.Value);
                        if (text.Length < 1 || text.Length > 240) continue;
                        if (text.IndexOf(' ') >= 0 || text.IndexOf('*') >= 0 || text.IndexOf('?') >= 0 || text.IndexOf('$') >= 0) continue;
                        if (text[0] == '/' || text[0] == '.') continue;
                        if (text.Contains('/')) { slashLiterals.Add(text.Trim('/')); continue; }
                        if (Segment.IsMatch(text)) segments.Add(text);
                    }
                    var csOnly = line.Contains("*.cs");
                    var candidates = slashLiterals.Where(l => l.Split('/').Length >= 2 && l.Length >= 8 && prefixes.Contains(l)).ToList();
                    var run = new List<string>();
                    void Close() { if (run.Count >= 2 && string.Join("/", run).Length >= 8) candidates.Add(string.Join("/", run)); }
                    foreach (var segment in segments)
                    {
                        var trial = run.Count == 0 ? segment : string.Join("/", run) + "/" + segment;
                        if (prefixes.Contains(trial)) { run.Add(segment); continue; }
                        Close();
                        run.Clear();
                        if (prefixes.Contains(segment)) run.Add(segment);
                    }
                    Close();
                    foreach (var candidate in candidates)
                        foreach (var path in tracked)
                            if (path.StartsWith(candidate + "/", StringComparison.Ordinal) && (!csOnly || path.EndsWith(".cs", StringComparison.Ordinal))) Add(suite, path);
                }
                if (sawListing && sawRepoRoot && !walkedSuites.Contains(suite)) unanalysable.Add(suite + " " + source);
            }
            var byPathThenSuite = refs.OrderBy(r => r.Path, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Suite, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (scanOnly)
            {
                output.WriteLine("paths-checked=" + tracked.Count);
                output.WriteLine("suites-checked=" + suites.Count);
                output.WriteLine("pairs-checked=" + refs.Count);
                output.WriteLine("unanalysable=" + unanalysable.Count);
                output.WriteLine(repoWalkSuites.Count > 0 ? "repo-walk=" + string.Join(",", repoWalkSuites) : "repo-walk=none");
                foreach (var row in unanalysable) output.WriteLine("UNANALYSABLE: " + row);
                foreach (var r in byPathThenSuite) output.WriteLine("referenced: " + r.Suite + " " + r.Path);
                return 0;
            }
            var inventory = RcInputInventory.Read(git, sha);
            var owned = inventory.Suites.ToDictionary(s => s.Id, s => new HashSet<string>(s.Inputs.Where(i => i.Present).Select(i => i.Path), StringComparer.Ordinal), StringComparer.Ordinal);
            var under = byPathThenSuite.Where(r => !owned.TryGetValue(r.Suite, out var set) || !set.Contains(r.Path)).ToList();
            var noopHits = under.Where(r => inventory.IsNoopPath(r.Path)).ToList();
            var unowned = inventory.Unowned ?? Array.Empty<RcUnownedPath>();
            output.WriteLine("paths-checked=" + tracked.Count);
            output.WriteLine("suites-checked=" + suites.Count);
            output.WriteLine("pairs-checked=" + refs.Count);
            output.WriteLine("under-selected=" + under.Count);
            output.WriteLine("unanalysable=" + unanalysable.Count);
            output.WriteLine("noop-conflict=" + noopHits.Count);
            output.WriteLine("noop-vs-owner=" + inventory.NoopConflicts.Length);
            output.WriteLine("unowned-count=" + unowned.Length);
            output.WriteLine("unresolved-count=" + inventory.Unresolved.Length);
            foreach (var reason in inventory.Unresolved) output.WriteLine("unresolved: " + reason);
            output.WriteLine("creditBlockedBy=" + inventory.CreditBlockedBy);
            output.WriteLine("needsOrchestratorDecision=" + (inventory.NeedsOrchestratorDecision ? "True" : "False"));
            output.WriteLine(repoWalkSuites.Count > 0 ? "repo-walk=" + string.Join(",", repoWalkSuites) : "repo-walk=none");
            foreach (var row in unanalysable) output.WriteLine("UNANALYSABLE: " + row);
            foreach (var r in noopHits) output.WriteLine("noop-conflict: " + r.Suite + " " + r.Path);
            foreach (var row in inventory.NoopConflicts) output.WriteLine("noop-vs-owner: " + row);
            if (emitRows)
            {
                var i = 0;
                foreach (var r in under) output.WriteLine("{\"group\":\"scan-" + (++i).ToString("d2", CultureInfo.InvariantCulture) + "\",\"paths\":[\"" + r.Path + "\"],\"suites\":[\"" + r.Suite + "\"]}");
            }
            else foreach (var r in under) output.WriteLine("under-selected: " + r.Suite + " " + r.Path);
            var unownedDir = Path.Combine(root, ".antiphon");
            Directory.CreateDirectory(unownedDir);
            var unownedFile = Path.Combine(unownedDir, "rc-unowned-paths.txt");
            File.WriteAllLines(unownedFile, unowned.Select(r => r.Hint + "\t" + r.Path));
            foreach (var line in RcInputInventory.UnownedCensusLines(unowned, unownedFile)) output.WriteLine(line);
            return under.Count > 0 || unanalysable.Count > 0 || noopHits.Count > 0 || inventory.NoopConflicts.Length > 0 ? 1 : 0;
        }
    }
}
