// CARD-1313: the change-aware selection proposal (antiphon-test-selection-v1) for a baseline B and a candidate A, and the
// release selection contract. The 2026-11-22 plan (2026-11-22-remaining-ps1-to-csharp.md, unit 3) ported them from
// scripts/lib/test-selection.ps1 (Get-TestSelection) and scripts/lib/release-selection.ps1 (Get-ReleaseSelectionContract)
// and made scripts/select-test-rosters.ps1 the select-test-rosters command (Run); both scripts are deleted.
//
// A selection is intent, never execution or release authority: executionEvidence is false and activation unqualified.
// It reads Git objects only (the working tree never counts): the candidate's pinned execution policy and catalog, the
// diff from B to A (no rename inference, ordinal paths), each changed path classified by the catalog's rules and edges
// for its old and new owners, the dependency closure of the selected sets and their prerequisites. An unknown path, a
// full rule, B not an ancestor of A, or a removed owner selects every set (mode full).
//
// Selector identity (selectorHash): the LF-normalised text of SelectorFiles at A. The running selector must be the
// candidate's: its own sources are embedded in this assembly (Antiphon.Nightly.csproj) and compared, as LF text, with
// A's blobs (selector-source-mismatch otherwise). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The select-test-rosters command's options (the parameters of the deleted select-test-rosters.ps1).</summary>
    public sealed class TestSelectionOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "select-test-rosters --baseline-sha <sha> --candidate-sha <sha> [--profile rc] [--repository-root <dir>]"
            + " [--output-path <file>] [--json] [--timing-roots <dir,dir>]";

        private static readonly string[] Valued = { "--baseline-sha", "--candidate-sha", "--profile", "--repository-root", "--output-path", "--timing-roots" };

        /// <summary>The baseline commit B (a full commit id).</summary>
        public string BaselineSha { get; init; } = "";

        /// <summary>The candidate commit A (a full commit id).</summary>
        public string CandidateSha { get; init; } = "";

        /// <summary>The execution policy profile.</summary>
        public string Profile { get; init; } = "rc";

        /// <summary>The repository the commits are read from.</summary>
        public string RepositoryRoot { get; init; } = "";

        /// <summary>A file to create with the selection's JSON (never overwritten); empty for none.</summary>
        public string OutputPath { get; init; } = "";

        /// <summary>The selection's canonical JSON on stdout instead of the summary lines.</summary>
        public bool Json { get; init; }

        /// <summary>Timing journal roots: the selection carries each selected set's estimate (never changing its digest or sets).</summary>
        public IReadOnlyList<string> TimingRoots { get; init; } = Array.Empty<string>();

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or a missing commit.</summary>
        public static TestSelectionOptions Parse(IReadOnlyList<string> arguments, string currentDirectory)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, new[] { "--json" });
            return new TestSelectionOptions
            {
                BaselineSha = flags.Required("--baseline-sha"), CandidateSha = flags.Required("--candidate-sha"), Profile = flags.Value("--profile", "rc"),
                RepositoryRoot = flags.Value("--repository-root", currentDirectory), OutputPath = flags.Value("--output-path", ""), Json = flags.Switch("--json"),
                TimingRoots = flags.List("--timing-roots"),
            };
        }
    }

    /// <summary>Get-TestSelection, Get-ReleaseSelectionContract and the select-test-rosters command.</summary>
    public static class TestSelection
    {
        /// <summary>The execution policy, read at the candidate.</summary>
        public const string PolicyPath = "tests/test-execution-policy.json";

        /// <summary>The selection catalog the policy pins.</summary>
        public const string CatalogPath = "tests/test-selection-policy.json";

        private static readonly string[] Selector =
        {
            "scripts/lib/Antiphon.Nightly/TestSelection.cs", "scripts/lib/Antiphon.Nightly/TestSelectionCatalog.cs",
            "scripts/lib/Antiphon.Nightly/TestSelectionGit.cs", "scripts/lib/Antiphon.Nightly/TestSelectionJson.cs",
        };

        private static readonly Regex AnyCaseSha = new Regex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex Status = new Regex("^[ADMTUXB]$", RegexOptions.CultureInvariant);

        /// <summary>The selector identity: the sources whose LF text selectorHash digests (each embedded as selector/&lt;file name&gt;).</summary>
        public static string[] SelectorFiles => (string[])Selector.Clone();

        /// <summary>The files a release selection contract reads at a commit: both policies, the selector identity and global.json.</summary>
        public static string[] ContractFiles => new[] { PolicyPath, CatalogPath }.Concat(Selector).Concat(new[] { "global.json" }).ToArray();

        /// <summary>The running selector's own source of a SelectorFiles path, as File.ReadAllText reads it.</summary>
        public static string RunningSource(string path)
        {
            if (Array.IndexOf(Selector, path) < 0) throw new TestSelectionRefusal("selector-source-unknown:" + path);
            using var stream = typeof(TestSelection).Assembly.GetManifestResourceStream("selector/" + Path.GetFileName(path))
                ?? throw new TestSelectionRefusal("selector-source-unavailable:" + path);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), true);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// selectorHash: for each SelectorFiles path in order, the candidate's text and the running text, both LF-normalised,
        /// must be equal (selector-source-mismatch otherwise); the digest of the map of path to the digest of that text.
        /// </summary>
        public static string SelectorHash(Func<string, string> candidate, Func<string, string> running)
        {
            var digests = new OrderedDictionary(StringComparer.Ordinal);
            foreach (var path in Selector)
            {
                var pinned = TestSelectionJson.LfText(candidate(path));
                if (pinned != TestSelectionJson.LfText(running(path))) throw new TestSelectionRefusal("selector-source-mismatch");
                digests[path] = TestSelectionJson.Digest(pinned);
            }
            return TestSelectionJson.Digest(digests);
        }

        /// <summary>
        /// Get-ReleaseSelectionContract: the policy hash (checked), the profile (resolved), the catalog hash (checked and
        /// pinned), the selector hash of the commit's SelectorFiles, the runtime hash of global.json, the profile and this
        /// platform.
        /// </summary>
        public static OrderedDictionary Contract(string repositoryRoot, string sha, string profile)
        {
            TestSelectionGit.Prefetch(repositoryRoot, sha, ContractFiles);
            string Blob(string path) => TestSelectionGit.Blob(repositoryRoot, sha, path);
            var policy = ExecutionPolicy.Parse(Blob(PolicyPath));
            if (policy.PolicyHash != NightlyPolicy.Hash(policy)) throw new TestSelectionRefusal("policy-hash-stale");
            NightlyPolicy.Profile(policy, profile);
            var catalog = TestSelectionCatalog.Parse(Blob(CatalogPath));
            var catalogHash = PsValue.Str(catalog["catalogHash"]);
            if (policy.SelectionCatalog?.Hash != catalogHash) throw new TestSelectionRefusal("catalog-pin-mismatch");
            var selectors = new OrderedDictionary(StringComparer.Ordinal);
            foreach (var path in Selector) selectors[path] = TestSelectionJson.Digest(TestSelectionJson.LfText(Blob(path)));
            return Ordered(("policyHash", policy.PolicyHash), ("catalogHash", catalogHash), ("selectorHash", TestSelectionJson.Digest(selectors)),
                ("runtimeHash", TestSelectionJson.Digest(Blob("global.json"))), ("profile", profile),
                ("platform", Path.DirectorySeparatorChar == '\\' ? "windows" : "linux"));
        }

        /// <summary>
        /// Get-TestSelection: the antiphon-test-selection-v1 proposal for baseline and candidate (full commit ids of commit
        /// objects) under the profile, its digest last. Refusals are TestSelectionRefusal (or the policy reader's own).
        /// </summary>
        public static OrderedDictionary Select(string repositoryRoot, string baselineSha, string candidateSha, string profile)
        {
            foreach (var sha in new[] { baselineSha, candidateSha })
            {
                if (sha == null || !AnyCaseSha.IsMatch(sha)) throw new TestSelectionRefusal("full-commit-sha-required");
                if (TestSelectionGit.Run(repositoryRoot, new[] { "cat-file", "-t", sha }).Stdout.Trim() != "commit") throw new TestSelectionRefusal("commit-object-required");
            }
            baselineSha = baselineSha.ToLowerInvariant();
            candidateSha = candidateSha.ToLowerInvariant();
            profile ??= "";
            TestSelectionGit.Prefetch(repositoryRoot, candidateSha, new[] { PolicyPath, CatalogPath }.Concat(Selector).ToArray());
            var policy = ExecutionPolicy.Parse(TestSelectionGit.Blob(repositoryRoot, candidateSha, PolicyPath));
            if (policy.SchemaVersion != 3 && policy.SchemaVersion != 4) throw new TestSelectionRefusal("policy-schema-unsupported");
            if (policy.PolicyHash != NightlyPolicy.Hash(policy)) throw new TestSelectionRefusal("policy-hash-stale");
            NightlyPolicy.Profile(policy, profile);
            NightlyPolicy.ProjectDispositions(policy);
            if (policy.SelectionCatalog?.Path != CatalogPath) throw new TestSelectionRefusal("catalog-path-unsupported");
            var catalog = TestSelectionCatalog.Parse(TestSelectionGit.Blob(repositoryRoot, candidateSha, CatalogPath));
            var catalogHash = PsValue.Str(catalog["catalogHash"]);
            if (policy.SelectionCatalog.Hash != catalogHash) throw new TestSelectionRefusal("catalog-pin-mismatch");
            var selectorHash = SelectorHash(path => TestSelectionGit.Blob(repositoryRoot, candidateSha, path), RunningSource);

            var candidatePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in TestSelectionGit.Run(repositoryRoot, new[] { "ls-tree", "-r", "--name-only", "-z", candidateSha, "--" }).Stdout.Split('\0'))
                if (path.Length > 0) candidatePaths.Add(path);
            IDictionary baselineCatalog = null;
            if (TestSelectionGit.Run(repositoryRoot, new[] { "ls-tree", "--name-only", "-z", baselineSha, "--", CatalogPath }).Stdout.Length > 0)
            {
                baselineCatalog = TestSelectionCatalog.Parse(TestSelectionGit.Blob(repositoryRoot, baselineSha, CatalogPath));
                var baselinePolicy = ExecutionPolicy.Parse(TestSelectionGit.Blob(repositoryRoot, baselineSha, PolicyPath));
                if (baselinePolicy.PolicyHash != NightlyPolicy.Hash(baselinePolicy) || baselinePolicy.SelectionCatalog?.Hash != PsValue.Str(baselineCatalog["catalogHash"]))
                    throw new TestSelectionRefusal("baseline-policy-hash-stale");
            }

            var fallback = new HashSet<string>(StringComparer.Ordinal);
            if (TestSelectionGit.Run(repositoryRoot, new[] { "merge-base", "--is-ancestor", baselineSha, candidateSha }, new[] { 0, 1 }).Exit == 1) fallback.Add("baseline-not-ancestor");
            var diff = TestSelectionGit.Run(repositoryRoot, new[] { "diff", "--no-ext-diff", "--name-status", "-z", "--no-renames", baselineSha, candidateSha, "--" }).Stdout;
            var tokens = diff.Split('\0');
            if (diff.Length > 0 && (tokens[tokens.Length - 1] != "" || tokens.Length % 2 != 1)) throw new TestSelectionRefusal("malformed-diff");
            var changes = new List<(string Path, string Status)>();
            for (var i = 0; i < tokens.Length - 1; i += 2)
            {
                string status = tokens[i], path = tokens[i + 1];
                if (!Status.IsMatch(status) || path.Length == 0 || path.StartsWith("/", StringComparison.CurrentCulture) || path.Contains('\0')
                    || path.Split('/').Contains("..", StringComparer.OrdinalIgnoreCase)) throw new TestSelectionRefusal("malformed-diff");
                changes.Add((path, status));
            }

            var setMap = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
            var setOrder = new List<string>();
            foreach (var item in TestSelectionJson.Elements(catalog["sets"]))
            {
                var set = Dictionary(item);
                var id = PsValue.Str(set["id"]);
                setMap.Add(id, set);
                setOrder.Add(id);
            }
            var selected = new HashSet<string>(StringComparer.Ordinal) { "core" };
            var selectedOrder = new List<string> { "core" };
            bool Choose(string id)
            {
                if (!selected.Add(id)) return false;
                selectedOrder.Add(id);
                return true;
            }
            var patterns = new Dictionary<string, Regex>(StringComparer.Ordinal);
            var records = new List<object>();
            var edges = new List<object>();
            foreach (var path in TestSelectionJson.Sorted(changes.Select(c => c.Path).ToArray()))
            {
                var change = changes.First(c => c.Path == path);
                var roles = change.Status == "A" ? new[] { "new" } : change.Status == "D" ? new[] { "old" } : new[] { "old", "new" };
                foreach (var role in roles)
                {
                    var source = role == "old" ? baselineCatalog : catalog;
                    var ruleIds = new List<string>();
                    var impacts = new List<object>();
                    var edgeIds = new List<string>();
                    if (source == null) fallback.Add("baseline-catalog-unavailable");
                    else
                    {
                        var rules = TestSelectionJson.Elements(source["rules"]).Select(Dictionary).ToList();
                        bool Listed(IDictionary rule) => TestSelectionJson.Elements(rule["paths"]).Any(p => PsValue.BaseObject(p) is string s && s == path);
                        string Partition(IDictionary rule) => PsValue.BaseObject(rule["partition"]) as string;
                        var providerMatch = rules.Any(r => Partition(r) == "provider" && Listed(r));
                        foreach (var rule in rules)
                        {
                            if (providerMatch && Partition(rule) == "shared") continue;
                            var match = Listed(rule);
                            if (!match && PsValue.IsTrue(rule["pattern"]))
                            {
                                var text = PsValue.Str(rule["pattern"]);
                                if (!patterns.TryGetValue(text, out var regex)) patterns[text] = regex = new Regex(text, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                                try { match = regex.IsMatch(path); }
                                catch (RegexMatchTimeoutException) { throw new TestSelectionRefusal("invalid-rule-timeout"); }
                            }
                            if (!match) continue;
                            var ruleId = PsValue.Str(rule["id"]);
                            ruleIds.Add(ruleId);
                            // @($rule.sets): a rule without sets adds one null owner, which the owner lookup refused.
                            if (rule["sets"] == null) throw new TestSelectionRefusal("rule-without-sets:" + ruleId);
                            impacts.AddRange(TestSelectionJson.Elements(rule["sets"]));
                            if (PsValue.IsTrue(rule["full"])) fallback.Add(ruleId == "P-AUTHORITY" || ruleId == "P-BUILD" ? "selection-authority-changed" : "rule-full:" + ruleId);
                        }
                        foreach (var edge in TestSelectionJson.Elements(source["edges"]).Select(Dictionary))
                        {
                            if (!(PsValue.BaseObject(edge["path"]) is string edgePath && edgePath == path)) continue;
                            edgeIds.Add(PsValue.Str(edge["id"]));
                            if (edge["sets"] == null) throw new TestSelectionRefusal("edge-without-sets:" + PsValue.Str(edge["id"]));
                            impacts.AddRange(TestSelectionJson.Elements(edge["sets"]));
                            edges.Add(Ordered(("id", edge["id"]), ("path", path), ("role", role), ("kind", edge["kind"]), ("sets", TestSelectionJson.Sorted(edge["sets"]))));
                        }
                        if (ruleIds.Count == 0 && edgeIds.Count == 0)
                        {
                            ruleIds.Add("P-UNKNOWN");
                            fallback.Add("unknown-path:" + path);
                        }
                    }
                    foreach (var impact in impacts)
                    {
                        var id = PsValue.Str(impact);
                        if (setMap.ContainsKey(id)) Choose(id);
                        else fallback.Add("removed-owner:" + id);
                    }
                    records.Add(Ordered(("path", path), ("status", change.Status), ("role", role), ("ruleIds", TestSelectionJson.Sorted(ruleIds)),
                        ("sets", TestSelectionJson.Sorted(impacts)), ("edges", TestSelectionJson.Sorted(edgeIds))));
                }
            }
            if (fallback.Count > 0) foreach (var id in setOrder) Choose(id);
            var queue = new Queue<string>(selectedOrder);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var target in TestSelectionJson.Elements(setMap[id]["dependsOn"]).Select(PsValue.Str))
                {
                    edges.Add(Ordered(("id", "set:" + id + "->" + target), ("kind", "dependsOn"), ("from", id), ("to", target)));
                    if (Choose(target)) queue.Enqueue(target);
                }
            }
            var prerequisiteMap = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
            foreach (var item in TestSelectionJson.Elements(catalog["prerequisites"]))
            {
                var prerequisite = Dictionary(item);
                prerequisiteMap[PsValue.Str(prerequisite["id"])] = prerequisite;
            }
            var prerequisites = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in selectedOrder)
                foreach (var p in TestSelectionJson.Elements(setMap[id]["prerequisites"]).Select(PsValue.Str))
                    if (prerequisites.Add(p)) queue.Enqueue(p);
            while (queue.Count > 0)
                foreach (var p in TestSelectionJson.Elements(prerequisiteMap[queue.Dequeue()]["dependsOn"]).Select(PsValue.Str))
                    if (prerequisites.Add(p)) queue.Enqueue(p);

            var removed = new List<object>();
            var selectedSets = new List<IDictionary>();
            foreach (var id in TestSelectionJson.Sorted(selectedOrder.ToArray()))
            {
                var set = setMap[id];
                var surviving = new List<object>();
                foreach (var member in TestSelectionJson.Elements(set["members"]))
                {
                    var memberSource = Dictionary(member)["source"];
                    if (PsValue.IsTrue(memberSource) && !candidatePaths.Contains(PsValue.Str(memberSource)))
                        removed.Add(Ordered(("set", id), ("member", member), ("reason", "source-removed")));
                    else surviving.Add(member);
                }
                var copy = new OrderedDictionary(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in set) copy[entry.Key] = entry.Value;
                copy["members"] = CultureSorted(surviving).ToArray();
                selectedSets.Add(copy);
            }

            var roots = TestSelectionGit.Run(repositoryRoot, new[] { "rev-list", "--max-parents=0", candidateSha, "--" }).Stdout.Trim().Split('\n');
            // One rev-parse names both blobs, one line each in argument order.
            var blobIds = TestSelectionGit.Run(repositoryRoot, new[] { "rev-parse", candidateSha + ":" + PolicyPath, candidateSha + ":" + CatalogPath })
                .Stdout.Trim().Split('\n').Select(line => line.Trim()).ToArray();
            if (blobIds.Length != 2 || !FullSha.IsMatch(blobIds[0]) || !FullSha.IsMatch(blobIds[1])) throw new TestSelectionRefusal("git-read-failed:rev-parse:shape");
            var obligations = new List<object>();
            foreach (var set in selectedSets)
            {
                var anyPlatform = TestSelectionJson.Elements(set["platforms"]).Any(p => PsValue.BaseObject(p) is string s && s == "any");
                if (!(PsValue.BaseObject(set["eligibility"]) is string eligibility && eligibility == "unattended") || !anyPlatform)
                    obligations.Add(Ordered(("set", set["id"]), ("eligibility", set["eligibility"]), ("platforms", set["platforms"]), ("reason", set["reason"])));
            }
            var result = Ordered(
                ("format", "antiphon-test-selection-v1"), ("repositoryIdentity", TestSelectionJson.Digest(TestSelectionJson.Sorted(roots))),
                ("baselineSha", baselineSha), ("candidateSha", candidateSha),
                ("policyBlob", blobIds[0]), ("policyHash", policy.PolicyHash), ("catalogBlob", blobIds[1]), ("catalogHash", catalogHash),
                ("selectorVersion", 1), ("selectorHash", selectorHash), ("profile", profile), ("mode", fallback.Count > 0 ? "full" : "narrow"),
                ("fallbackReasons", TestSelectionJson.Sorted(fallback.ToArray())), ("changedPaths", records.ToArray()), ("selectedSets", selectedSets.Cast<object>().ToArray()),
                ("unselectedSets", TestSelectionJson.Sorted(setOrder.Where(id => !selected.Contains(id)).ToArray())),
                ("prerequisites", TestSelectionJson.Sorted(prerequisites.ToArray())), ("explanationEdges", CultureSorted(edges).ToArray()),
                ("unresolvedObligations", obligations.ToArray()), ("removedMembers", removed.ToArray()), ("executionEvidence", false), ("activation", "unqualified"));
            result["digest"] = TestSelectionJson.Digest(result);
            return result;
        }

        /// <summary>
        /// The select-test-rosters command: the selection (with the timing estimates when roots are given; a timing fault
        /// leaves them out), written once to OutputPath when given, then its JSON or the summary lines on host.Out. Exit 0;
        /// 2 refused ("selection refused: &lt;reason&gt;" on host.Error).
        /// </summary>
        public static int Run(TestSelectionOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                IDictionary selection = Select(options.RepositoryRoot, options.BaselineSha, options.CandidateSha, options.Profile);
                if (options.TimingRoots.Count > 0)
                {
                    try { selection = TestTimings.AddEstimates(selection, TestTimings.Report(options.TimingRoots.ToArray(), "roster")); }
                    catch (Exception e) when (!(e is OutOfMemoryException)) { }
                }
                var text = TestSelectionJson.Canonical(selection);
                if (!string.IsNullOrEmpty(options.OutputPath))
                {
                    // Never overwrite an older intent and leave it looking like the requested run.
                    using var stream = new FileStream(options.OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var bytes = new UTF8Encoding(false).GetBytes(text + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (options.Json)
                {
                    host.Out.WriteLine(text);
                    return 0;
                }
                host.Out.WriteLine("mode=" + PsValue.Str(selection["mode"]) + " activation=unqualified executionEvidence=false digest=" + PsValue.Str(selection["digest"]));
                foreach (var set in TestSelectionJson.Elements(selection["selectedSets"]).Select(Dictionary))
                    host.Out.WriteLine(PsValue.Str(set["id"]) + " eligibility=" + PsValue.Str(set["eligibility"]) + " platforms=" + Joined(set["platforms"]));
                foreach (var change in TestSelectionJson.Elements(selection["changedPaths"]).Select(Dictionary))
                    host.Out.WriteLine(PsValue.Str(change["role"]) + " " + PsValue.Str(change["path"]) + ": " + Joined(change["ruleIds"]) + " edges=" + Joined(change["edges"]));
                foreach (var reason in TestSelectionJson.Elements(selection["fallbackReasons"])) host.Out.WriteLine("fallback: " + PsValue.Str(reason));
                host.Out.WriteLine("prerequisites: " + Joined(selection["prerequisites"]));
                return 0;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                host.Error.WriteLine("selection refused: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 2;
            }
        }

        private static string Joined(object values) => string.Join(",", TestSelectionJson.Elements(values).Select(PsValue.Str));

        private static IDictionary Dictionary(object value) =>
            PsValue.BaseObject(value) as IDictionary ?? throw new TestSelectionRefusal("invalid-catalog-object");

        /// <summary>Sort-Object {canonical JSON} -CaseSensitive: the current culture, case-sensitive; equal keys keep their order.</summary>
        private static IEnumerable<object> CultureSorted(IEnumerable<object> items)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            return items.Select(item => (Item: item, Key: TestSelectionJson.Canonical(item)))
                .OrderBy(pair => pair.Key, Comparer<string>.Create((x, y) => compare.Compare(x, y, CompareOptions.None)))
                .Select(pair => pair.Item);
        }

        private static OrderedDictionary Ordered(params (string Key, object Value)[] pairs)
        {
            var map = new OrderedDictionary(StringComparer.Ordinal);
            foreach (var (key, value) in pairs) map[key] = value;
            return map;
        }
    }
}
