// Get-RcProjectSelection (scripts/lib/rc-project-selection.ps1): the project graph at B and at A read from the
// committed MSBuild XML (never evaluated), the projects a change owns and everything downstream of them. The
// caller has asserted both commits and that B is an ancestor of A. A graph diagnostic the PowerShell builds from
// its own throw text is reproduced; one carrying a .NET exception's message (invalid XML, a regex timeout) is
// NotHandled, since that text belongs to the runtime the worker happens to run on.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Antiphon.Checkpoints;

namespace Antiphon.Scripts
{
    internal static class RcProjectSelection
    {
        private const string CategoryPath = "tests/rc-project-categories.json";
        private static readonly Regex BlobHeader = new Regex("^100(?:644|755) blob ([0-9a-f]{40}) +([0-9]+)$", RegexOptions.CultureInvariant);
        private static readonly Regex Category = new Regex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex ProjectPath = new Regex(@"\.csproj$", RegexOptions.CultureInvariant);
        private static readonly Regex Status = new Regex("^[ADMTUXB]$", RegexOptions.CultureInvariant);
        private static readonly Regex LastSegment = new Regex("[^/]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Unresolved = new Regex("[$@%;:]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Wildcard = new Regex(@"[*?\[]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PackageProperty = new Regex(@"^\$\(Pkg[A-Za-z0-9_]+\)[\\/]", RegexOptions.CultureInvariant);
        private static readonly Regex SharedInput = new Regex(@"(^scripts/|\.(props|targets|sln|slnx)$|(^|/)(global\.json|NuGet\.Config|nuget\.config)$|^tests/[^/]*policy[^/]*\.json$|^tests/rc-project-categories\.json$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A throw "text" inside the per-input try of Read-RcProjectGraph: its message is the text.</summary>
        private sealed class GraphFault : Exception { public GraphFault(string message) : base(message) { } }

        private sealed class Project
        {
            public string Path;
            public List<object> Categories;
            public List<string> References = new List<string>();
            public List<string> Inputs = new List<string>();
            public List<string> Downstream = new List<string>();
            public bool HasTests;
            public string BatchDisposition = "unclassified";

            public PsObject Row() => ReleaseSelection.Obj(("path", Path), ("categories", Categories), ("references", References.Cast<object>().ToList()),
                ("inputs", Inputs.Cast<object>().ToList()), ("downstream", Downstream.Cast<object>().ToList()), ("hasTests", HasTests), ("batchDisposition", BatchDisposition));
        }

        private sealed class Graph
        {
            public string Sha;
            public Dictionary<string, Project> Projects = new Dictionary<string, Project>(StringComparer.Ordinal);
            public List<object> Batches;
            public string InventoryBlob;
            public List<string> Reasons;
        }

        internal static PsObject Select(string root, string baselineSha, string candidateSha, string scriptsLib)
        {
            if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName is "tr" or "az") throw new NotHandledException("culture-casing");
            var documents = new Dictionary<string, XmlDocument>(StringComparer.Ordinal);
            Graph old = ReadGraph(root, baselineSha, documents);
            Graph @new = ReadGraph(root, candidateSha, documents);
            var reasons = new List<string>(old.Reasons.Concat(@new.Reasons));
            // The attempt asserted merge-base --is-ancestor B A: baseline-not-ancestor cannot arise here.
            string[] diff = ReleaseSelection.Git(root, new[] { "diff", "--no-ext-diff", "--no-renames", "--name-status", "-z", baselineSha, candidateSha, "--" }, new[] { 0 }).Stdout.Split('\0');
            var changes = new List<object>();
            var changed = new List<string>();
            var affected = new List<string>();
            for (int i = 0; i < diff.Length - 1; i += 2)
            {
                string status = diff[i], path = diff[i + 1];
                if (!Status.IsMatch(status) || path.Length == 0) throw new NotHandledException("malformed-project-diff");
                changes.Add(ReleaseSelection.Obj(("path", path), ("status", status)));
                if (SharedInput.IsMatch(path)) reasons.Add("shared-input:" + path);
                if (status != "A" && status != "D" && status != "M") reasons.Add("uncertain-change:" + path);
                var owners = new List<string>();
                Graph[] graphs = status == "A" ? new[] { @new } : status == "D" ? new[] { old } : new[] { old, @new };
                foreach (Graph graph in graphs)
                {
                    var local = new List<string>();
                    foreach (Project project in graph.Projects.Values)
                    {
                        string directory = LastSegment.Replace(project.Path, "");
                        if (path.StartsWith(directory, StringComparison.Ordinal) || project.Inputs.Contains(path)) local.Add(project.Path);
                    }
                    if (local.Count == 0) reasons.Add("unknown-path:" + path);
                    owners.AddRange(local);
                    affected.AddRange(Downstream(graph, local));
                }
                changed.AddRange(owners);
            }
            var sortedReasons = PsSort.Ordinal(reasons);
            if (sortedReasons.Count > 0) affected = @new.Projects.Keys.ToList();
            var batchRows = new List<object>();
            foreach (string path in @new.Batches.Cast<string>())
                if (@new.Projects.ContainsKey(path) && affected.Contains(path))
                    batchRows.Add(ReleaseSelection.Obj(("id", path), ("project", path), ("categories", @new.Projects[path].Categories), ("membership", "whole-project")));
            var graphRows = new List<object>();
            foreach (Graph graph in new[] { old, @new })
            {
                var rows = PsSort.CultureCaseSensitive(graph.Projects.Values, p => p.Path).Select(p => (object)p.Row()).ToList();
                graphRows.Add(ReleaseSelection.Obj(("sha", graph.Sha), ("inventoryBlob", graph.InventoryBlob), ("projects", rows)));
            }
            string planner = File.ReadAllText(Path.Combine(scriptsLib, "rc-project-selection.ps1"));
            var result = ReleaseSelection.Obj(("format", "antiphon-rc-project-selection-v1"), ("baselineSha", baselineSha), ("candidateSha", candidateSha),
                ("plannerVersion", 1), ("plannerHash", ReleaseSelection.SelectionDigest(planner)),
                ("mode", sortedReasons.Count > 0 ? "Full" : "Scoped"), ("effectiveMode", "Full"), ("coverageScope", "Full"),
                ("activation", "shadow-only"), ("executionEvidence", false), ("gateCredit", false),
                ("changedPaths", changes), ("changedProjects", PsSort.Ordinal(changed)), ("affectedProjects", PsSort.Ordinal(affected)),
                ("categories", PsSort.Ordinal(@new.Projects.Values.Where(p => affected.Contains(p.Path)).SelectMany(p => p.Categories.Cast<string>()))),
                ("batches", batchRows), ("graphs", graphRows), ("fallbackReasons", sortedReasons));
            result.Pairs.Add(new KeyValuePair<string, object>("digest", ReleaseSelection.SelectionDigest(result)));
            return result;
        }

        /// <summary>Get-RcDownstream: the seeds and every project that references one of them, transitively.</summary>
        private static List<string> Downstream(Graph graph, IEnumerable<string> seeds)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (string seed in seeds) if (seen.Add(seed)) queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (Project project in graph.Projects.Values)
                    if (project.References.Contains(current) && seen.Add(project.Path)) queue.Enqueue(project.Path);
            }
            return PsSort.Ordinal(seen).Cast<string>().ToList();
        }

