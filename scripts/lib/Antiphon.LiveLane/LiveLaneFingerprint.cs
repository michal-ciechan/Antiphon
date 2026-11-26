// CARD-1545 S2a-1: the live-lane input fingerprint, ported from scripts/lib/live-lane-fingerprint.ps1 (W2 slice S22; the
// script is deleted). Diagnostic and shadow-only: it never earns gate credit or stands for execution. It reads Git objects
// at one commit only (no working tree, no mtime) and makes the lane DUE whenever an input is not proven covered. Git and
// the rc project graph come through LiveLaneSources (the CLI binds the nightly core's TestSelectionGit and
// RcProjectSelection, the reads the PowerShell called). Every step runs in the PowerShell's order and digests the same
// canonical text, so a commit gives the same fingerprint, byte for byte (LiveLaneParityTests). Plain C#, framework only.
// ASCII only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>One entry of a commit's tree (ls-tree): its path, mode and object id.</summary>
    public sealed class LiveLaneTreeEntry
    {
        public LiveLaneTreeEntry(string path, string mode, string blob)
        {
            Path = path;
            Mode = mode;
            Blob = blob;
        }

        public string Path { get; }
        public string Mode { get; }
        public string Blob { get; }

        public LiveLaneMap ToJson() => new LiveLaneMap(("path", Path), ("mode", Mode), ("blob", Blob));
    }

    /// <summary>A commit's tree in ls-tree order, with the listing it was read from.</summary>
    public sealed class LiveLaneTree
    {
        private readonly Dictionary<string, LiveLaneTreeEntry> _entries = new Dictionary<string, LiveLaneTreeEntry>(StringComparer.Ordinal);
        private readonly List<string> _paths = new List<string>();

        public LiveLaneTree(string listing) => Listing = listing ?? "";

        public string Listing { get; }
        public IReadOnlyList<string> Paths => _paths;
        public bool Contains(string path) => path != null && _entries.ContainsKey(path);
        public LiveLaneTreeEntry this[string path] => path != null && _entries.TryGetValue(path, out var entry) ? entry : null;

        internal void Add(LiveLaneTreeEntry entry)
        {
            if (!_entries.ContainsKey(entry.Path)) _paths.Add(entry.Path);
            _entries[entry.Path] = entry;
        }
    }

    /// <summary>One lane's entry of tests/live-lane-inputs.json.</summary>
    public sealed class LiveLaneDeclaration
    {
        public string Manifest { get; set; }
        public IReadOnlyList<string> ContractFiles { get; set; }
        public IReadOnlyList<string> MinimumRoots { get; set; }
        public IReadOnlyList<string> RuntimeRoots { get; set; }
        public IReadOnlyList<string> NamespaceRoots { get; set; }
        public IReadOnlyList<string> InstructionPatterns { get; set; }
    }

    /// <summary>Get-LiveLaneFingerprint's answer.</summary>
    public sealed class LiveLaneFingerprintResult
    {
        public const string Format = "antiphon-live-lane-fingerprint-v1";

        public string LaneId { get; set; }
        public string Status { get; set; }
        public IReadOnlyList<string> UnknownReasons { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Observations { get; set; } = Array.Empty<string>();
        public string Digest { get; set; }
        /// <summary>laneContract, repositoryClosure, liveRoots, testSources, runtimeInputs, instructions (and environment).</summary>
        public LiveLaneMap Components { get; set; }
        public IReadOnlyList<string> SelectedProjects { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> ClosureProjects { get; set; } = Array.Empty<string>();
        public IReadOnlyList<LiveLaneTreeEntry> Inventory { get; set; } = Array.Empty<LiveLaneTreeEntry>();
        /// <summary>The attestation's status and reasons when one was given; null otherwise.</summary>
        public LiveLaneEnvironment Environment { get; set; }

        public bool Complete => Status == "complete";

        /// <summary>{status, reasons} of the attestation, or null (the fingerprint's environment field).</summary>
        public LiveLaneMap EnvironmentJson() =>
            Environment == null ? null : new LiveLaneMap(("status", Environment.Status), ("reasons", Environment.Reasons.Cast<object>().ToList()));

        /// <summary>The digest of the code alone: the components without the environment (the receipts' Claude-version key).</summary>
        public string CodeDigest => LiveLaneFingerprint.CodeDigest(LaneId, Components);

        /// <summary>The ordered hashtable the PowerShell returned.</summary>
        public LiveLaneMap ToJson()
        {
            var map = new LiveLaneMap(("format", Format), ("schemaVersion", 1), ("laneId", LaneId), ("status", Status),
                ("unknownReasons", UnknownReasons.Cast<object>().ToList()), ("observations", Observations.Cast<object>().ToList()), ("digest", Digest), ("components", Components),
                ("selectedProjects", SelectedProjects.Cast<object>().ToList()), ("closureProjects", ClosureProjects.Cast<object>().ToList()),
                ("inventory", Inventory.Select(entry => (object)entry.ToJson()).ToList()));
            if (Environment != null) map["environment"] = EnvironmentJson();
            return map;
        }
    }

    /// <summary>The fingerprint and its helpers.</summary>
    public static class LiveLaneFingerprint
    {
        public const string DeclarationPath = "tests/live-lane-inputs.json";
        public const string DeclarationFormat = "antiphon-live-lane-inputs-v1";
        public const string ManifestFormat = "antiphon-live-lanes-v1";

        private static readonly string[] DeclarationFields = { "manifest", "contractFiles", "minimumRoots", "runtimeRoots", "namespaceRoots", "instructionPatterns" };
        public static readonly IReadOnlyList<string> Components = new[] { "laneContract", "repositoryClosure", "liveRoots", "testSources", "runtimeInputs", "instructions" };
        // The files the CLI loads by name, independent of what a declaration says it hashes.
        private static readonly string[] InstructionLeaves = { "CLAUDE.md", "CLAUDE.local.md", "AGENTS.md", ".mcp.json" };
        private static readonly string[] ImportingLeaves = { "CLAUDE.md", "CLAUDE.local.md", "AGENTS.md" };
        private static readonly Regex SharedBuild = new Regex(@"(^|/)(Directory\.Build\.[^/]*|[^/]*\.props|[^/]*\.targets|global\.json|NuGet\.Config|nuget\.config|Directory\.Packages\.props|packages\.lock\.json|[^/]*\.sln[^/]*)$", RegexOptions.CultureInvariant);
        private static readonly Regex TreeLine = new Regex("^([0-7]{6}) ([a-z]+) ([0-9a-f]{40}|[0-9a-f]{64})\t(.+)$");
        private static readonly Regex RegularMode = new Regex("^1006[0-7]{2}$");
        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$");
        private static readonly Regex LaneName = new Regex("^[a-z0-9][a-z0-9.-]*$");
        private static readonly Regex NamespaceRoot = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$");
        private static readonly Regex ProjectName = new Regex("^[A-Za-z0-9_][A-Za-z0-9_.-]*$");
        private static readonly Regex ImportLine = new Regex(@"^\s*@(\S+)\s*$");
        private static readonly Regex Drive = new Regex("^[A-Za-z]:", RegexOptions.IgnoreCase);
        private static readonly Regex Namespace = new Regex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)");
        private static readonly Regex ScriptLiteral = new Regex(@"""((?:scripts/)?[A-Za-z0-9_./-]+\.(?:ps1|psm1|mjs|js|sh))""");
        private static readonly Regex Printable = new Regex(@"^[ !#-\[\]-~]*$");
        private static readonly Regex ClaudeTree = new Regex(@"(^|/)\.claude/");

        /// <summary>ConvertTo-LiveLaneGlobRegex: ** crosses directories, * and ? do not, anything else is literal.</summary>
        public static Regex Glob(string pattern)
        {
            var builder = new StringBuilder("^");
            for (var i = 0; i < pattern.Length; i++)
            {
                var ch = pattern[i];
                if (ch == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*') { builder.Append(".*"); i++; }
                else if (ch == '*') builder.Append("[^/]*");
                else if (ch == '?') builder.Append("[^/]");
                else builder.Append(Regex.Escape(ch.ToString()));
            }
            return new Regex(builder.Append('$').ToString(), RegexOptions.CultureInvariant);
        }

        /// <summary>Resolve-LiveLanePath: a relative reference joined onto a repository directory; null when it leaves the tree.</summary>
        public static string ResolvePath(string directory, string reference)
        {
            var parts = new List<string>();
            foreach (var part in ((directory ?? "") + (reference ?? "").Replace('\\', '/')).Split('/'))
            {
                if (part == "..")
                {
                    if (parts.Count == 0) return null;
                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part.Length > 0 && part != ".") parts.Add(part);
            }
            return string.Join("/", parts);
        }

        // The path without its last segment, keeping the slash ($path -replace '[^/]+$','').
        private static string Parent(string path) => Regex.Replace(path, "[^/]+$", "");

        // The last segment ($path -replace '^.*/','').
        private static string Leaf(string path) => Regex.Replace(path, "^.*/", "");

        private static bool StringList(object value, bool root)
        {
            if (!(value is IList<object> list)) return false;
            foreach (var item in list)
            {
                if (!(item is string text) || text.Length == 0 || text.StartsWith("/", StringComparison.Ordinal) || text.Contains('\\') || text.Contains("..")) return false;
                if (root && !text.EndsWith("/", StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static IReadOnlyList<string> Strings(object value) => ((IList<object>)value).Cast<string>().ToList();

        /// <summary>Read-LiveLaneTree: the commit's tree (ls-tree -r -z); malformed-lane-tree for an entry of another shape.</summary>
        public static LiveLaneTree ReadTree(ILiveLaneGit git, string root, string sha)
        {
            var text = git.Run(root, new[] { "ls-tree", "-r", "-z", sha, "--" }, new[] { 0 }).Stdout;
            var tree = new LiveLaneTree(text);
            foreach (var entry in text.Split('\0'))
            {
                if (entry.Length == 0) continue;
                var match = TreeLine.Match(entry);
                if (!match.Success) throw new LiveLaneRefusal("malformed-lane-tree");
                tree.Add(new LiveLaneTreeEntry(match.Groups[4].Value, match.Groups[1].Value, match.Groups[3].Value));
            }
            return tree;
        }

        // Read-LiveLaneBlob: the bytes of the tree entry at path, read by its blob id.
        private static string Blob(ILiveLaneGit git, string root, LiveLaneTree tree, string path) => git.BlobText(root, tree[path]?.Blob ?? "");

        /// <summary>Read-LiveLaneDeclaration: the lane's entry, exactly the six fields, typed; declaration-invalid otherwise.</summary>
        public static LiveLaneDeclaration ReadDeclaration(ILiveLaneGit git, string root, LiveLaneTree tree, string lane)
        {
            if (!tree.Contains(DeclarationPath) || !RegularMode.IsMatch(tree[DeclarationPath].Mode)) throw new LiveLaneRefusal("declaration-invalid");
            object document;
            try { document = LiveLaneData.Parse(Blob(git, root, tree, DeclarationPath)); }
            catch (Exception) { throw new LiveLaneRefusal("declaration-invalid"); }
            if (!(document is LiveLaneMap map)) throw new LiveLaneRefusal("declaration-invalid");
            if (string.Join(",", LiveLaneData.Sorted(map.Keys)) != "format,lanes" || !LiveLaneData.Same(map["format"], DeclarationFormat)
                || !(map["lanes"] is LiveLaneMap lanes)) throw new LiveLaneRefusal("declaration-invalid");
            var names = lanes.Keys.Where(name => name == lane).ToList();
            if (names.Count != 1) throw new LiveLaneRefusal("declaration-invalid");
            if (!(lanes[names[0]] is LiveLaneMap entry)) throw new LiveLaneRefusal("declaration-invalid");
            if (string.Join(",", LiveLaneData.Sorted(entry.Keys)) != string.Join(",", LiveLaneData.Sorted(DeclarationFields))) throw new LiveLaneRefusal("declaration-invalid");
            if (!(entry["manifest"] is string manifest) || !StringList(new List<object> { manifest }, false)) throw new LiveLaneRefusal("declaration-invalid");
            foreach (var field in new[] { "contractFiles", "instructionPatterns" }) if (!StringList(entry[field], false)) throw new LiveLaneRefusal("declaration-invalid");
            foreach (var field in new[] { "minimumRoots", "runtimeRoots" }) if (!StringList(entry[field], true)) throw new LiveLaneRefusal("declaration-invalid");
            if (!(entry["namespaceRoots"] is IList<object> roots) || roots.Any(r => !(r is string s) || !NamespaceRoot.IsMatch(s))) throw new LiveLaneRefusal("declaration-invalid");
            return new LiveLaneDeclaration
            {
                Manifest = manifest, ContractFiles = Strings(entry["contractFiles"]), MinimumRoots = Strings(entry["minimumRoots"]),
                RuntimeRoots = Strings(entry["runtimeRoots"]), NamespaceRoots = Strings(roots), InstructionPatterns = Strings(entry["instructionPatterns"])
            };
        }

        /// <summary>The lane ids a commit's declaration names (in document order); declaration-invalid when it cannot be read.</summary>
        public static IReadOnlyList<string> DeclaredLanes(ILiveLaneGit git, string root, LiveLaneTree tree)
        {
            if (!tree.Contains(DeclarationPath) || !RegularMode.IsMatch(tree[DeclarationPath].Mode)) throw new LiveLaneRefusal("declaration-invalid");
            object document;
            try { document = LiveLaneData.Parse(Blob(git, root, tree, DeclarationPath)); }
            catch (Exception) { throw new LiveLaneRefusal("declaration-invalid"); }
            if (!(document is LiveLaneMap map) || !(map["lanes"] is LiveLaneMap lanes)) throw new LiveLaneRefusal("declaration-invalid");
            return lanes.Keys.ToList();
        }

        private static object Manifest(ILiveLaneGit git, string root, LiveLaneTree tree, string path)
        {
            if (!tree.Contains(path)) throw new LiveLaneRefusal("manifest-missing");
            try { return LiveLaneData.Parse(Blob(git, root, tree, path)); }
            catch (Exception) { throw new LiveLaneRefusal("manifest-invalid"); }
        }

        /// <summary>Read-LiveLaneManifestProjects: the projects the manifest's core rows name, ordinal and distinct.</summary>
        public static IReadOnlyList<string> ManifestProjects(ILiveLaneGit git, string root, LiveLaneTree tree, string path)
        {
            var manifest = Manifest(git, root, tree, path) as LiveLaneMap;
            var core = manifest == null ? null : manifest["core"] as IList<object>;
            if (manifest == null || !LiveLaneData.Same(manifest["format"], ManifestFormat) || core == null || core.Count == 0) throw new LiveLaneRefusal("manifest-invalid");
            var projects = new List<string>();
            foreach (var item in core)
            {
                if (!(item is LiveLaneMap row) || !(row["project"] is string project) || !ProjectName.IsMatch(project)) throw new LiveLaneRefusal("manifest-invalid");
                projects.Add(project);
            }
            return LiveLaneData.Sorted(projects);
        }

        /// <summary>Get-LiveLaneManifestRoster: the lane's expanded core uids at one commit (attestation rows and receipts bind to them).</summary>
        public static IReadOnlyList<string> ManifestRoster(ILiveLaneGit git, string root, LiveLaneTree tree, string path) =>
            LiveLaneAttestation.Roster(Manifest(git, root, tree, path));

        /// <summary>Get-LiveLaneFingerprintDigest: domain-separated by lane and schema version; the components are the whole material.</summary>
        public static string Digest(string lane, object components)
        {
            var material = "antiphon-live-lane-fingerprint\n" + LiveLaneData.Canonical(new LiveLaneMap(("schemaVersion", 1), ("laneId", lane), ("components", components)));
            return LiveLaneData.Sha256Text(material);
        }

        /// <summary>
        /// The code digest: the fingerprint digest of the components without the environment (the attested executable,
        /// runtime, settings and account). Receipts are keyed by it and by the Claude version, so one code digest can be shown
        /// against every Claude version it was run with. Null when there are no components.
        /// </summary>
        public static string CodeDigest(string lane, LiveLaneMap components)
        {
            if (components == null) return null;
            var code = new LiveLaneMap();
            foreach (var pair in components) if (pair.Key != "environment") code[pair.Key] = pair.Value;
            return Digest(lane, code);
        }

        // Get-LiveLaneImportSet: the files MSBuild visits for the closure projects, mirroring the reader's literal walk (ancestor
        // Directory.Build.* and literal Import targets, missing targets included).
        private static HashSet<string> ImportSet(LiveLaneProjectGraph graph, IEnumerable<string> projects)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in projects)
            {
                var queue = new Queue<string>();
                queue.Enqueue(project);
                var ancestor = Regex.Replace(project, "/[^/]+$", "");
                if (ancestor == project) ancestor = "";
                while (true)
                {
                    foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
                    {
                        var input = ancestor.Length > 0 ? ancestor + "/" + name : name;
                        if (graph.Paths.Contains(input)) queue.Enqueue(input);
                    }
                    if (ancestor.Length == 0) break;
                    ancestor = ancestor.Contains('/') ? Regex.Replace(ancestor, "/[^/]+$", "") : "";
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (queue.Count > 0)
                {
                    var input = queue.Dequeue();
                    if (!seen.Add(input)) continue;
                    visited.Add(input);
                    // An unresolvable import is already a graph reason naming this input.
                    if (graph.Imports.TryGetValue(input, out var targets)) foreach (var target in targets) queue.Enqueue(target);
                }
            }
            return visited;
        }

        // Get-LiveLaneInstructionImports: follows CLI `@path` lines relative to the importing file, bounded and cycle-safe.
        private static HashSet<string> InstructionImports(ILiveLaneGit git, string root, LiveLaneTree tree, IEnumerable<string> files, List<string> unknown)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<(string File, int Depth)>();
            foreach (var file in files) queue.Enqueue((file, 0));
            var read = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var (file, depth) = queue.Dequeue();
                if (!read.Add(file)) continue;
                if (!RegularMode.IsMatch(tree[file]?.Mode ?? "")) continue;
                var text = LiveLaneData.LfText(Blob(git, root, tree, file));
                foreach (var line in text.Split('\n'))
                {
                    var match = ImportLine.Match(line);
                    if (!match.Success) continue;
                    var reference = match.Groups[1].Value;
                    if (reference.StartsWith("~", StringComparison.Ordinal)) { unknown.Add("instruction-import-user:" + file); continue; }
                    if (reference.StartsWith("/", StringComparison.Ordinal) || reference.StartsWith("\\", StringComparison.Ordinal) || Drive.IsMatch(reference))
                    {
                        unknown.Add("instruction-import-outside:" + file);
                        continue;
                    }
                    var target = ResolvePath(Parent(file), reference);
                    if (string.IsNullOrEmpty(target)) { unknown.Add("instruction-import-outside:" + file); continue; }
                    if (!tree.Contains(target)) { unknown.Add("instruction-import-missing:" + target); continue; }
                    found.Add(target);
                    if (depth + 1 >= 8) { unknown.Add("instruction-import-depth:" + target); continue; }
                    queue.Enqueue((target, depth + 1));
                }
            }
            return found;
        }

        // Get-LiveLaneGrep: one git process per scan; records are rev:path NUL line NUL text.
        private static List<(string Path, string Text)> Grep(ILiveLaneGit git, string root, string sha, string pattern, IEnumerable<string> pathSpecs)
        {
            var rows = new List<(string, string)>();
            var arguments = new List<string> { "grep", "-z", "-n", "-I", "-E", pattern, sha, "--" };
            arguments.AddRange(pathSpecs);
            var text = git.Run(root, arguments, new[] { 0, 1 }).Stdout;
            foreach (var record in text.Split('\n'))
            {
                if (record.Length == 0) continue;
                var fields = record.Split('\0', 3);
                if (fields.Length != 3 || !fields[0].StartsWith(sha + ":", StringComparison.Ordinal)) throw new LiveLaneRefusal("malformed-lane-grep");
                rows.Add((fields[0].Substring(sha.Length + 1), fields[2]));
            }
            return rows;
        }

        // Get-LiveLaneEntryDigest: TestSelectionJson.Digest over the tree entries of the paths (null for a path the tree does not hold).
        private static string EntryDigest(IEnumerable<string> paths, LiveLaneTree tree)
        {
            var builder = new StringBuilder("[");
            foreach (var path in paths)
            {
                if (builder.Length > 1) builder.Append(',');
                var entry = tree[path];
                if (entry != null && Printable.IsMatch(path) && Printable.IsMatch(entry.Mode) && Printable.IsMatch(entry.Blob))
                    builder.Append("{\"blob\":\"").Append(entry.Blob).Append("\",\"mode\":\"").Append(entry.Mode).Append("\",\"path\":\"").Append(path).Append("\"}");
                else builder.Append(LiveLaneData.Canonical(entry?.ToJson()));
            }
            return LiveLaneData.Sha256Text(builder.Append(']').ToString());
        }

        /// <summary>
        /// Get-LiveLaneFingerprint: the lane's input fingerprint at one full commit id (full-commit-required otherwise),
        /// with the attested environment as one more component when attestation names a document (an unknown environment
        /// leaves the lane unknown).
        /// </summary>
        public static LiveLaneFingerprintResult Compute(LiveLaneSources sources, string root, string sha, string lane, string attestation = null)
        {
            var git = sources.Git;
            if (sha == null || !FullSha.IsMatch(sha) || git.Run(root, new[] { "cat-file", "-t", sha }, new[] { 0 }).Stdout.Trim() != "commit")
                throw new LiveLaneRefusal("full-commit-required");
            if (lane == null || !LaneName.IsMatch(lane)) throw new LiveLaneRefusal("declaration-invalid");
            var tree = ReadTree(git, root, sha);
            var declaration = ReadDeclaration(git, root, tree, lane);
            var projectNames = ManifestProjects(git, root, tree, declaration.Manifest);
            var unknown = new List<string>();
            var observations = new List<string>();

            // Core rows name projects; each must resolve to exactly one csproj, never a fallback.
            var csprojs = tree.Paths.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal)).ToList();
            var selected = new List<string>();
            foreach (var name in projectNames)
            {
                var hits = csprojs.Where(path => Leaf(path) == name + ".csproj").ToList();
                if (hits.Count == 0) unknown.Add("core-project-missing:" + name);
                else if (hits.Count > 1) unknown.Add("core-project-ambiguous:" + name);
                else selected.Add(hits[0]);
            }

            // Closure from the candidate graph: selected test projects and every transitive reference.
            var graph = sources.Graph.Read(root, sha, tree.Listing);
            var closure = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var project in selected) if (closure.Add(project)) queue.Enqueue(project);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!graph.Projects.TryGetValue(current, out var node)) continue;
                foreach (var reference in node.References) if (closure.Add(reference)) queue.Enqueue(reference);
            }
            var closureProjects = LiveLaneData.Sorted(closure);
            var imports = ImportSet(graph, closureProjects);

            // Input faults in the closure or its imports leave the closure unknown; catalog faults and faults in other
            // projects are observations (Q-8).
            var relevant = closureProjects.Concat(imports).ToList();
            foreach (var reason in graph.FallbackReasons)
            {
                var closureFault = false;
                if (reason.StartsWith("graph:", StringComparison.Ordinal))
                    closureFault = relevant.Any(input => reason.StartsWith("graph:" + input + ":", StringComparison.Ordinal));
                else if (reason.StartsWith("project-cycle:", StringComparison.Ordinal))
                    closureFault = closure.Contains(reason.Substring("project-cycle:".Length));
                if (closureFault) unknown.Add(reason); else observations.Add(reason);
            }

            var members = Components.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            var closureDirectories = closureProjects.Select(Parent).ToList();
            var testDirectories = selected.Select(Parent).ToList();
            var contract = declaration.ContractFiles.Select(Glob).ToList();
            var instructionRules = declaration.InstructionPatterns.Select(pattern =>
            {
                if (pattern.EndsWith("/**", StringComparison.Ordinal) && !pattern.Substring(0, pattern.Length - 3).Contains('*'))
                    return new Regex("(^|/)" + Regex.Escape(pattern.Substring(0, pattern.Length - 3)) + "/", RegexOptions.CultureInvariant);
                if (!pattern.Contains('/')) return new Regex("(^|/)" + Glob(pattern).ToString().Substring(1), RegexOptions.CultureInvariant);
                return Glob(pattern);
            }).ToList();
            var rootCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var declared in declaration.MinimumRoots.Concat(declaration.RuntimeRoots)) rootCounts[declared] = 0;
            var contractCounts = new int[contract.Count];
            var instructionFiles = new List<string>();
            foreach (var path in tree.Paths)
            {
                foreach (var directory in closureDirectories) if (path.StartsWith(directory, StringComparison.Ordinal)) { members["repositoryClosure"].Add(path); break; }
                foreach (var directory in testDirectories) if (path.StartsWith(directory, StringComparison.Ordinal)) { members["testSources"].Add(path); break; }
                if (SharedBuild.IsMatch(path)) members["repositoryClosure"].Add(path);
                foreach (var declared in declaration.MinimumRoots) if (path.StartsWith(declared, StringComparison.Ordinal)) { members["liveRoots"].Add(path); rootCounts[declared]++; }
                foreach (var declared in declaration.RuntimeRoots) if (path.StartsWith(declared, StringComparison.Ordinal)) { members["runtimeInputs"].Add(path); rootCounts[declared]++; }
                for (var i = 0; i < contract.Count; i++) if (contract[i].IsMatch(path)) { members["laneContract"].Add(path); contractCounts[i]++; }
                if (InstructionLeaves.Contains(Leaf(path), StringComparer.Ordinal) || ClaudeTree.IsMatch(path))
                {
                    members["instructions"].Add(path);
                    instructionFiles.Add(path);
                    if (!instructionRules.Any(rule => rule.IsMatch(path))) unknown.Add("instruction-outside-inputs:" + path);
                }
            }
            foreach (var project in closureProjects)
            {
                if (!graph.Projects.TryGetValue(project, out var node)) continue;
                foreach (var input in node.Inputs)
                {
                    if (!tree.Contains(input)) continue;
                    members["repositoryClosure"].Add(input);
                    if (selected.Contains(project, StringComparer.Ordinal)) members["testSources"].Add(input);
                }
            }
            foreach (var input in imports) if (tree.Contains(input)) members["repositoryClosure"].Add(input);
            members["laneContract"].Add(DeclarationPath);
            members["laneContract"].Add(declaration.Manifest);
            foreach (var pair in rootCounts) if (pair.Value == 0) unknown.Add("declared-root-empty:" + pair.Key);
            for (var i = 0; i < contract.Count; i++) if (contractCounts[i] == 0) unknown.Add("declared-root-empty:" + declaration.ContractFiles[i]);
            var importers = instructionFiles.Where(path => ImportingLeaves.Contains(Leaf(path), StringComparer.Ordinal)).ToList();
            foreach (var path in InstructionImports(git, root, tree, importers, unknown)) members["instructions"].Add(path);

            var inventory = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in Components) inventory.UnionWith(members[name]);
            foreach (var path in inventory)
            {
                var mode = tree[path]?.Mode;
                if (mode == "120000") unknown.Add("symlink:" + path);
                else if (mode == "160000") unknown.Add("gitlink:" + path);
            }

            // A relevant namespace declared outside the inventory is code the lane may compile or load without hashing it.
            foreach (var (path, text) in Grep(git, root, sha, "^[[:space:]]*namespace[[:space:]]+[A-Za-z_][A-Za-z0-9_.]*", new[] { "*.cs" }))
            {
                var match = Namespace.Match(text);
                if (!match.Success) continue;
                var name = match.Groups[1].Value;
                if (inventory.Contains(path)) continue;
                foreach (var declared in declaration.NamespaceRoots)
                    if (name == declared || name.StartsWith(declared + ".", StringComparison.Ordinal)) { unknown.Add("namespace-outside-inputs:" + path); break; }
            }

            // Script literals in selected test sources must name hashed inputs. A bare name is a harness under scripts/; a
            // root that is hashed also covers a file it does not yet hold.
            if (testDirectories.Count > 0)
            {
                var covering = declaration.MinimumRoots.Concat(declaration.RuntimeRoots).Concat(closureDirectories).ToList();
                foreach (var (_, text) in Grep(git, root, sha, "\"(scripts/)?[A-Za-z0-9_./-]+\\.(ps1|psm1|mjs|js|sh)\"", testDirectories))
                {
                    foreach (Match literalMatch in ScriptLiteral.Matches(text))
                    {
                        var literal = literalMatch.Groups[1].Value;
                        var target = literal.StartsWith("scripts/", StringComparison.Ordinal) ? ResolvePath("", literal)
                            : !literal.Contains('/') ? "scripts/" + literal
                            : tree.Contains(literal) ? literal
                            : ResolvePath("scripts/", literal);
                        if (string.IsNullOrEmpty(target)) { unknown.Add("undeclared-runtime:" + literal); continue; }
                        if (inventory.Contains(target) || covering.Any(prefix => target.StartsWith(prefix, StringComparison.Ordinal))) continue;
                        unknown.Add("undeclared-runtime:" + target);
                    }
                }
            }

            var components = new LiveLaneMap();
            foreach (var name in Components) components[name] = EntryDigest(LiveLaneData.Sorted(members[name]), tree);
            // S2a-2: an attested environment is one more component; an unknown environment leaves the lane unknown.
            LiveLaneEnvironment environment = null;
            if (!string.IsNullOrEmpty(attestation))
            {
                var roster = ManifestRoster(git, root, tree, declaration.Manifest);
                environment = LiveLaneAttestation.Environment(attestation, lane, roster);
                if (environment.Complete) components["environment"] = environment.Digest;
                else
                {
                    unknown.Add("environment-unknown");
                    components["environment"] = LiveLaneData.Digest(environment.Reasons.Cast<object>().ToList());
                }
            }
            var unknownReasons = LiveLaneData.Sorted(unknown);
            return new LiveLaneFingerprintResult
            {
                LaneId = lane, Status = unknownReasons.Length > 0 ? "unknown" : "complete", UnknownReasons = unknownReasons,
                Observations = LiveLaneData.Sorted(observations), Digest = Digest(lane, components), Components = components,
                SelectedProjects = LiveLaneData.Sorted(selected), ClosureProjects = closureProjects,
                Inventory = LiveLaneData.Sorted(inventory).Select(path => tree[path]).ToList(), Environment = environment
            };
        }
    }
}
