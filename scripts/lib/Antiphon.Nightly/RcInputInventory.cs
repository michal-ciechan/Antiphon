// CARD-1836 S3a (moved from rc-input-manifest.ps1): object-only, conservative input inventory. Never carry
// authority. Reads only git objects of full commit ids: the tree, the ownership and no-op maps, and every project
// and import the suites' project closure names. Plain C#: no PowerShell types.
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
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>An inventory refusal (full-commit-required, invalid-input-tree, input-map-version, ...).</summary>
    public sealed class RcInputRefusal : Exception
    {
        public RcInputRefusal(string message) : base(message) { }
    }

    /// <summary>One input of a suite at a commit: present with its tree entry, or absent.</summary>
    public sealed class RcInputRecord
    {
        public int Version { get; set; } = 1;
        public string Suite { get; set; }
        public string Project { get; set; }
        public string Path { get; set; }
        public string Mode { get; set; }
        public string Type { get; set; }
        public string ObjectId { get; set; }
        public bool Present { get; set; }
        public string Reason { get; set; }

        internal bool SameAs(RcInputRecord other) =>
            Version == other.Version && Suite == other.Suite && Project == other.Project && Path == other.Path && Mode == other.Mode
            && Type == other.Type && ObjectId == other.ObjectId && Present == other.Present && Reason == other.Reason;
    }

    public sealed class RcInputEdge
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Project { get; set; }
        public string Reason { get; set; }
    }

    public sealed class RcInputSuite
    {
        public string Id { get; set; }
        public string Project { get; set; }
        public RcInputRecord[] Inputs { get; set; } = Array.Empty<RcInputRecord>();
        public RcInputEdge[] Edges { get; set; } = Array.Empty<RcInputEdge>();
    }

    public sealed class RcUnownedPath
    {
        public string Path { get; set; }
        public string Hint { get; set; }
        public string Reason { get; set; } = "unowned-path";
    }

    /// <summary>antiphon-rc-input-snapshot-v1: one commit's suites and their inputs.</summary>
    public sealed class RcInputSnapshot
    {
        public string Format { get; set; } = "antiphon-rc-input-snapshot-v1";
        public string Sha { get; set; }
        public string TreeId { get; set; }
        public string MapObjectId { get; set; }
        public bool IndexBroadened { get; set; }
        public RcInputSuite[] Suites { get; set; } = Array.Empty<RcInputSuite>();
        public string[] Unresolved { get; set; } = Array.Empty<string>();
        /// <summary>Paths no suite owns; null for a snapshot rebuilt from a stored manifest (no claim either way).</summary>
        public RcUnownedPath[] Unowned { get; set; }
        public string[] NoopConflicts { get; set; } = Array.Empty<string>();
        public bool NeedsOrchestratorDecision { get; set; }
        public string CreditBlockedBy { get; set; }
        /// <summary>The checked-in no-op path patterns this snapshot applied (not part of the record).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Regex[] NoopPatterns { get; set; } = Array.Empty<Regex>();

        public bool IsNoopPath(string path) => NoopPatterns.Any(p => p.IsMatch(path ?? ""));
    }

    public sealed class RcDerivedIndexEdge
    {
        public string Path { get; set; }
        public string[] Suites { get; set; }
    }

    /// <summary>Positive derived-index edges: each adds its path to the named suites in both revisions.</summary>
    public sealed class RcDerivedIndex
    {
        public long? Version { get; set; }
        public RcDerivedIndexEdge[] Edges { get; set; } = Array.Empty<RcDerivedIndexEdge>();
    }

    public sealed class RcInputChange
    {
        public string Status { get; set; }
        public string Path { get; set; }
    }

    public sealed class RcInputIndexUse
    {
        public long? Version { get; set; }
        public bool Broadened { get; set; }
        public string Authority { get; set; } = "positive-only";
    }

    /// <summary>antiphon-rc-input-inventory-v1: the two snapshots, their changes and the dirty suites. Shadow only.</summary>
    public sealed class RcInputManifest
    {
        public string Format { get; set; } = "antiphon-rc-input-inventory-v1";
        public string BaselineSha { get; set; }
        public string CandidateSha { get; set; }
        public RcInputSnapshot[] Snapshots { get; set; }
        public RcInputChange[] Changes { get; set; }
        public string[] DirtySuites { get; set; }
        public string[] Unresolved { get; set; }
        public RcUnownedPath[] Unowned { get; set; }
        public bool NeedsOrchestratorDecision { get; set; }
        public string[] TestOnlyDeferred { get; set; }
        public string[] ChangedNotRun { get; set; }
        public string[] NoopConflicts { get; set; }
        public string CreditBlockedBy { get; set; }
        public RcInputIndexUse Index { get; set; }
        public string[] RequiredActualReceipts { get; set; } =
        {
            "sdk-runtime-os-architecture", "powershell-node-browser-native", "configuration-environment",
            "resolved-package-graph-and-content", "generated-fixture-recipes-and-outputs", "immutable-images", "discovery-case-manifest",
        };
        public string Activation { get; set; } = "shadow-only";
        public bool ExecutionEvidence { get; set; }
        public bool GateCredit { get; set; }
    }

    public static class RcInputInventory
    {
        public const string MapPath = "scripts/lib/rc-input-owners.json";
        public const string NoopPath = "scripts/lib/rc-input-noop.json";

        private static Exception Refuse(string what) => new RcInputRefusal(what);

        private static readonly Regex PatternRefused = new Regex(@"[\\\x00-\x1f:;\[\]]", RegexOptions.CultureInvariant);

        /// <summary>
        /// A repository-relative glob: ** across directories (**/ also matches none), * and ? within one. Case-sensitive
        /// unless ignoreCase. Refuses an empty, rooted, parent-escaping or over-long pattern, or one with \ : ; [ ] or a
        /// control character.
        /// </summary>
        public static Regex Pattern(string pattern, bool ignoreCase = false)
        {
            if (string.IsNullOrEmpty(pattern) || pattern.Length > 2048 || PatternRefused.IsMatch(pattern) || pattern.StartsWith("/", StringComparison.Ordinal)
                || pattern.Split('/').Contains("..")) throw Refuse("invalid-input-pattern");
            var expression = "^" + Regex.Escape(pattern).Replace("\\*\\*/", "(?:.*/)?").Replace("\\*\\*", ".*").Replace("\\*", "[^/]*").Replace("\\?", "[^/]") + "$";
            var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            return new Regex(expression, options, TimeSpan.FromMilliseconds(100));
        }

        private static readonly Regex Unresolvable = new Regex(@"[$@%:\x00-\x1f]", RegexOptions.CultureInvariant);

        private static string DirectoryOf(string path) => path.Substring(0, path.LastIndexOf('/') + 1);

        /// <summary>
        /// An MSBuild item or import path relative to the project's directory ($(MSBuildThisFileDirectory) relative to the
        /// importing file), normalised. Any other expression is unresolved; a path above the root is outside.
        /// </summary>
        public static string ResolvePath(string from, string value, string project = null)
        {
            var directory = DirectoryOf(project ?? from);
            var text = (value ?? "").Replace('\\', '/');
            const string thisFile = "$(MSBuildThisFileDirectory)";
            if (text.StartsWith(thisFile, StringComparison.Ordinal)) { directory = DirectoryOf(from); text = text.Substring(thisFile.Length); }
            if (text.Length == 0 || Unresolvable.IsMatch(text) || text.StartsWith("/", StringComparison.Ordinal)) throw Refuse("unresolved-expression");
            var parts = new List<string>();
            foreach (var part in (directory + text).Split('/'))
            {
                if (part == "..")
                {
                    if (parts.Count == 0) throw Refuse("outside-repository");
                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part.Length > 0 && part != ".") parts.Add(part);
            }
            return string.Join("/", parts);
        }

        /// <summary>Refuses a JSON object (at any depth) that repeats a name, ignoring case.</summary>
        public static void AssertUniqueKeys(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in element.EnumerateObject())
                {
                    if (!keys.Add(p.Name)) throw Refuse("duplicate-input-map-key");
                    AssertUniqueKeys(p.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) AssertUniqueKeys(item);
        }

        private static void AssertKeys(JsonElement o, params string[] allowed)
        {
            if (o.ValueKind != JsonValueKind.Object) throw Refuse("invalid-catalog-object");
            foreach (var p in o.EnumerateObject()) if (Array.IndexOf(allowed, p.Name) < 0) throw Refuse("invalid-catalog-field:" + p.Name);
        }

        private static bool Has(JsonElement o, string name) => o.TryGetProperty(name, out _);
        private static JsonElement Get(JsonElement o, string name) => o.TryGetProperty(name, out var v) ? v : default;
        private static bool IsString(JsonElement v) => v.ValueKind == JsonValueKind.String;
        private static bool IsArray(JsonElement v) => v.ValueKind == JsonValueKind.Array;
        private static string Text(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static bool IsOne(JsonElement v) => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n == 1 && v.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) < 0;

        internal static string[] Sorted(IEnumerable<string> values) =>
            new SortedSet<string>(values.Where(v => v != null), StringComparer.Ordinal).ToArray();

        // ---- git ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Git for one repository: answers fixed by full commit ids (a tree listing, a tree id, ancestry, an object
        /// type) are kept, and blobs are read through one cat-file --batch child. Replace objects are disabled.
        /// </summary>
        public sealed class GitReader : IDisposable
        {
            private readonly string root;
            private readonly Dictionary<string, (int Exit, string Text)> memo = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
            private Process batch;
            private Stream batchOut;

            public GitReader(string repositoryRoot) { root = Path.GetFullPath(repositoryRoot); }

            public string Root => root;

            private ProcessStartInfo Start(params string[] args)
            {
                var start = new ProcessStartInfo("git")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = root,
                };
                start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
                start.Environment["GIT_TERMINAL_PROMPT"] = "0";
                foreach (var a in new[] { "-c", "core.fsmonitor=false", "-c", "gc.auto=0" }) start.ArgumentList.Add(a);
                foreach (var a in args) start.ArgumentList.Add(a);
                return start;
            }

            /// <summary>Runs git; exit codes outside allowed are refused.</summary>
            public (int Exit, string Text) Run(int[] allowed, params string[] args)
            {
                var key = string.Join("\0", args);
                if (memo.TryGetValue(key, out var kept)) return kept;
                using var process = Process.Start(Start(args));
                var output = new MemoryStream();
                var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(300_000))
                {
                    try { process.Kill(true); } catch (Exception) { }
                    throw Refuse("git-timeout: " + string.Join(" ", args));
                }
                process.WaitForExit();
                copy.Wait();
                var exit = process.ExitCode;
                if (Array.IndexOf(allowed, exit) < 0) throw Refuse("git-failed(" + exit + "): " + string.Join(" ", args) + ": " + error.Result.Trim());
                var answer = (exit, Decode(output.ToArray()));
                if (Fixed(args)) memo[key] = answer;
                return answer;
            }

            public string Run(params string[] args) => Run(new[] { 0 }, args).Text;

            private static bool FullSha(string s) => s != null && s.Length == 40 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

            private static bool Fixed(string[] a) =>
                (a.Length == 5 && a[0] == "ls-tree" && a[1] == "-r" && a[2] == "-z" && a[3] == "--full-tree" && FullSha(a[4]))
                || (a.Length == 2 && a[0] == "rev-parse" && a[1].EndsWith("^{tree}", StringComparison.Ordinal) && FullSha(a[1].Substring(0, a[1].Length - 7)))
                || (a.Length == 4 && a[0] == "merge-base" && a[1] == "--is-ancestor" && FullSha(a[2]) && FullSha(a[3]))
                || (a.Length == 3 && a[0] == "cat-file" && a[1] == "-t" && FullSha(a[2]));

            // Git's text is UTF-8; a BOM is dropped as a reader would, invalid bytes are refused.
            private static string Decode(byte[] bytes)
            {
                var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }

            private readonly Dictionary<string, string> blobs = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>The object (an object id or rev:path) as text, or null when git has no such object.</summary>
            public string Blob(string objectName)
            {
                if (blobs.TryGetValue(objectName, out var kept)) return kept;
                if (batch == null)
                {
                    var start = Start("cat-file", "--batch");
                    start.RedirectStandardInput = true;
                    batch = Process.Start(start);
                    batchOut = batch.StandardOutput.BaseStream;
                    _ = batch.StandardError.ReadToEndAsync();
                }
                var request = Encoding.UTF8.GetBytes(objectName + "\n");
                batch.StandardInput.BaseStream.Write(request, 0, request.Length);
                batch.StandardInput.BaseStream.Flush();
                var header = ReadLine();
                if (header.EndsWith(" missing", StringComparison.Ordinal) || header.EndsWith(" ambiguous", StringComparison.Ordinal)) return blobs[objectName] = null;
                var fields = header.Split(' ');
                if (fields.Length != 3 || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size)) throw Refuse("git-batch: " + header);
                var content = new byte[size];
                for (var read = 0; read < size;)
                {
                    var n = batchOut.Read(content, read, (int)Math.Min(size - read, 1 << 20));
                    if (n <= 0) throw Refuse("git-batch: truncated " + objectName);
                    read += n;
                }
                if (batchOut.ReadByte() != '\n') throw Refuse("git-batch: framing " + objectName);
                return blobs[objectName] = Decode(content);
            }

            private string ReadLine()
            {
                var bytes = new List<byte>();
                for (int b; (b = batchOut.ReadByte()) != '\n';)
                {
                    if (b < 0) throw Refuse("git-batch: closed");
                    bytes.Add((byte)b);
                }
                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            public void Dispose()
            {
                if (batch == null) return;
                try { batch.StandardInput.Close(); batch.WaitForExit(10_000); } catch (Exception) { }
                batch.Dispose();
                batch = null;
            }
        }

        // ---- one commit -------------------------------------------------------------------------------------------

        private static readonly Regex TreeLine = new Regex(@"^([0-7]{6}) (blob|commit) ([a-f0-9]{40})\t([^\x00-\x1f]+)$", RegexOptions.CultureInvariant);
        private static readonly Regex GitAttributes = new Regex(@"(^|/)\.gitattributes$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex NugetConfig = new Regex(@"(^|/)nuget\.config$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex IndexPathRefused = new Regex(@"[\\:*?\x00-\x1f]", RegexOptions.CultureInvariant);
        private static readonly string[] KnownSdks = { "Microsoft.NET.Sdk", "Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Worker", "Microsoft.NET.Sdk.Razor" };

        private sealed class TreeEntry
        {
            public string Mode, Type, ObjectId;
        }

        private sealed class SuiteBuild
        {
            public string Id, Project;
            public readonly Dictionary<string, RcInputRecord> Inputs = new Dictionary<string, RcInputRecord>(StringComparer.Ordinal);
            public readonly List<RcInputEdge> Edges = new List<RcInputEdge>();
        }

        private sealed class Reading
        {
            public Dictionary<string, TreeEntry> Tree;
            public Regex[] Noop = Array.Empty<Regex>();
            public readonly List<string> NoopConflicts = new List<string>();
            public readonly HashSet<string> NoopConflictKeys = new HashSet<string>(StringComparer.Ordinal);

            public bool IsNoop(string path) => Noop.Any(p => p.IsMatch(path));

            // A checked-in no-op still skips the project walk. An explicit owner root wins, and the overlap is a
            // noop-vs-owner conflict the census fails on.
            public void Add(SuiteBuild suite, string path, string reason, bool fromOwnerRoot = false)
            {
                if (IsNoop(path))
                {
                    if (!fromOwnerRoot) return;
                    if (NoopConflictKeys.Add(path + "|" + reason)) NoopConflicts.Add(path + " reason=" + reason);
                }
                if (suite.Inputs.TryGetValue(path, out var record))
                {
                    record.Reason = string.Join(";", Sorted(record.Reason.Split(';').Append(reason)));
                    return;
                }
                Tree.TryGetValue(path, out var entry);
                suite.Inputs[path] = new RcInputRecord
                {
                    Suite = suite.Id, Project = suite.Project, Path = path, Mode = entry?.Mode, Type = entry?.Type, ObjectId = entry?.ObjectId,
                    Present = entry != null, Reason = reason,
                };
            }
        }

        private static JsonElement ReadMap(GitReader git, TreeEntry entry, string tooLarge)
        {
            var text = git.Blob(entry.ObjectId) ?? throw Refuse("missing-object:" + entry.ObjectId);
            if (Encoding.UTF8.GetByteCount(text) > 1048576) throw Refuse(tooLarge);
            using var json = JsonDocument.Parse(text);
            AssertUniqueKeys(json.RootElement);
            return json.RootElement.Clone();
        }

        /// <summary>
        /// The input snapshot of a full commit id: suites from the ownership map (or its suiteProjects glob), each
        /// suite's owner roots and project closure (project tree, packages.lock.json, ancestor Directory.* files,
        /// imports and items), positive derived-index edges, and every path no suite owns. Anything the closure cannot
        /// read or resolve is an unresolved reason, never a silent omission.
        /// </summary>
        public static RcInputSnapshot Read(GitReader git, string sha, RcDerivedIndex derivedIndex = null)
        {
            if (sha == null || !Regex.IsMatch(sha, @"\A[a-f0-9]{40}\z") || git.Run(new[] { 0, 1, 128 }, "cat-file", "-t", sha).Text.Trim() != "commit")
                throw Refuse("full-commit-required");
            var reading = new Reading { Tree = new Dictionary<string, TreeEntry>(StringComparer.Ordinal) };
            var tree = reading.Tree;
            var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unresolved = new List<string>();
            foreach (var line in git.Run("ls-tree", "-r", "-z", "--full-tree", sha).Split('\0'))
            {
                if (line.Length == 0) continue;
                var m = TreeLine.Match(line);
                if (!m.Success) throw Refuse("invalid-input-tree");
                var path = m.Groups[4].Value;
                if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') || path.Split('/').Contains("..")) throw Refuse("invalid-input-path");
                if (!folded.Add(path)) unresolved.Add("case-collision:" + path);
                tree.Add(path, new TreeEntry { Mode = m.Groups[1].Value, Type = m.Groups[2].Value, ObjectId = m.Groups[3].Value });
                if (m.Groups[1].Value != "100644" && m.Groups[1].Value != "100755") unresolved.Add("unresolved-link-or-submodule:" + path);
                // Filter drivers / LFS may substitute bytes outside the committed tree.
                if (GitAttributes.IsMatch(path)) unresolved.Add("external-content-contract:" + path);
            }
            if (!tree.TryGetValue(MapPath, out var mapEntry) || mapEntry.Mode != "100644") throw Refuse("missing-regular-input-map");
            var map = ReadMap(git, mapEntry, "input-map-too-large");
            AssertKeys(map, "schemaVersion", "suites", "suiteProjects", "roots", "testOnly");
            if (!IsOne(Get(map, "schemaVersion"))) throw Refuse("input-map-version");
            if (!IsArray(Get(map, "roots")) || Has(map, "suites") == Has(map, "suiteProjects")) throw Refuse("input-map-shape");
            var paths = tree.Keys.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var suiteRows = new List<(JsonElement Id, JsonElement Project, bool FromJson)>();
            if (Has(map, "suites"))
            {
                if (!IsArray(map.GetProperty("suites"))) throw Refuse("input-map-suites");
                foreach (var row in map.GetProperty("suites").EnumerateArray())
                {
                    AssertKeys(row, "id", "project");
                    suiteRows.Add((Get(row, "id"), Get(row, "project"), true));
                }
            }
            var suites = new Dictionary<string, SuiteBuild>(StringComparer.Ordinal);
            var suiteFold = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddSuite(string id, string project)
            {
                if (string.IsNullOrWhiteSpace(id) || !suiteFold.Add(id) || project == null || !tree.ContainsKey(project) || !project.EndsWith(".csproj", StringComparison.Ordinal))
                    throw Refuse("invalid-input-suite");
                suites.Add(id, new SuiteBuild { Id = id, Project = project });
            }
            if (Has(map, "suites")) foreach (var (id, project, _) in suiteRows) AddSuite(Text(id), Text(project));
            else
            {
                var pattern = Pattern(Text(Get(map, "suiteProjects")) ?? Get(map, "suiteProjects").ToString());
                foreach (var path in paths) if (pattern.IsMatch(path) && path.EndsWith(".csproj", StringComparison.Ordinal)) AddSuite(path, path);
            }
            if (suites.Count == 0) throw Refuse("input-suites-empty");
            if (tree.TryGetValue(NoopPath, out var noopEntry))
            {
                if (noopEntry.Mode != "100644") throw Refuse("missing-regular-noop-map");
                var noop = ReadMap(git, noopEntry, "noop-map-too-large");
                AssertKeys(noop, "schemaVersion", "rows");
                if (!IsOne(Get(noop, "schemaVersion"))) throw Refuse("noop-map-version");
                if (!IsArray(Get(noop, "rows"))) throw Refuse("noop-map-shape");
                var patterns = new List<Regex>();
                foreach (var row in noop.GetProperty("rows").EnumerateArray())
                {
                    AssertKeys(row, "path", "reason", "evidence");
                    if (string.IsNullOrEmpty(Text(Get(row, "path"))) || string.IsNullOrEmpty(Text(Get(row, "reason"))) || string.IsNullOrEmpty(Text(Get(row, "evidence"))))
                        throw Refuse("invalid-noop-row");
                    patterns.Add(Pattern(Text(Get(row, "path"))));
                }
                reading.Noop = patterns.ToArray();
            }
            // ** with unmatched-path-fallback, or any ** whose consumers are *, is the retired broad fallback. Ignore it.
            // A ** row with reason repo-walk names the suites that read every non-no-op path.
            foreach (var root in map.GetProperty("roots").EnumerateArray())
            {
                AssertKeys(root, "path", "consumers", "reason", "except", "walker");
                var rootPath = Text(Get(root, "path"));
                var reason = Text(Get(root, "reason"));
                var consumers = Get(root, "consumers");
                if (rootPath == null || string.IsNullOrEmpty(reason) || !IsArray(consumers) || consumers.GetArrayLength() == 0) throw Refuse("invalid-input-root");
                if (Has(root, "except") && !IsArray(root.GetProperty("except"))) throw Refuse("invalid-input-root");
                if (Has(root, "walker") && root.GetProperty("walker").ValueKind != JsonValueKind.True && root.GetProperty("walker").ValueKind != JsonValueKind.False)
                    throw Refuse("invalid-input-root");
                var ids = new List<string>();
                foreach (var id in consumers.EnumerateArray())
                {
                    if (!IsString(id) || (id.GetString() != "*" && !suites.ContainsKey(id.GetString()))) throw Refuse("unknown-input-consumer");
                    ids.Add(id.GetString());
                }
                if (rootPath == "**")
                {
                    if (reason == "unmatched-path-fallback" || ids.Contains("*")) continue;
                    if (reason != "repo-walk") throw Refuse("invalid-broad-root");
                    foreach (var id in ids) foreach (var path in paths) reading.Add(suites[id], path, reason);
                    continue;
                }
                var pattern = Pattern(rootPath, NugetConfig.IsMatch(rootPath));
                var except = new List<Regex>();
                if (Has(root, "except"))
                    foreach (var item in root.GetProperty("except").EnumerateArray())
                    {
                        if (string.IsNullOrEmpty(Text(item))) throw Refuse("invalid-input-root");
                        except.Add(Pattern(item.GetString()));
                    }
                foreach (var id in ids)
                {
                    var targets = id == "*" ? suites.Values.ToArray() : new[] { suites[id] };
                    foreach (var path in paths)
                    {
                        if (!pattern.IsMatch(path) || except.Any(e => e.IsMatch(path))) continue;
                        foreach (var suite in targets) reading.Add(suite, path, reason, fromOwnerRoot: true);
                    }
                }
            }
            var documents = new Dictionary<string, XmlDocument>(StringComparer.Ordinal);
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var suite in suites.Values)
            {
                reading.Add(suite, MapPath, "ownership-contract");
                foreach (var path in new[] { "global.json", "NuGet.config", "Directory.Packages.props" }) reading.Add(suite, path, "root-build-contract");
                var projects = new Queue<string>();
                projects.Enqueue(suite.Project);
                var seenProjects = new HashSet<string>(StringComparer.Ordinal);
                while (projects.Count > 0)
                {
                    var project = projects.Dequeue();
                    if (!seenProjects.Add(project)) continue;
                    if (!tree.ContainsKey(project)) { unresolved.Add("missing-project:" + project); continue; }
                    var directory = DirectoryOf(project);
                    foreach (var path in paths) if (path.StartsWith(directory, StringComparison.Ordinal)) reading.Add(suite, path, "project-tree:" + project);
                    reading.Add(suite, directory + "packages.lock.json", "package-lock-or-resolved-receipt-required");
                    var queue = new Queue<string>();
                    queue.Enqueue(project);
                    for (var ancestor = directory.TrimEnd('/'); ; ancestor = ancestor.Substring(0, Math.Max(0, ancestor.LastIndexOf('/'))))
                    {
                        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                        {
                            var path = ancestor.Length > 0 ? ancestor + "/" + name : name;
                            reading.Add(suite, path, "ancestor-build-input");
                            if (tree.ContainsKey(path)) queue.Enqueue(path);
                        }
                        if (ancestor.Length == 0) break;
                    }
                    var seenDocuments = new HashSet<string>(StringComparer.Ordinal);
                    while (queue.Count > 0)
                    {
                        var input = queue.Dequeue();
                        if (!seenDocuments.Add(input)) continue;
                        reading.Add(suite, input, "project-or-import");
                        try
                        {
                            if (!tree.TryGetValue(input, out var entry) || entry.Type != "blob" || (entry.Mode != "100644" && entry.Mode != "100755")) throw Refuse("missing-regular-project-input");
                            if (!documents.TryGetValue(entry.ObjectId, out var document))
                            {
                                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 };
                                using var reader = XmlReader.Create(new StringReader(git.Blob(entry.ObjectId) ?? ""), settings);
                                document = new XmlDocument { XmlResolver = null };
                                document.Load(reader);
                                documents[entry.ObjectId] = document;
                            }
                            if (document.DocumentElement?.LocalName != "Project") throw Refuse("invalid-project-xml");
                            if (document.SelectNodes("//*[local-name()='Target' or local-name()='UsingTask' or local-name()='Sdk' or local-name()='MSBuild']").Count > 0)
                                unresolved.Add("dynamic-build:" + input);
                            foreach (XmlElement node in document.SelectNodes("//*[@Sdk]"))
                                if (Array.IndexOf(KnownSdks, node.GetAttribute("Sdk")) < 0) unresolved.Add("external-sdk:" + input);
                            foreach (XmlElement node in document.SelectNodes("//*[local-name()='Import' or local-name()='ProjectReference' or local-name()='Compile' or local-name()='Content' or local-name()='None' or local-name()='EmbeddedResource' or local-name()='AdditionalFiles' or local-name()='Analyzer']"))
                            {
                                var kind = node.LocalName;
                                var value = kind == "Import" ? node.GetAttribute("Project") : node.GetAttribute("Include");
                                if (string.IsNullOrEmpty(value)) continue; // Update/Remove never subtract from the conservative tree.
                                foreach (var part in value.Split(';'))
                                {
                                    try
                                    {
                                        var target = kind == "Import" ? ResolvePath(input, part) : ResolvePath(input, part, project);
                                        var pattern = Pattern(target);
                                        var destinations = target.IndexOfAny(new[] { '*', '?' }) < 0 ? new[] { target } : paths.Where(p => pattern.IsMatch(p)).ToArray();
                                        if (destinations.Length == 0 && (kind == "Import" || kind == "ProjectReference")) throw Refuse("missing-glob-target");
                                        foreach (var destination in destinations)
                                        {
                                            reading.Add(suite, destination, kind);
                                            suite.Edges.Add(new RcInputEdge { From = input, To = destination, Project = project, Reason = kind });
                                            if (!tree.ContainsKey(destination)) unresolved.Add("missing-input:" + destination);
                                            if (kind == "Import") queue.Enqueue(destination);
                                            if (kind == "ProjectReference")
                                            {
                                                projects.Enqueue(destination);
                                                if (destination.EndsWith(".csproj", StringComparison.Ordinal)) referenced.Add(destination);
                                            }
                                        }
                                    }
                                    catch (Exception e) when (!(e is OutOfMemoryException))
                                    {
                                        unresolved.Add("unresolved-" + kind.ToLowerInvariant() + ":" + input + ":" + e.Message);
                                    }
                                }
                            }
                        }
                        catch (Exception e) when (!(e is OutOfMemoryException)) { unresolved.Add("project-parse:" + input + ":" + e.Message); }
                    }
                }
            }
            // Positive index edges add records in BOTH revisions, even for an unchanged path. Unowned is computed after
            // them, so an index hit owns the path instead of flagging it.
            var indexBroadened = false;
            foreach (var edge in derivedIndex?.Edges ?? Array.Empty<RcDerivedIndexEdge>())
            {
                var path = edge?.Path;
                if (string.IsNullOrEmpty(path) || IndexPathRefused.IsMatch(path) || path.StartsWith("/", StringComparison.Ordinal) || path.Split('/').Contains(".."))
                {
                    unresolved.Add("index-invalid-input");
                    continue;
                }
                foreach (var id in edge.Suites ?? new string[] { null })
                {
                    if (id == null || !suites.TryGetValue(id, out var suite)) { unresolved.Add("index-unknown-consumer"); continue; }
                    if (!suite.Inputs.ContainsKey(path)) indexBroadened = true;
                    reading.Add(suite, path, "derived-index-positive");
                    suite.Edges.Add(new RcInputEdge { From = "derived-index", To = path, Project = suite.Project, Reason = "derived-index-positive" });
                    if (!tree.ContainsKey(path)) unresolved.Add("index-missing-input:" + path);
                }
            }
            var owned = new HashSet<string>(suites.Values.SelectMany(s => s.Inputs.Keys), StringComparer.Ordinal);
            var unowned = new List<RcUnownedPath>();
            string parentSha = null;
            var parentKnown = false;
            foreach (var path in paths)
            {
                if (owned.Contains(path) || reading.IsNoop(path)) continue;
                var hint = "unknown";
                if (path.EndsWith(".csproj", StringComparison.Ordinal))
                {
                    // new-csproj: no suite project, nobody references it, and the parent commit does not contain it.
                    if (!suites.Values.Any(s => s.Project == path) && !referenced.Contains(path))
                    {
                        if (!parentKnown)
                        {
                            parentKnown = true;
                            var parent = git.Run(new[] { 0, 128 }, "rev-parse", sha + "^");
                            parentSha = parent.Exit == 0 && Regex.IsMatch(parent.Text.Trim(), @"\A[a-f0-9]{40}\z") ? parent.Text.Trim() : null;
                        }
                        if (parentSha == null || git.Blob(parentSha + ":" + path) == null) hint = "new-csproj";
                    }
                }
                else if (path.StartsWith("docs/", StringComparison.Ordinal)) hint = "new-docs-folder";
                unowned.Add(new RcUnownedPath { Path = path, Hint = hint });
            }
            var decision = unowned.Count > 0 || unresolved.Count > 0;
            return new RcInputSnapshot
            {
                Sha = sha, TreeId = git.Run("rev-parse", sha + "^{tree}").Trim(), MapObjectId = mapEntry.ObjectId, IndexBroadened = indexBroadened,
                Suites = suites.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(id => suites[id]).Select(s => new RcInputSuite
                {
                    Id = s.Id, Project = s.Project, Inputs = s.Inputs.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => s.Inputs[k]).ToArray(), Edges = s.Edges.ToArray(),
                }).ToArray(),
                Unresolved = Sorted(unresolved), Unowned = unowned.ToArray(), NoopConflicts = Sorted(reading.NoopConflicts),
                NeedsOrchestratorDecision = decision, CreditBlockedBy = decision ? "unowned-paths" : null, NoopPatterns = reading.Noop,
            };
        }

        /// <summary>Read with a reader of its own for the repository.</summary>
        public static RcInputSnapshot Read(string repositoryRoot, string sha, RcDerivedIndex derivedIndex = null)
        {
            using var git = new GitReader(repositoryRoot);
            return Read(git, sha, derivedIndex);
        }

        /// <summary>The census lines: the count, the first 40 unowned paths with their hints, and the list file past 40.</summary>
        public static string[] UnownedCensusLines(IEnumerable<RcUnownedPath> unowned, string listPath)
        {
            var rows = (unowned ?? Array.Empty<RcUnownedPath>()).Where(r => r != null).ToArray();
            var lines = new List<string> { "UNOWNED-COUNT " + rows.Length.ToString(CultureInfo.InvariantCulture) };
            lines.AddRange(rows.Take(40).Select(r => "UNOWNED-PATH " + r.Hint + " " + r.Path));
            if (rows.Length > 40) lines.Add("UNOWNED-LIST " + listPath);
            return lines.ToArray();
        }

        // ---- two commits ------------------------------------------------------------------------------------------

        private static readonly Regex ClassDeclaration = new Regex(@"(?<![A-Za-z0-9_])class\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant);
        private static readonly Regex TestAttribute = new Regex(@"\[Test(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

        private static IEnumerable<string> CodeLines(string text)
        {
            foreach (var line in (text ?? "").Split('\n'))
            {
                var trim = line.Trim().TrimEnd('\r');
                if (trim.StartsWith("//", StringComparison.Ordinal) || trim.StartsWith("*", StringComparison.Ordinal) || trim.StartsWith("/*", StringComparison.Ordinal)) continue;
                yield return trim;
            }
        }

        private static Dictionary<string, int> ClassCounts(string text)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in CodeLines(text))
                foreach (Match m in ClassDeclaration.Matches(line))
                {
                    counts.TryGetValue(m.Groups[1].Value, out var n);
                    counts[m.Groups[1].Value] = n + 1;
                }
            return counts;
        }

        private static int TestAttributes(string text) => CodeLines(text).Sum(line => TestAttribute.Matches(line).Count);

        private sealed class TestOnlyPolicy
        {
            public bool Enabled;
            public Regex[] Paths = Array.Empty<Regex>(), ExceptPaths = Array.Empty<Regex>();
            public HashSet<string> ExceptReasons = new HashSet<string>(StringComparer.Ordinal), ProductEdges = new HashSet<string>(StringComparer.Ordinal);
            public (Regex Pattern, Regex[] Except, string Reason, bool Walker, string[] Consumers)[] Roots = Array.Empty<(Regex, Regex[], string, bool, string[])>();

            private static bool Carves((Regex Pattern, Regex[] Except, string Reason, bool Walker, string[] Consumers) root, string path) => root.Except.Any(e => e.IsMatch(path));

            public bool IsTestOnly(string path)
            {
                if (!Enabled || !Paths.Any(p => p.IsMatch(path)) || ExceptPaths.Any(p => p.IsMatch(path))) return false;
                return !Roots.Any(r => r.Pattern.IsMatch(path) && !Carves(r, path) && ExceptReasons.Contains(r.Reason));
            }

            // project-tree:<P> names the project whose directory holds the file: the suite's own tree stays test-only,
            // a referenced helper does not.
            public bool IsProductEdge(string reason, string ownProject) =>
                !string.IsNullOrEmpty(reason) && reason.Split(';').Any(part => ProductEdges.Contains(part)
                    || (part.StartsWith("project-tree:", StringComparison.Ordinal) && part.Length > 13 && part.Substring(13) != ownProject));

            public bool IsWalker(string suiteId, string path) =>
                Roots.Any(r => r.Walker && r.Pattern.IsMatch(path) && !Carves(r, path) && (r.Consumers.Contains(suiteId) || r.Consumers.Contains("*")));

            public bool IsStarWalker(string path) => Roots.Any(r => r.Walker && r.Consumers.Contains("*") && r.Pattern.IsMatch(path) && !Carves(r, path));
        }

        private static TestOnlyPolicy ReadTestOnlyPolicy(GitReader git, string sha)
        {
            var text = git.Blob(sha + ":" + MapPath) ?? throw Refuse("missing-regular-input-map");
            using var json = JsonDocument.Parse(text);
            var map = json.RootElement;
            if (!map.TryGetProperty("testOnly", out var policy) || policy.ValueKind == JsonValueKind.Null) return new TestOnlyPolicy();
            AssertKeys(policy, "enabled", "paths", "exceptPaths", "exceptReasons", "productEdgeReasons");
            var enabled = Get(policy, "enabled");
            if (enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False) throw Refuse("invalid-test-only-class");
            string[] Strings(string name)
            {
                var array = Get(policy, name);
                if (!IsArray(array)) throw Refuse("invalid-test-only-class");
                return array.EnumerateArray().Select(item => string.IsNullOrEmpty(Text(item)) ? throw Refuse("invalid-test-only-class") : item.GetString()).ToArray();
            }
            var paths = Strings("paths");
            var exceptPaths = Strings("exceptPaths");
            var exceptReasons = Strings("exceptReasons");
            var productEdges = Strings("productEdgeReasons");
            var roots = map.GetProperty("roots").EnumerateArray().Select(root =>
            {
                var path = Text(Get(root, "path"));
                var except = Has(root, "except") ? root.GetProperty("except").EnumerateArray().Select(e => Pattern(Text(e))).ToArray() : Array.Empty<Regex>();
                var walker = Get(root, "walker").ValueKind == JsonValueKind.True;
                var consumers = IsArray(Get(root, "consumers")) ? root.GetProperty("consumers").EnumerateArray().Select(Text).ToArray() : Array.Empty<string>();
                return (Pattern(path, NugetConfig.IsMatch(path ?? "")), except, Text(Get(root, "reason")) ?? "", walker, consumers);
            }).ToArray();
            return new TestOnlyPolicy
            {
                Enabled = enabled.ValueKind == JsonValueKind.True, Paths = paths.Select(p => Pattern(p)).ToArray(), ExceptPaths = exceptPaths.Select(p => Pattern(p)).ToArray(),
                ExceptReasons = new HashSet<string>(exceptReasons, StringComparer.Ordinal), ProductEdges = new HashSet<string>(productEdges, StringComparer.Ordinal), Roots = roots,
            };
        }

        private static RcInputRecord Absent(string suite, string project, string path) => new RcInputRecord
        {
            Suite = suite, Project = project, Path = path, Present = false, Reason = "absent-from-revision-closure",
        };

        /// <summary>
        /// The two snapshots of a baseline and a candidate, their changed paths and the dirty suites. A suite is dirty
        /// when it exists on one side only or its input records differ once each side records the other's paths as
        /// absent. A changed path on a positive index edge dirties its suites. With the test-only policy on, a suite
        /// whose changed inputs are all test-only (no product edge, no structural walker change, no new test attribute
        /// under a * walker) is deferred, and classes added by a test-only change are listed as changed but not run.
        /// </summary>
        public static RcInputManifest Manifest(string repositoryRoot, string baselineSha, string candidateSha, RcDerivedIndex derivedIndex = null)
        {
            using var git = new GitReader(repositoryRoot);
            var old = Read(git, baselineSha, derivedIndex);
            var @new = Read(git, candidateSha, derivedIndex);
            var unresolved = new List<string>(old.Unresolved.Concat(@new.Unresolved));
            if (git.Run(new[] { 0, 1 }, "merge-base", "--is-ancestor", baselineSha, candidateSha).Exit != 0) unresolved.Add("baseline-not-ancestor");
            if (old.MapObjectId != @new.MapObjectId) unresolved.Add("ownership-map-changed");
            var dirty = new SortedSet<string>(StringComparer.Ordinal);
            var changes = new List<RcInputChange>();
            var diff = git.Run("diff", "--no-ext-diff", "--no-renames", "--name-status", "-z", baselineSha, candidateSha, "--").Split('\0');
            for (var i = 0; i < diff.Length - 1; i += 2)
            {
                if (!Regex.IsMatch(diff[i], @"\A[ADMTUXB]\z") || diff[i + 1].Length == 0) throw Refuse("invalid-input-diff");
                changes.Add(new RcInputChange { Status = diff[i], Path = diff[i + 1] });
            }
            // The union records absence explicitly and includes old edges, added inputs and retired inputs.
            var ids = Sorted(old.Suites.Select(s => s.Id).Concat(@new.Suites.Select(s => s.Id)));
            var order = StringComparer.Create(CultureInfo.CurrentCulture, false);
            foreach (var id in ids)
            {
                var left = old.Suites.FirstOrDefault(s => s.Id == id);
                var right = @new.Suites.FirstOrDefault(s => s.Id == id);
                if (left == null || right == null) { dirty.Add(id); continue; }
                var union = Sorted(left.Inputs.Concat(right.Inputs).Select(r => r.Path));
                foreach (var side in new[] { left, right })
                {
                    var present = new HashSet<string>(side.Inputs.Select(r => r.Path), StringComparer.Ordinal);
                    side.Inputs = side.Inputs.Concat(union.Where(p => !present.Contains(p)).Select(p => Absent(id, side.Project, p))).OrderBy(r => r.Path, order).ToArray();
                }
                var byPath = right.Inputs.ToDictionary(r => r.Path, StringComparer.Ordinal);
                if (left.Inputs.Length != right.Inputs.Length || left.Inputs.Any(r => !byPath.TryGetValue(r.Path, out var other) || !r.SameAs(other))) dirty.Add(id);
            }
            var broadened = old.IndexBroadened || @new.IndexBroadened;
            var changedPaths = new HashSet<string>(changes.Select(c => c.Path), StringComparer.Ordinal);
            foreach (var edge in derivedIndex?.Edges ?? Array.Empty<RcDerivedIndexEdge>())
            {
                if (edge?.Path == null || !changedPaths.Contains(edge.Path)) continue;
                foreach (var id in edge.Suites ?? new string[] { null })
                {
                    if (id == null || Array.IndexOf(ids, id) < 0) { unresolved.Add("index-unknown-consumer"); continue; }
                    if (dirty.Add(id)) broadened = true;
                }
            }
            var unownedByPath = new Dictionary<string, RcUnownedPath>(StringComparer.Ordinal);
            foreach (var row in (old.Unowned ?? Array.Empty<RcUnownedPath>()).Concat(@new.Unowned ?? Array.Empty<RcUnownedPath>()))
                if (!string.IsNullOrEmpty(row?.Path)) unownedByPath[row.Path] = row;
            var policy = ReadTestOnlyPolicy(git, candidateSha);
            var deferred = new List<string>();
            var notRun = new List<string>();
            if (policy.Enabled)
            {
                string FileText(string sha, string path) => git.Blob(sha + ":" + path) ?? "";
                var status = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var change in changes) status[change.Path] = change.Status;
                foreach (var id in dirty.ToArray())
                {
                    var before = old.Suites.FirstOrDefault(s => s.Id == id);
                    var after = @new.Suites.FirstOrDefault(s => s.Id == id);
                    if (before == null || after == null) continue;
                    var changed = ChangedInputPaths(before, after);
                    if (changed.Length == 0) continue;
                    var keep = false;
                    var deferredPaths = new List<string>();
                    foreach (var path in changed)
                    {
                        if (!policy.IsTestOnly(path)) { keep = true; continue; }
                        deferredPaths.Add(path);
                        var record = after.Inputs.Concat(before.Inputs).FirstOrDefault(r => r.Path == path && r.Present);
                        if (record != null && policy.IsProductEdge(record.Reason, after.Project)) { keep = true; continue; }
                        // A file add/remove/type change or a class-declaration multiset change selects walkers; a method
                        // body or signature edit does not.
                        var changeStatus = status.TryGetValue(path, out var s) ? s : "M";
                        var structural = changeStatus == "A" || changeStatus == "D" || changeStatus == "T" || !SameCounts(ClassCounts(FileText(baselineSha, path)), ClassCounts(FileText(candidateSha, path)));
                        if (structural && policy.IsWalker(id, path)) keep = true;
                        if (policy.IsStarWalker(path) && TestAttributes(FileText(candidateSha, path)) > TestAttributes(FileText(baselineSha, path))) keep = true;
                    }
                    if (keep) continue;
                    dirty.Remove(id);
                    deferred.AddRange(deferredPaths.Select(p => id + ":" + p));
                }
                foreach (var change in changes)
                {
                    if (!policy.IsTestOnly(change.Path)) continue;
                    var before = ClassCounts(FileText(baselineSha, change.Path));
                    var added = new List<string>();
                    foreach (var pair in ClassCounts(FileText(candidateSha, change.Path)))
                    {
                        before.TryGetValue(pair.Key, out var seen);
                        for (var n = seen; n < pair.Value; n++) added.Add(pair.Key);
                    }
                    if (added.Count == 0) continue;
                    var suiteId = ContainingSuiteId(change.Path, @new.Suites);
                    if (suiteId == null) continue;
                    notRun.AddRange(added.Select(name => suiteId + ":" + change.Path + ":" + name));
                }
            }
            var manifestUnowned = unownedByPath.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => unownedByPath[k]).ToArray();
            var sortedUnresolved = Sorted(unresolved);
            var decision = manifestUnowned.Length > 0 || sortedUnresolved.Length > 0;
            return new RcInputManifest
            {
                BaselineSha = baselineSha, CandidateSha = candidateSha, Snapshots = new[] { old, @new }, Changes = changes.ToArray(), DirtySuites = dirty.ToArray(),
                Unresolved = sortedUnresolved, Unowned = manifestUnowned, NeedsOrchestratorDecision = decision, TestOnlyDeferred = Sorted(deferred),
                ChangedNotRun = Sorted(notRun), NoopConflicts = Sorted(old.NoopConflicts.Concat(@new.NoopConflicts).Where(r => !string.IsNullOrEmpty(r))),
                CreditBlockedBy = decision ? "unowned-paths" : null, Index = new RcInputIndexUse { Version = derivedIndex?.Version, Broadened = broadened },
            };
        }

        private static bool SameCounts(Dictionary<string, int> left, Dictionary<string, int> right) =>
            left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var n) && n == pair.Value);

        private static string[] ChangedInputPaths(RcInputSuite left, RcInputSuite right)
        {
            var before = new Dictionary<string, RcInputRecord>(StringComparer.Ordinal);
            var after = new Dictionary<string, RcInputRecord>(StringComparer.Ordinal);
            foreach (var r in left.Inputs) if (r != null) before[r.Path] = r;
            foreach (var r in right.Inputs) if (r != null) after[r.Path] = r;
            return Sorted(before.Keys.Concat(after.Keys)).Where(path =>
                !before.TryGetValue(path, out var l) || !after.TryGetValue(path, out var r) || l.ObjectId != r.ObjectId || l.Present != r.Present).ToArray();
        }

        // The suite whose project directory is the longest prefix of the path.
        private static string ContainingSuiteId(string path, IEnumerable<RcInputSuite> suites)
        {
            string best = null;
            var length = -1;
            foreach (var suite in suites)
            {
                var directory = DirectoryOf(suite.Project ?? "");
                if (directory.Length > 0 && path.StartsWith(directory, StringComparison.Ordinal) && directory.Length > length) { best = suite.Id; length = directory.Length; }
            }
            return best;
        }
    }
}