        /// <summary>Resolve-RcProjectInput.</summary>
        private static string Resolve(string from, string include, string project, bool allowWildcard)
        {
            string directory = LastSegment.Replace(project, "");
            string value = include.Replace("\\", "/");
            const string ThisDirectory = "$(MSBuildThisFileDirectory)";
            if (value.StartsWith(ThisDirectory, StringComparison.Ordinal))
            {
                directory = LastSegment.Replace(from, "");
                value = value.Substring(ThisDirectory.Length);
            }
            if (value.Length == 0 || Unresolved.IsMatch(value) || (!allowWildcard && Wildcard.IsMatch(value)) || value.StartsWith("/", StringComparison.CurrentCulture))
                throw new GraphFault("unresolved-input:" + from);
            var parts = new List<string>();
            foreach (string part in (directory + value).Split('/'))
            {
                if (part == "..")
                {
                    if (parts.Count == 0) throw new GraphFault("outside-input:" + from);
                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part.Length > 0 && part != ".") parts.Add(part);
            }
            return string.Join("/", parts);
        }

        private static Graph ReadGraph(string root, string sha, Dictionary<string, XmlDocument> documents)
        {
            var graph = new Graph { Sha = sha };
            var reasons = new List<string>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
            var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
            // ls-tree -r -l -z: the -r -z listing Read-RcProjectGraph parses, plus each blob's size for one batched read.
            string listing = ReleaseSelection.Git(root, new[] { "ls-tree", "-r", "-l", "-z", sha, "--" }, new[] { 0 }).Stdout;
            foreach (string entry in listing.Split('\0'))
            {
                if (entry.Length == 0) continue;
                int tab = entry.IndexOf('\t');
                if (tab < 0) throw new NotHandledException("malformed-project-tree");
                string header = entry.Substring(0, tab), path = entry.Substring(tab + 1);
                paths.Add(path);
                Match blob = BlobHeader.Match(header);
                if (blob.Success) { blobs[path] = blob.Groups[1].Value; sizes[blob.Groups[1].Value] = long.Parse(blob.Groups[2].Value, CultureInfo.InvariantCulture); }
            }

            PsObject catalog = null;
            if (!blobs.ContainsKey(CategoryPath)) reasons.Add("category-inventory-invalid");
            else
            {
                object parsed = PsJson.Parse(ReadBlobs(root, new[] { blobs[CategoryPath] }, sizes)[blobs[CategoryPath]]);
                if (!(parsed is PsObject document)) throw new NotHandledException("category-inventory-shape");
                object schema = document.GetExact("schemaVersion");
                if (schema != null && !(schema is long)) throw new NotHandledException("category-inventory-schema");
                if (schema is long version && version == 1 && document.GetExact("projects") is PsObject && document.GetExact("batches") is List<object>) catalog = document;
                else reasons.Add("category-inventory-invalid");
            }
            var categoryProjects = catalog == null ? new PsObject() : (PsObject)catalog.GetExact("projects");
            var catalogBatches = catalog == null ? new List<object>() : (List<object>)catalog.GetExact("batches");
            object ProjectEntry(string key) { foreach (var pair in categoryProjects.Pairs) if (pair.Key == key) return pair.Value; return null; }

            foreach (string path in PsSort.Ordinal(paths.Where(p => ProjectPath.IsMatch(p))).Cast<string>())
            {
                // @($catalog.projects[$path]): null is one null item, a list its items, anything else one item.
                object value = ProjectEntry(path);
                var items = value is List<object> list ? list : new List<object> { value };
                List<object> categories;
                if (items.Count == 0 || items.Any(c => !(c is string s) || !Category.IsMatch(s))) { reasons.Add("unknown-project:" + path); categories = new List<object> { "unknown" }; }
                else categories = PsSort.Ordinal(items.Cast<string>());
                graph.Projects[path] = new Project { Path = path, Categories = categories };
            }
            foreach (var pair in categoryProjects.Pairs) if (!graph.Projects.ContainsKey(pair.Key)) reasons.Add("stale-project:" + pair.Key);
            foreach (object batch in catalogBatches)
            {
                if (!(batch is string name)) throw new NotHandledException("category-batch-shape");
                if (!graph.Projects.ContainsKey(name)) reasons.Add("unknown-batch:" + name);
            }

            // The start inputs of every project (the project and its ancestors' Directory.Build files), read together.
            var starts = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Project project in graph.Projects.Values) foreach (string input in StartInputs(project.Path, paths)) starts.Add(input);
            var wanted = starts.Where(blobs.ContainsKey).Select(p => blobs[p]).Where(b => !documents.ContainsKey(b)).Distinct().ToList();
            var texts = ReadBlobs(root, wanted, sizes);
            XmlDocument Document(string input)
            {
                string blob = blobs[input];
                if (documents.TryGetValue(blob, out XmlDocument known)) return known;
                if (!texts.TryGetValue(blob, out string text)) text = ReadBlobs(root, new[] { blob }, sizes)[blob];
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                var document = new XmlDocument { XmlResolver = null };
                try { using (var reader = XmlReader.Create(new StringReader(text), settings)) document.Load(reader); }
                catch (Exception e) { throw new NotHandledException("project-xml-invalid:" + e.GetType().Name); }
                documents[blob] = document;
                return document;
            }

