// Get-TestSelectionCore (scripts/lib/test-selection.ps1) for the attempt's baseline B and candidate A: the diff
// between them, each changed path classified by the catalog's rules and edges, and the sets, prerequisites and
// explanation the proposal names. The caller has already asserted both commits and that B is an ancestor of A.
// Every refusal, and every value whose PowerShell reading this port would have to guess at, is NotHandled.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Scripts
{
    internal static class TestSelection
    {
        private const string PolicyPath = "tests/test-execution-policy.json";
        private const string CatalogPath = "tests/test-selection-policy.json";
        private static readonly string[] SelectorFiles = { "scripts/select-test-rosters.ps1", "scripts/lib/test-selection.ps1" };
        private static readonly Regex Status = new Regex("^[ADMTUXB]$", RegexOptions.CultureInvariant);
        private static readonly Regex ProjectFile = new Regex(@"^[^\\]+\.csproj$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static List<object> List(object value, string reason)
        {
            if (value == null) return new List<object>();
            if (value is List<object> list) return list;
            throw new NotHandledException(reason);
        }

        private static List<string> Strings(object value, string reason)
        {
            var result = new List<string>();
            foreach (object item in List(value, reason))
            {
                if (!(item is string s)) throw new NotHandledException(reason);
                result.Add(s);
            }
            return result;
        }

        private static string OptionalString(object value, string reason)
        {
            if (value == null) return null;
            if (value is string s) return s;
            throw new NotHandledException(reason);
        }

        /// <summary>The scripts directory the bridge runs from: the selector files it holds must be the candidate's.</summary>
        internal static PsObject Select(string root, string baselineSha, string candidateSha, string profile, string scriptsLib, PsObject contract)
        {
            var texts = ReleaseSelection.Texts(root, candidateSha, new[] { PolicyPath, CatalogPath, SelectorFiles[0], SelectorFiles[1] });
            if (!(PsJson.Parse(texts[PolicyPath]) is PsObject policy)) throw new NotHandledException("policy-shape");
            if (!(policy.Get("schemaVersion") is long schema) || schema != 3 && schema != 4) throw new NotHandledException("policy-schema-unsupported");
            if (!(policy.Get("policyHash") is string policyHash) || policyHash != ReleaseSelection.PolicyHash(policy)) throw new NotHandledException("policy-hash-stale");
            ProjectDispositions(policy);
            if (!(policy.Get("selectionCatalog") is PsObject pin) || !(pin.Get("path") is string catalogPath) || catalogPath != CatalogPath) throw new NotHandledException("catalog-path-unsupported");
            if (!(PsJson.Parse(texts[CatalogPath]) is PsObject catalog)) throw new NotHandledException("catalog-shape");
            SelectionCatalog.Assert(catalog);
            string catalogHash = (string)catalog.GetExact("catalogHash");
            if (!(pin.Get("hash") is string pinnedHash) || pinnedHash != catalogHash) throw new NotHandledException("catalog-pin-mismatch");

            string scriptsRepo = Path.GetDirectoryName(Path.GetDirectoryName(scriptsLib));
            if (string.IsNullOrEmpty(scriptsRepo)) throw new NotHandledException("scripts-root");
            var selectorBytes = new PsObject();
            foreach (string path in SelectorFiles)
            {
                string pinned = ReleaseSelection.LfText(texts[path]);
                string actual = ReleaseSelection.LfText(File.ReadAllText(Path.Combine(scriptsRepo, path)));
                if (pinned != actual) throw new NotHandledException("selector-source-mismatch");
                selectorBytes.Pairs.Add(new KeyValuePair<string, object>(path, ReleaseSelection.SelectionDigest(pinned)));
            }

            string tree = ReleaseSelection.Git(root, new[] { "ls-tree", "-r", "--name-only", "-z", candidateSha, "--" }, new[] { 0 }).Stdout;
            var candidatePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in tree.Split('\0')) if (path.Length > 0) candidatePaths.Add(path);

            PsObject baselineCatalog = null;
            string oldTree = ReleaseSelection.Git(root, new[] { "ls-tree", "--name-only", "-z", baselineSha, "--", CatalogPath }, new[] { 0 }).Stdout;
            if (oldTree.Length > 0)
            {
                var old = ReleaseSelection.Texts(root, baselineSha, new[] { CatalogPath, PolicyPath });
                if (!(PsJson.Parse(old[CatalogPath]) is PsObject parsedCatalog)) throw new NotHandledException("baseline-catalog-shape");
                SelectionCatalog.Assert(parsedCatalog);
                baselineCatalog = parsedCatalog;
                if (!(PsJson.Parse(old[PolicyPath]) is PsObject baselinePolicy) || !(baselinePolicy.Get("policyHash") is string baselineHash)
                    || baselineHash != ReleaseSelection.PolicyHash(baselinePolicy)
                    || !(baselinePolicy.Get("selectionCatalog") is PsObject baselinePin) || !(baselinePin.Get("hash") is string baselinePinned)
                    || baselinePinned != (string)parsedCatalog.GetExact("catalogHash")) throw new NotHandledException("baseline-policy-hash-stale");
            }

            // The attempt asserted merge-base --is-ancestor B A before asking: baseline-not-ancestor cannot arise here.
            var fallback = new HashSet<string>(StringComparer.Ordinal);
            string diff = ReleaseSelection.Git(root, new[] { "diff", "--no-ext-diff", "--name-status", "-z", "--no-renames", baselineSha, candidateSha, "--" }, new[] { 0 }).Stdout;
            string[] tokens = diff.Split('\0');
            if (diff.Length > 0 && (tokens[tokens.Length - 1] != "" || tokens.Length % 2 != 1)) throw new NotHandledException("malformed-diff");
            var changes = new List<(string Path, string Status)>();
            for (int i = 0; i < tokens.Length - 1; i += 2)
            {
                string status = tokens[i], path = tokens[i + 1];
                if (!Status.IsMatch(status) || path.Length == 0 || path.StartsWith("/", StringComparison.CurrentCulture) || path.Contains('\0') || path.Split('/').Contains(".."))
                    throw new NotHandledException("malformed-diff");
                changes.Add((path, status));
            }

            var setMap = new Dictionary<string, PsObject>(StringComparer.Ordinal);
            foreach (object set in List(catalog.GetExact("sets"), "catalog-sets")) setMap.Add((string)((PsObject)set).GetExact("id"), (PsObject)set);
            var selected = new HashSet<string>(StringComparer.Ordinal) { "core" };
            var selectedOrder = new List<string> { "core" };
            void Select(string id) { if (selected.Add(id)) selectedOrder.Add(id); }
            var records = new List<object>();
            var edges = new List<PsObject>();
            foreach (string path in PsSort.Ordinal(changes.Select(c => c.Path)).Cast<string>())
            {
                var change = changes.First(c => c.Path == path);
                string[] roles = change.Status == "A" ? new[] { "new" } : change.Status == "D" ? new[] { "old" } : new[] { "old", "new" };
                foreach (string role in roles)
                {
                    PsObject source = role == "old" ? baselineCatalog : catalog;
                    var ruleIds = new List<string>();
                    var impacts = new List<string>();
                    var edgeIds = new List<string>();
                    if (source == null) fallback.Add("baseline-catalog-unavailable");
                    else
                    {
                        var rules = List(source.GetExact("rules"), "catalog-rules").Cast<PsObject>().ToList();
                        bool PathListed(PsObject rule) => List(rule.GetExact("paths"), "rule-paths").Any(p => p is string s && s == path);
                        string Partition(PsObject rule) => OptionalString(rule.GetExact("partition"), "rule-partition");
                        bool providerMatch = false;
                        foreach (PsObject r in rules) if (Partition(r) == "provider" && PathListed(r)) providerMatch = true;
                        foreach (PsObject rule in rules)
                        {
                            if (providerMatch && Partition(rule) == "shared") continue;
                            bool match = PathListed(rule);
                            string pattern = OptionalString(rule.GetExact("pattern"), "rule-pattern");
                            if (!match && !string.IsNullOrEmpty(pattern))
                            {
                                try { match = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).IsMatch(path); }
                                catch (RegexMatchTimeoutException) { throw new NotHandledException("invalid-rule-timeout"); }
                            }
                            if (!match) continue;
                            string ruleId = (string)rule.GetExact("id");
                            ruleIds.Add(ruleId);
                            object ruleSets = rule.GetExact("sets");
                            // @($null) adds one null impact, and Dictionary.ContainsKey($null) then throws: a refusal.
                            if (ruleSets == null) throw new NotHandledException("rule-sets-null");
                            impacts.AddRange(Strings(ruleSets, "rule-sets"));
                            if (rule.GetExact("full") is bool full && full)
                                fallback.Add(ruleId == "P-AUTHORITY" || ruleId == "P-BUILD" ? "selection-authority-changed" : "rule-full:" + ruleId);
                        }
                        foreach (PsObject edge in List(source.GetExact("edges"), "catalog-edges").Cast<PsObject>())
                        {
                            if (!((string)edge.GetExact("path") == path)) continue;
                            string edgeId = (string)edge.GetExact("id");
                            object edgeSets = edge.GetExact("sets");
                            if (edgeSets == null) throw new NotHandledException("edge-sets-null");
                            var sets = Strings(edgeSets, "edge-sets");
                            edgeIds.Add(edgeId);
                            impacts.AddRange(sets);
                            edges.Add(ReleaseSelection.Obj(("id", edgeId), ("path", path), ("role", role), ("kind", (string)edge.GetExact("kind")), ("sets", PsSort.Ordinal(sets))));
                        }
                        if (ruleIds.Count == 0 && edgeIds.Count == 0) { ruleIds.Add("P-UNKNOWN"); fallback.Add("unknown-path:" + path); }
                    }
                    foreach (string id in impacts) { if (setMap.ContainsKey(id)) Select(id); else fallback.Add("removed-owner:" + id); }
                    records.Add(ReleaseSelection.Obj(("path", path), ("status", change.Status), ("role", role), ("ruleIds", PsSort.Ordinal(ruleIds)),
                        ("sets", PsSort.Ordinal(impacts)), ("edges", PsSort.Ordinal(edgeIds))));
                }
            }
            if (fallback.Count > 0) foreach (string id in setMap.Keys) Select(id);
            var queue = new Queue<string>(selectedOrder);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                foreach (string target in Strings(setMap[id].GetExact("dependsOn"), "set-depends"))
                {
                    edges.Add(ReleaseSelection.Obj(("id", "set:" + id + "->" + target), ("kind", "dependsOn"), ("from", id), ("to", target)));
                    if (selected.Add(target)) { selectedOrder.Add(target); queue.Enqueue(target); }
                }
            }
            // $prereqMap is a case-insensitive @{}: ids that differ only in case would collide there.
            var prereqMap = new Dictionary<string, PsObject>(StringComparer.OrdinalIgnoreCase);
            foreach (object p in List(catalog.GetExact("prerequisites"), "catalog-prerequisites"))
            {
                string id = (string)((PsObject)p).GetExact("id");
                if (prereqMap.ContainsKey(id)) throw new NotHandledException("prerequisite-case");
                prereqMap.Add(id, (PsObject)p);
            }
            var prereqs = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selected)
                foreach (string p in Strings(setMap[id].GetExact("prerequisites"), "set-prerequisites"))
                    if (prereqs.Add(p)) queue.Enqueue(p);
            while (queue.Count > 0)
                foreach (string p in Strings(prereqMap[queue.Dequeue()].GetExact("dependsOn"), "prerequisite-depends"))
                    if (prereqs.Add(p)) queue.Enqueue(p);

            var removed = new List<object>();
            var selectedSets = new List<PsObject>();
            foreach (string id in PsSort.Ordinal(selected).Cast<string>())
            {
                PsObject set = setMap[id];
                var surviving = new List<PsObject>();
                foreach (PsObject member in List(set.GetExact("members"), "set-members").Cast<PsObject>())
                {
                    string memberSource = OptionalString(member.GetExact("source"), "member-source");
                    if (!string.IsNullOrEmpty(memberSource) && !candidatePaths.Contains(memberSource))
                        removed.Add(ReleaseSelection.Obj(("set", id), ("member", member), ("reason", "source-removed")));
                    else surviving.Add(member);
                }
                var copy = new PsObject();
                foreach (var pair in set.Pairs) if (pair.Key != "members") copy.Pairs.Add(pair);
                copy.Pairs.Add(new KeyValuePair<string, object>("members", PsSort.CultureCaseSensitive(surviving, m => PsJson.SelectionCanonical(m)).Cast<object>().ToList()));
                selectedSets.Add(copy);
            }

            string roots = ReleaseSelection.Git(root, new[] { "rev-list", "--max-parents=0", candidateSha, "--" }, new[] { 0 }).Stdout;
            string[] blobIds = ReleaseSelection.Git(root, new[] { "rev-parse", candidateSha + ":" + PolicyPath, candidateSha + ":" + CatalogPath }, new[] { 0 })
                .Stdout.Trim().Split('\n').Select(line => line.Trim()).ToArray();
            if (blobIds.Length != 2 || !ReleaseSelection.FullSha.IsMatch(blobIds[0]) || !ReleaseSelection.FullSha.IsMatch(blobIds[1])) throw new NotHandledException("git-read-failed:rev-parse:shape");

            var obligations = new List<object>();
            foreach (PsObject set in selectedSets)
            {
                var platforms = Strings(set.GetExact("platforms"), "set-platforms");
                if ((string)set.GetExact("eligibility") != "unattended" || !platforms.Contains("any"))
                    obligations.Add(ReleaseSelection.Obj(("set", set.GetExact("id")), ("eligibility", set.GetExact("eligibility")), ("platforms", set.GetExact("platforms")), ("reason", set.GetExact("reason"))));
            }
            var result = ReleaseSelection.Obj(
                ("format", "antiphon-test-selection-v1"), ("repositoryIdentity", ReleaseSelection.SelectionDigest(PsSort.Ordinal(roots.Trim().Split('\n')))),
                ("baselineSha", baselineSha), ("candidateSha", candidateSha),
                ("policyBlob", blobIds[0]), ("policyHash", policyHash), ("catalogBlob", blobIds[1]), ("catalogHash", catalogHash),
                ("selectorVersion", 1), ("selectorHash", ReleaseSelection.SelectionDigest(selectorBytes)), ("profile", profile),
                ("mode", fallback.Count > 0 ? "full" : "narrow"), ("fallbackReasons", PsSort.Ordinal(fallback)), ("changedPaths", records),
                ("selectedSets", selectedSets.Cast<object>().ToList()),
                ("unselectedSets", PsSort.Ordinal(setMap.Keys.Where(id => !selected.Contains(id)))),
                ("prerequisites", PsSort.Ordinal(prereqs)),
                ("explanationEdges", PsSort.CultureCaseSensitive(edges, e => PsJson.SelectionCanonical(e)).Cast<object>().ToList()),
                ("unresolvedObligations", obligations), ("removedMembers", removed), ("executionEvidence", false), ("activation", "unqualified"));
            result.Pairs.Add(new KeyValuePair<string, object>("digest", ReleaseSelection.SelectionDigest(result)));
            return result;
        }

        /// <summary>Test-NightlyProjectDispositions, on the shapes whose PowerShell reading is certain.</summary>
        internal static void ProjectDispositions(PsObject policy)
        {
            var rows = List(policy.Get("projectDispositions"), "dispositions-shape");
            if (!(policy.Get("suites") is PsObject suites)) throw new NotHandledException("dispositions-suites");
            var suiteProjects = new List<string>();
            foreach (var pair in suites.Pairs)
            {
                if (!(pair.Value is PsObject suite)) throw new NotHandledException("dispositions-suite");
                string project = OptionalString(suite.Get("project"), "dispositions-suite-project");
                if (!string.IsNullOrEmpty(project)) suiteProjects.Add(project);
            }
            int primary = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in rows)
            {
                if (!(item is PsObject row) || !(row.Get("role") is string role) || role != "primary" && role != "child") throw new NotHandledException("dispositions-role");
                if (role == "primary") primary++;
                if (!(row.Get("project") is string path) || !ProjectFile.IsMatch(path) || path.StartsWith("/", StringComparison.CurrentCulture)
                    || path.Split('/').Contains("..") || !seen.Add(path)) throw new NotHandledException("dispositions-project");
                if (!(row.Get("reason") is string reason) || string.IsNullOrWhiteSpace(reason)) throw new NotHandledException("dispositions-reason");
                if (role == "primary") { if (!suiteProjects.Contains(path)) throw new NotHandledException("dispositions-primary"); }
                else
                {
                    var consumers = Strings(row.Get("consumers"), "dispositions-consumers");
                    if (consumers.Count == 0 || consumers.Any(c => !suiteProjects.Contains(c))) throw new NotHandledException("dispositions-consumers");
                }
            }
            if (primary != suiteProjects.Count) throw new NotHandledException("dispositions-census");
        }
    }
}
