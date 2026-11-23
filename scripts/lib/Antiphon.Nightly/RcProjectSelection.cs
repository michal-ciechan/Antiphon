// CARD-1545: the rc project planner - advisory project batches, never execution or qualification authority. The
// 2026-11-22 plan (docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, unit 8) ported it from
// scripts/lib/rc-project-selection.ps1 (Get-RcProjectSelection, Read-RcProjectGraph, Resolve-RcProjectInput,
// Get-RcDownstream, Read-RcProjectBlob) and made scripts/select-rc-project-batches.ps1 the select-rc-project-batches
// command (Run); both scripts are deleted, and so is the CLI's own copy of the planner (Antiphon.Scripts/RcProjectSelection.cs).
//
// The project graph of a commit is read from its committed MSBuild XML, never evaluated: each project's references,
// literal imports, ancestor Directory.Build.props/.targets and the files its items name (globs matched against the
// commit's tree), with the categories and batches of tests/rc-project-categories.json. A selection maps each changed path
// (no rename inference) to its owning projects at B and A and closes over everything downstream. Any doubt - an unknown
// path, a shared build input, a graph diagnostic, an invalid inventory, a cycle, an unbatched test project, B not an
// ancestor of A - proposes Full (every project affected). Callers: the release selection attempt (ReleaseSelectionAttempt,
// a non-Full mode with a baseline), the live-lane fingerprint (scripts/lib/live-lane-fingerprint.ps1 reads Read, Resolve and
// BlobText from the loaded core) and the select-rc-project-batches command.
//
// The document cache is the caller's dictionary keyed by git blob id (immutable bytes): a parsed XmlDocument, or the
// exception an unparseable blob gave (kept so every import site reports it without a second read), and "text:<blob>" for
// a blob's text (BlobText). plannerHash is the digest of the planner's identity (format and PlannerVersion; version 2 is
// this C# planner): bump PlannerVersion when its answer changes. Plain C#. ASCII only.
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
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>The select-rc-project-batches command's options (the parameters of the deleted select-rc-project-batches.ps1).</summary>
    public sealed class SelectRcProjectBatchesOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "select-rc-project-batches --baseline-sha <sha> --candidate-sha <sha> [--repository-root <dir>] [--output-path <file>]";

        private static readonly string[] Valued = { "--baseline-sha", "--candidate-sha", "--repository-root", "--output-path" };

        /// <summary>The baseline commit B (a full commit id).</summary>
        public string BaselineSha { get; init; } = "";

        /// <summary>The candidate commit A (a full commit id).</summary>
        public string CandidateSha { get; init; } = "";

        /// <summary>The repository the commits are read from.</summary>
        public string RepositoryRoot { get; init; } = "";

        /// <summary>A file to create with the selection's JSON (never overwritten); empty for none.</summary>
        public string OutputPath { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or a missing commit.</summary>
        public static SelectRcProjectBatchesOptions Parse(IReadOnlyList<string> arguments, string defaultRepositoryRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            return new SelectRcProjectBatchesOptions
            {
                BaselineSha = flags.Required("--baseline-sha"), CandidateSha = flags.Required("--candidate-sha"),
                RepositoryRoot = flags.Value("--repository-root", defaultRepositoryRoot ?? ""), OutputPath = flags.Value("--output-path", ""),
            };
        }
    }

    /// <summary>One project of a commit's graph (a row of the selection's graphs).</summary>
    public sealed class RcProject
    {
        public string Path { get; init; } = "";
        public string[] Categories { get; init; } = Array.Empty<string>();
        public string[] References { get; set; } = Array.Empty<string>();
        public string[] Inputs { get; set; } = Array.Empty<string>();
        public string[] Downstream { get; set; } = Array.Empty<string>();
        public bool HasTests { get; set; }

        /// <summary>whole-project (a catalog batch), harness-only (a reasoned non-qualification test project) or unclassified.</summary>
        public string BatchDisposition { get; set; } = "unclassified";

        /// <summary>The project as the selection's graphs list it.</summary>
        public OrderedDictionary Row() => RcProjectSelection.Ordered(("path", Path), ("categories", Categories), ("references", References),
            ("inputs", Inputs), ("downstream", Downstream), ("hasTests", HasTests), ("batchDisposition", BatchDisposition));
    }

    /// <summary>Read-RcProjectGraph's answer: the projects of one commit, its tree's paths and blob ids, the batches and the diagnostics.</summary>
    public sealed class RcProjectGraph
    {
        public string Sha { get; init; } = "";

        /// <summary>The projects by path, in ordinal path order.</summary>
        public Dictionary<string, RcProject> Projects { get; } = new Dictionary<string, RcProject>(StringComparer.Ordinal);

        /// <summary>Every path of the commit's tree.</summary>
        public HashSet<string> Paths { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The blob id of each regular file of the tree.</summary>
        public Dictionary<string, string> Blobs { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The batched projects (the catalog's, plus each unbatched test project), ordinal.</summary>
        public string[] Batches { get; set; } = Array.Empty<string>();

        /// <summary>The category inventory's blob id ("" when the commit has none).</summary>
        public string InventoryBlob { get; set; } = "";

        /// <summary>The graph's diagnostics, ordinal and distinct; any one proposes Full.</summary>
        public string[] FallbackReasons { get; set; } = Array.Empty<string>();
    }

    /// <summary>Get-RcProjectSelection, Read-RcProjectGraph and the select-rc-project-batches command.</summary>
    public static class RcProjectSelection
    {
        /// <summary>The committed category inventory.</summary>
        public const string CategoryPath = "tests/rc-project-categories.json";

        /// <summary>The planner's version (1 was the PowerShell planner).</summary>
        public const int PlannerVersion = 2;

        /// <summary>The selection document's format.</summary>
        public const string Format = "antiphon-rc-project-selection-v1";

        /// <summary>The digest of the planner's identity.</summary>
        public static string PlannerHash => TestSelectionJson.Digest(Format + "/planner/" + PlannerVersion.ToString(CultureInfo.InvariantCulture));

        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex TreeEntry = new Regex("^100(?:644|755) blob ([0-9a-f]{40})$", RegexOptions.CultureInvariant);
        private static readonly Regex Category = new Regex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex Status = new Regex("^[ADMTUXB]$", RegexOptions.CultureInvariant);
        private static readonly Regex Unresolved = new Regex("[$@%;:]", RegexOptions.CultureInvariant);
        private static readonly Regex Wildcard = new Regex(@"[*?\[]", RegexOptions.CultureInvariant);
        private static readonly Regex PackageProperty = new Regex(@"^\$\(Pkg[A-Za-z0-9_]+\)[\\/]", RegexOptions.CultureInvariant);
        private static readonly Regex SharedInput = new Regex(@"(^scripts/|\.(props|targets|sln|slnx)$|(^|/)(global\.json|NuGet\.Config|nuget\.config)$|^tests/[^/]*policy[^/]*\.json$|^tests/rc-project-categories\.json$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private const string ThisFileDirectory = "$(MSBuildThisFileDirectory)";

        /// <summary>A graph input's own diagnostic (the text after "graph:&lt;input&gt;:").</summary>
        public sealed class GraphFault : Exception
        {
            public GraphFault(string message) : base(message) { }
        }

        /// <summary>
        /// The select-rc-project-batches command: the selection of options.BaselineSha and options.CandidateSha, written once
        /// to OutputPath when given (its canonical JSON and a line end; an existing file is never replaced), then its canonical
        /// JSON on host.Out. Exit 0; 2 refused ("project selection refused: &lt;reason&gt;" on host.Error).
        /// </summary>
        public static int Run(SelectRcProjectBatchesOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                var text = TestSelectionJson.Canonical(Select(options.RepositoryRoot, options.BaselineSha, options.CandidateSha));
                if (!string.IsNullOrEmpty(options.OutputPath))
                {
                    using var stream = new FileStream(options.OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var bytes = new UTF8Encoding(false).GetBytes(text + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                host.Out.WriteLine(text);
                return 0;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                host.Error.WriteLine("project selection refused: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 2;
            }
        }

        /// <summary>
        /// Get-RcProjectSelection: the antiphon-rc-project-selection-v1 proposal for baseline and candidate (full commit ids
        /// of commit objects; full-commit-required otherwise), its digest last. readBatch (null: TestSelectionGit.ReadBlobBatch)
        /// reads the start inputs of each graph together; a blob it leaves out is read by itself.
        /// </summary>
        public static OrderedDictionary Select(string repositoryRoot, string baselineSha, string candidateSha,
            Func<string, string, string[], Dictionary<string, string>> readBatch = null)
        {
            foreach (var sha in new[] { baselineSha, candidateSha })
            {
                if (sha == null || !FullSha.IsMatch(sha)) throw new NightlyRefusal("full-commit-required");
                var type = TestSelectionGit.Run(repositoryRoot, new[] { "cat-file", "-t", sha }, new[] { 0, 128 });
                if (type.Exit != 0 || type.Stdout.Trim() != "commit") throw new NightlyRefusal("full-commit-required");
            }
            var documents = new Hashtable(StringComparer.Ordinal);
            var old = Read(repositoryRoot, baselineSha, documents, null, readBatch);
            var @new = Read(repositoryRoot, candidateSha, documents, null, readBatch);
            var reasons = new List<string>(old.FallbackReasons.Concat(@new.FallbackReasons));
            if (TestSelectionGit.Run(repositoryRoot, new[] { "merge-base", "--is-ancestor", baselineSha, candidateSha }, new[] { 0, 1 }).Exit != 0) reasons.Add("baseline-not-ancestor");
            // No rename inference: both the removed and the added ownership take part.
            var diff = TestSelectionGit.Run(repositoryRoot, new[] { "diff", "--no-ext-diff", "--no-renames", "--name-status", "-z", baselineSha, candidateSha, "--" }).Stdout.Split('\0');
            var changes = new List<object>();
            var changed = new List<string>();
            var affected = new List<string>();
            for (var i = 0; i < diff.Length - 1; i += 2)
            {
                string status = diff[i], path = diff[i + 1];
                if (!Status.IsMatch(status) || path.Length == 0) throw new NightlyRefusal("malformed-project-diff");
                changes.Add(Ordered(("path", path), ("status", status)));
                if (SharedInput.IsMatch(path)) reasons.Add("shared-input:" + path);
                if (status != "A" && status != "D" && status != "M") reasons.Add("uncertain-change:" + path);
                var graphs = status == "A" ? new[] { @new } : status == "D" ? new[] { old } : new[] { old, @new };
                foreach (var graph in graphs)
                {
                    var local = new List<string>();
                    foreach (var project in graph.Projects.Values)
                        if (path.StartsWith(Directory(project.Path), StringComparison.Ordinal) || project.Inputs.Contains(path, StringComparer.Ordinal)) local.Add(project.Path);
                    if (local.Count == 0) reasons.Add("unknown-path:" + path);
                    changed.AddRange(local);
                    affected.AddRange(Downstream(graph, local));
                }
            }
            var sortedReasons = TestSelectionJson.Sorted(reasons.ToArray());
            if (sortedReasons.Length > 0) affected = @new.Projects.Keys.ToList();
            var affectedSet = new HashSet<string>(affected, StringComparer.Ordinal);
            var batchRows = new List<object>();
            foreach (var path in TestSelectionJson.Sorted(@new.Batches))
                if (@new.Projects.TryGetValue(path, out var batched) && affectedSet.Contains(path))
                    batchRows.Add(Ordered(("id", path), ("project", path), ("categories", batched.Categories), ("membership", "whole-project")));
            var graphRows = new List<object>();
            var culture = CultureInfo.CurrentCulture.CompareInfo;
            foreach (var graph in new[] { old, @new })
            {
                // Sort-Object path -CaseSensitive: the current culture, case-sensitive (ordinal between culture-equal paths).
                var rows = graph.Projects.Values.OrderBy(p => p.Path, Comparer<string>.Create((x, y) =>
                {
                    var c = culture.Compare(x, y, CompareOptions.None);
                    return c != 0 ? c : string.CompareOrdinal(x, y);
                })).Select(p => (object)p.Row()).ToArray();
                graphRows.Add(Ordered(("sha", graph.Sha), ("inventoryBlob", graph.InventoryBlob), ("projects", rows)));
            }
            var result = Ordered(("format", Format), ("baselineSha", baselineSha), ("candidateSha", candidateSha),
                ("plannerVersion", PlannerVersion), ("plannerHash", PlannerHash),
                ("mode", sortedReasons.Length > 0 ? "Full" : "Scoped"), ("effectiveMode", "Full"), ("coverageScope", "Full"),
                ("activation", "shadow-only"), ("executionEvidence", false), ("gateCredit", false),
                ("changedPaths", changes.ToArray()), ("changedProjects", TestSelectionJson.Sorted(changed.ToArray())),
                ("affectedProjects", TestSelectionJson.Sorted(affected.ToArray())),
                ("categories", TestSelectionJson.Sorted(@new.Projects.Values.Where(p => affectedSet.Contains(p.Path)).SelectMany(p => p.Categories).ToArray())),
                ("batches", batchRows.ToArray()), ("graphs", graphRows.ToArray()), ("fallbackReasons", sortedReasons));
            result["digest"] = TestSelectionJson.Digest(result);
            return result;
        }

        /// <summary>
        /// Read-RcProjectGraph: the project graph of a commit. documents is the parsed-document cache (null: one for this
        /// read only); listing is this commit's own `ls-tree -r -z` output when the caller already read it (null or empty:
        /// read here); readBatch as Select's.
        /// </summary>
        public static RcProjectGraph Read(string repositoryRoot, string sha, IDictionary documents = null, string listing = null,
            Func<string, string, string[], Dictionary<string, string>> readBatch = null)
        {
            documents ??= new Hashtable(StringComparer.Ordinal);
            readBatch ??= TestSelectionGit.ReadBlobBatch;
            var graph = new RcProjectGraph { Sha = sha };
            var reasons = new List<string>();
            // Empty is not given (PowerShell passes $null to a string parameter as ""); an empty tree reads empty again.
            var text = string.IsNullOrEmpty(listing) ? TestSelectionGit.Run(repositoryRoot, new[] { "ls-tree", "-r", "-z", sha, "--" }).Stdout : listing;
            foreach (var entry in text.Split('\0'))
            {
                if (entry.Length == 0) continue;
                var tab = entry.IndexOf('\t');
                if (tab < 0) throw new NightlyRefusal("malformed-project-tree");
                var path = entry.Substring(tab + 1);
                graph.Paths.Add(path);
                var blob = TreeEntry.Match(entry.Substring(0, tab));
                if (blob.Success) graph.Blobs[path] = blob.Groups[1].Value;
            }

            IDictionary catalogProjects = new OrderedDictionary(StringComparer.Ordinal);
            object[] catalogBatches = Array.Empty<object>();
            object exemptionValue = null;
            try
            {
                if (!graph.Blobs.TryGetValue(CategoryPath, out var inventory)) throw new NightlyRefusal("category-inventory-absent");
                var catalog = TestSelectionJson.Parse(BlobText(repositoryRoot, inventory, documents)) as IDictionary;
                if (catalog == null || !(catalog["schemaVersion"] is long version) || version != 1
                    || !(catalog["projects"] is IDictionary listed) || !(catalog["batches"] is object[] batched)) throw new NightlyRefusal("category-inventory-invalid");
                catalogProjects = listed;
                catalogBatches = batched;
                exemptionValue = catalog["nonQualificationTests"];
            }
            catch (Exception e) when (!(e is OutOfMemoryException)) { reasons.Add("category-inventory-invalid"); }

            foreach (var path in TestSelectionJson.Sorted(graph.Paths.Where(p => p.EndsWith(".csproj", StringComparison.Ordinal)).ToArray()))
            {
                // @($catalog.projects[$path]): no entry is one null item, a list its items, anything else one item.
                var value = catalogProjects.Contains(path) ? catalogProjects[path] : null;
                var items = value is object[] list ? list : new[] { value };
                string[] categories;
                if (items.Length == 0 || items.Any(c => !(c is string s) || !Category.IsMatch(s)))
                {
                    reasons.Add("unknown-project:" + path);
                    categories = new[] { "unknown" };
                }
                else categories = TestSelectionJson.Sorted(items);
                graph.Projects[path] = new RcProject { Path = path, Categories = categories };
            }
            foreach (var key in catalogProjects.Keys)
                if (!graph.Projects.ContainsKey(PsValue.Str(key))) reasons.Add("stale-project:" + PsValue.Str(key));
            foreach (var batch in catalogBatches)
                if (!graph.Projects.ContainsKey(PsValue.Str(batch))) reasons.Add("unknown-batch:" + PsValue.Str(batch));

            // Literal imports and ancestor Directory.Build inputs are read conservatively, conditional references included;
            // MSBuild is never run and no project expression evaluated. The start inputs of every project not parsed yet are
            // read together (one path per blob id); the imports they name are read one at a time.
            var wanted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var project in graph.Projects.Values)
                foreach (var input in StartInputs(project.Path, graph.Paths))
                    if (graph.Blobs.TryGetValue(input, out var blob) && !documents.Contains(blob) && !wanted.ContainsKey(blob)) wanted[blob] = input;
            var prefetched = new Dictionary<string, string>(StringComparer.Ordinal);
            if (wanted.Count > 0)
            {
                var batchTexts = readBatch(repositoryRoot, sha, wanted.Values.ToArray()) ?? new Dictionary<string, string>();
                foreach (var pair in wanted) if (batchTexts.TryGetValue(pair.Value, out var batchText)) prefetched[pair.Key] = batchText;
            }

            string[] sortedPaths = null;
            foreach (var project in graph.Projects.Values)
            {
                var queue = new Queue<string>(StartInputs(project.Path, graph.Paths));
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var references = new List<string>();
                var inputs = new List<string>();
                while (queue.Count > 0)
                {
                    var input = queue.Dequeue();
                    if (!visited.Add(input)) continue;
                    try
                    {
                        if (!graph.Blobs.TryGetValue(input, out var blob)) throw new GraphFault("missing-input:" + input);
                        if (!documents.Contains(blob))
                        {
                            var xml = prefetched.TryGetValue(blob, out var known) ? known : TestSelectionGit.Blob(repositoryRoot, sha, input);
                            documents[blob] = Parse(xml);
                        }
                        // Invalid immutable XML fails the same way at every import site: each site records it.
                        if (documents[blob] is Exception failure) throw new GraphFault(failure.Message);
                        var document = (XmlDocument)documents[blob];
                        foreach (var node in Elements(document, "//*[local-name()='ProjectReference']"))
                        {
                            var target = Resolve(input, node.GetAttribute("Include"), project.Path, false);
                            if (!graph.Projects.ContainsKey(target)) throw new GraphFault("missing-reference:" + input);
                            references.Add(target);
                        }
                        foreach (var node in Elements(document, "//*[local-name()='Import']"))
                            queue.Enqueue(Resolve(input, node.GetAttribute("Project"), input, false));
                        if (Elements(document, "//*[local-name()='PackageReference' and @Include]").Any(n => string.Equals(n.GetAttribute("Include"), "TUnit", StringComparison.OrdinalIgnoreCase)))
                            project.HasTests = true;
                        foreach (var node in Elements(document, "//*[@Include and (local-name()='Compile' or local-name()='Content' or local-name()='None' or local-name()='EmbeddedResource' or local-name()='AdditionalFiles') and not(ancestor::*[local-name()='Target'])]"))
                        {
                            var include = node.GetAttribute("Include");
                            // NuGet's Pkg properties name restored package binaries, not repository files; packages are owned elsewhere.
                            if (PackageProperty.IsMatch(include)) continue;
                            var target = Resolve(input, include, project.Path, true);
                            var wild = target.IndexOfAny(new[] { '*', '?' });
                            if (wild < 0)
                            {
                                if (node.LocalName == "Compile" && !graph.Paths.Contains(target)) throw new GraphFault("missing-linked-input:" + input);
                                inputs.Add(target);
                                continue;
                            }
                            // MSBuild takes square brackets literally. Its simple globs match conservatively across directories, in
                            // bounded time; the expression is anchored and case-sensitive, so only the sorted range of paths that
                            // start with the glob's literal prefix can match.
                            var pattern = new Regex("^" + Regex.Escape(target).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                                RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
                            if (sortedPaths == null)
                            {
                                sortedPaths = graph.Paths.ToArray();
                                Array.Sort(sortedPaths, StringComparer.Ordinal);
                            }
                            var prefix = target.Substring(0, wild);
                            var at = Array.BinarySearch(sortedPaths, prefix, StringComparer.Ordinal);
                            if (at < 0) at = ~at;
                            try
                            {
                                for (; at < sortedPaths.Length && sortedPaths[at].StartsWith(prefix, StringComparison.Ordinal); at++)
                                    if (pattern.IsMatch(sortedPaths[at])) inputs.Add(sortedPaths[at]);
                            }
                            catch (RegexMatchTimeoutException) { throw new GraphFault("glob-timeout:" + include); }
                        }
                    }
                    catch (Exception e) when (!(e is OutOfMemoryException)) { reasons.Add("graph:" + input + ":" + e.Message); }
                }
                project.References = TestSelectionJson.Sorted(references.ToArray());
                project.Inputs = TestSelectionJson.Sorted(inputs.ToArray());
            }

            IDictionary exemptions = new OrderedDictionary(StringComparer.Ordinal);
            if (exemptionValue is IDictionary given) exemptions = given;
            else if (exemptionValue != null) reasons.Add("invalid-test-dispositions");
            foreach (DictionaryEntry pair in exemptions)
            {
                var key = PsValue.Str(pair.Key);
                if (!graph.Projects.ContainsKey(key) || !(pair.Value is string reason) || string.IsNullOrWhiteSpace(reason)) reasons.Add("invalid-test-disposition:" + key);
            }
            var batches = catalogBatches.Select(PsValue.Str).ToList();
            foreach (var project in graph.Projects.Values)
            {
                if (batches.Contains(project.Path, StringComparer.Ordinal)) project.BatchDisposition = "whole-project";
                else if (exemptions.Contains(project.Path)) project.BatchDisposition = "harness-only";
                else if (project.HasTests)
                {
                    reasons.Add("unbatched-test-project:" + project.Path);
                    batches.Add(project.Path);
                }
                project.Downstream = Downstream(graph, new[] { project.Path }).Where(p => p != project.Path).ToArray();
                // An edge back to the project through its own downstream is a cycle.
                foreach (var dependency in project.References)
                    if (dependency == project.Path || project.Downstream.Contains(dependency, StringComparer.Ordinal)) reasons.Add("project-cycle:" + project.Path);
            }
            graph.Batches = TestSelectionJson.Sorted(batches.ToArray());
            graph.InventoryBlob = graph.Blobs.TryGetValue(CategoryPath, out var inventoryBlob) ? inventoryBlob : "";
            graph.FallbackReasons = TestSelectionJson.Sorted(reasons.ToArray());
            return graph;
        }

        /// <summary>Get-RcDownstream: the seeds and every project that references one of them, transitively (ordinal).</summary>
        public static string[] Downstream(RcProjectGraph graph, IEnumerable<string> seeds)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var seed in seeds ?? Array.Empty<string>()) if (seed != null && seen.Add(seed)) queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var project in graph.Projects.Values)
                    if (project.References.Contains(current, StringComparer.Ordinal) && seen.Add(project.Path)) queue.Enqueue(project.Path);
            }
            return TestSelectionJson.Sorted(seen.ToArray());
        }

        /// <summary>
        /// Resolve-RcProjectInput: the repository path an item or import names. A relative include is taken from the
        /// project's directory, one starting with $(MSBuildThisFileDirectory) from the declaring file's. A property,
        /// item or metadata reference, a rooted path or (unless allowWildcard) a glob is unresolved-input:&lt;from&gt;, and
        /// a path leaving the tree outside-input:&lt;from&gt; (GraphFault).
        /// </summary>
        public static string Resolve(string from, string include, string project, bool allowWildcard)
        {
            from ??= "";
            var directory = Directory(project ?? from);
            var value = (include ?? "").Replace('\\', '/');
            if (value.StartsWith(ThisFileDirectory, StringComparison.Ordinal))
            {
                directory = Directory(from);
                value = value.Substring(ThisFileDirectory.Length);
            }
            if (value.Length == 0 || Unresolved.IsMatch(value) || (!allowWildcard && Wildcard.IsMatch(value)) || value.StartsWith("/", StringComparison.Ordinal))
                throw new GraphFault("unresolved-input:" + from);
            var parts = new List<string>();
            foreach (var part in (directory + value).Split('/'))
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

        /// <summary>Read-RcProjectBlob: a blob's text by its immutable id (cat-file blob), kept in the cache under "text:&lt;blob&gt;".</summary>
        public static string BlobText(string repositoryRoot, string blob, IDictionary cache)
        {
            var key = "text:" + blob;
            if (cache != null && cache[key] is string kept) return kept;
            var text = TestSelectionGit.Run(repositoryRoot, new[] { "cat-file", "blob", blob ?? "" }).Stdout;
            if (cache != null) cache[key] = text;
            return text;
        }

        // A parsed document, or the exception the text gave (no DTD, no resolver).
        private static object Parse(string text)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            try
            {
                using var reader = XmlReader.Create(new StringReader(text ?? ""), settings);
                var document = new XmlDocument { XmlResolver = null };
                document.Load(reader);
                return document;
            }
            catch (XmlException e) { return e; }
        }

        private static IEnumerable<XmlElement> Elements(XmlDocument document, string xpath)
        {
            foreach (XmlNode node in document.SelectNodes(xpath))
                if (node is XmlElement element) yield return element;
        }

        // The path without its last segment ("" for a top-level file).
        private static string Directory(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path.Substring(0, slash + 1);
        }

        /// <summary>The project file, then Directory.Build.props/.targets of each ancestor directory the tree holds.</summary>
        private static List<string> StartInputs(string projectPath, HashSet<string> paths)
        {
            var inputs = new List<string> { projectPath };
            var slash = projectPath.LastIndexOf('/');
            var ancestor = slash < 0 ? "" : projectPath.Substring(0, slash);
            while (true)
            {
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
                {
                    var input = ancestor.Length > 0 ? ancestor + "/" + name : name;
                    if (paths.Contains(input)) inputs.Add(input);
                }
                if (ancestor.Length == 0) break;
                slash = ancestor.LastIndexOf('/');
                ancestor = slash < 0 ? "" : ancestor.Substring(0, slash);
            }
            return inputs;
        }

        internal static OrderedDictionary Ordered(params (string Key, object Value)[] pairs)
        {
            var map = new OrderedDictionary(StringComparer.Ordinal);
            foreach (var (key, value) in pairs) map[key] = value;
            return map;
        }
    }
}