            string[] sortedPaths = null;
            foreach (Project project in graph.Projects.Values)
            {
                var queue = new Queue<string>(StartInputs(project.Path, paths));
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var references = new List<string>();
                var inputs = new List<string>();
                while (queue.Count > 0)
                {
                    string input = queue.Dequeue();
                    if (!visited.Add(input)) continue;
                    try
                    {
                        if (!blobs.ContainsKey(input)) throw new GraphFault("missing-input:" + input);
                        XmlDocument document = Document(input);
                        foreach (XmlElement node in Elements(document, "//*[local-name()='ProjectReference']"))
                        {
                            string target = Resolve(input, node.GetAttribute("Include"), project.Path, false);
                            if (!graph.Projects.ContainsKey(target)) throw new GraphFault("missing-reference:" + input);
                            references.Add(target);
                        }
                        foreach (XmlElement node in Elements(document, "//*[local-name()='Import']"))
                            queue.Enqueue(Resolve(input, node.GetAttribute("Project"), input, false));
                        foreach (XmlElement node in Elements(document, "//*[local-name()='PackageReference' and @Include]"))
                        {
                            string include = node.GetAttribute("Include");
                            bool tunit = string.Equals(include, "TUnit", StringComparison.OrdinalIgnoreCase);
                            if (!tunit && (string.Compare(include, "TUnit", CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0
                                || string.Compare(include, "TUnit", CultureInfo.CurrentCulture, CompareOptions.IgnoreCase) == 0)) throw new NotHandledException("package-name-casing");
                            if (tunit) project.HasTests = true;
                        }
                        foreach (XmlElement node in Elements(document, "//*[@Include and (local-name()='Compile' or local-name()='Content' or local-name()='None' or local-name()='EmbeddedResource' or local-name()='AdditionalFiles') and not(ancestor::*[local-name()='Target'])]"))
                        {
                            string include = node.GetAttribute("Include");
                            if (PackageProperty.IsMatch(include)) continue;
                            string target = Resolve(input, include, project.Path, true);
                            int wild = target.IndexOfAny(new[] { '*', '?' });
                            if (wild >= 0)
                            {
                                string expression = "^" + Regex.Escape(target).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                                var pattern = new Regex(expression, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
                                if (sortedPaths == null) { sortedPaths = paths.ToArray(); Array.Sort(sortedPaths, StringComparer.Ordinal); }
                                string prefix = target.Substring(0, wild);
                                int at = Array.BinarySearch(sortedPaths, prefix, StringComparer.Ordinal);
                                if (at < 0) at = ~at;
                                try
                                {
                                    for (; at < sortedPaths.Length && sortedPaths[at].StartsWith(prefix, StringComparison.Ordinal); at++)
                                        if (pattern.IsMatch(sortedPaths[at])) inputs.Add(sortedPaths[at]);
                                }
                                catch (RegexMatchTimeoutException) { throw new NotHandledException("glob-timeout"); }
                            }
                            else
                            {
                                if (node.LocalName == "Compile" && !paths.Contains(target)) throw new GraphFault("missing-linked-input:" + input);
                                inputs.Add(target);
                            }
                        }
                    }
                    catch (GraphFault fault) { reasons.Add("graph:" + input + ":" + fault.Message); }
                }
                project.References = PsSort.Ordinal(references).Cast<string>().ToList();
                project.Inputs = PsSort.Ordinal(inputs).Cast<string>().ToList();
            }

            object exemptionValue = catalog?.GetExact("nonQualificationTests");
            PsObject exemptions = new PsObject();
            if (exemptionValue is PsObject given) exemptions = given;
            else if (exemptionValue != null) reasons.Add("invalid-test-dispositions");
            foreach (var pair in exemptions.Pairs)
                if (!graph.Projects.ContainsKey(pair.Key) || !(pair.Value is string text) || string.IsNullOrWhiteSpace(text)) reasons.Add("invalid-test-disposition:" + pair.Key);
            var batches = catalogBatches.Cast<string>().ToList();
            foreach (Project project in graph.Projects.Values)
            {
                if (batches.Contains(project.Path)) project.BatchDisposition = "whole-project";
                else if (exemptions.Pairs.Any(p => p.Key == project.Path)) project.BatchDisposition = "harness-only";
                else if (project.HasTests) { reasons.Add("unbatched-test-project:" + project.Path); batches.Add(project.Path); }
                project.Downstream = Downstream(graph, new[] { project.Path }).Where(p => p != project.Path).ToList();
                foreach (string dependency in project.References)
                    if (dependency == project.Path || project.Downstream.Contains(dependency)) reasons.Add("project-cycle:" + project.Path);
            }
            graph.Batches = PsSort.Ordinal(batches);
            graph.InventoryBlob = blobs.TryGetValue(CategoryPath, out string inventory) ? inventory : "";
            graph.Reasons = PsSort.Ordinal(reasons).Cast<string>().ToList();
            return graph;
        }

        private static IEnumerable<XmlElement> Elements(XmlDocument document, string xpath)
        {
            foreach (XmlNode node in document.SelectNodes(xpath))
            {
                if (!(node is XmlElement element)) throw new NotHandledException("project-xml-node");
                yield return element;
            }
        }

        /// <summary>The project file, then Directory.Build.props/.targets of each ancestor that the tree holds.</summary>
        private static List<string> StartInputs(string projectPath, HashSet<string> paths)
        {
            var inputs = new List<string> { projectPath };
            string ancestor = Regex.Replace(projectPath, "/[^/]+$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            while (true)
            {
                foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets" })
                {
                    string input = ancestor.Length > 0 ? ancestor + "/" + name : name;
                    if (paths.Contains(input)) inputs.Add(input);
                }
                if (ancestor.Length == 0) break;
                ancestor = ancestor.Contains('/') ? Regex.Replace(ancestor, "/[^/]+$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : "";
            }
            return inputs;
        }

        /// <summary>Blob texts by id in one git show per hundred ids, split by the sizes ls-tree -l gave.</summary>
        private static Dictionary<string, string> ReadBlobs(string root, IReadOnlyList<string> ids, Dictionary<string, long> sizes)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int at = 0; at < ids.Count; at += 100)
            {
                var chunk = ids.Skip(at).Take(100).ToList();
                var start = new ProcessStartInfo("git") { WorkingDirectory = Path.GetFullPath(root) };
                foreach (string argument in new[] { "show", "--no-textconv", "--no-color" }) start.ArgumentList.Add(argument);
                foreach (string id in chunk) start.ArgumentList.Add(id);
                CheckpointProcessRunner.BytesResult shown;
                try { shown = CheckpointProcessRunner.RunBytes(start, 30000, true); }
                catch (Exception e) when (!(e is NotHandledException)) { throw new NotHandledException("git-failed:" + e.GetType().Name); }
                long total = chunk.Sum(id => sizes[id]);
                if (shown.TimedOut || shown.Exit != 0 || shown.Stdout.LongLength != total || total > 16777216) throw new NotHandledException("git-read-failed:show-blobs");
                int offset = 0;
                foreach (string id in chunk)
                {
                    int length = (int)sizes[id];
                    texts[id] = ReleaseSelection.Decode(shown.Stdout, offset, length);
                    offset += length;
                }
            }
            return texts;
        }
    }
}
