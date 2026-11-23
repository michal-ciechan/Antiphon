// CARD-1820 S1R5: the cross-language script closure over a supplied immutable inventory. It never runs what it reads.
// It replaced scripts/lib/release-impact-scripts.ps1 (its 33 functions and the JSON adapter), deleted 2026-11-23 in
// unit 17 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; ReleaseImpactScriptTests calls it in process.
//
// Pure analysis: no Git, no checkout enumeration, no execution. A consumer (a test class) is seeded from the string
// literals of its C# declaration sources (whole-literal paths, quoted or embedded command paths, and ScriptHarness
// basenames), then closed breadth first over each reached file's dependencies:
// - PowerShell (.ps1, .psm1), read by PsReader (ReleaseImpactScriptReader.cs): dot-source and call targets, script
//   paths run as commands, Import-Module, Add-Type sources, pwsh/powershell -File, bash/sh/node/python script
//   arguments, and any constant path expression elsewhere (Join-Path, Split-Path, string concatenation, expandable
//   strings, [IO.Path]::Combine and GetDirectoryName) that reaches an inventory path or a project directory;
// - project files (.csproj): the project directory's inputs (build outputs excluded) and literal item includes;
// - other scripts (.psd1, .sh, .bash, .js, .mjs, .cjs, .py): exact repo-relative tokens.
// Values are evaluated with $PSScriptRoot and $PSCommandPath anchored at the file, every assignment of a variable in
// the file unioned, and a constant foreach binding its loop variable; at most 64 values and a depth of 24.
// Positive edges are additive. Any dynamic, ambiguous, missing, unparsed, external, escaping or budget-cut closure
// keeps a path-specific reason (script-target-unresolved:<caller>:<detail>; a detail over 160 characters keeps a
// 128-character prefix and 16 hex digits of the whole detail's SHA-256). The result is always incomplete with
// fallback Full: S1R6 composes it with the release selector, which does not call it yet.
// Strings in and a record out: no file, process, clock or environment access. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A test class to close: its project, full name and the C# sources that declare it.</summary>
    public sealed record ReleaseScriptConsumer(string Project, string FullName, IReadOnlyList<string> DeclarationPaths);

    /// <summary>A class that reaches a path: every way it does (kinds, ordinal) and the first chain from its declaration.</summary>
    public sealed record ReleaseScriptEdgeClass(string Project, string FullName, IReadOnlyList<string> Kinds, IReadOnlyList<string> Chain);

    /// <summary>An inventory path and the classes that reach it (ordinal by project::fullName).</summary>
    public sealed record ReleaseScriptEdge(string Path, IReadOnlyList<ReleaseScriptEdgeClass> Classes);

    /// <summary>The closure: never complete, fallback Full, the reasons (ordinal) and the edges (ordinal by path).</summary>
    public sealed record ReleaseScriptClosure(
        string Schema, bool Complete, string Fallback, IReadOnlyList<string> IncompleteReasons, IReadOnlyList<ReleaseScriptEdge> Edges)
    {
        /// <summary>The antiphon-release-script-closure-v1 document: schema, complete, fallback, incompleteReasons, edges.</summary>
        public string ToJson()
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", Schema);
                writer.WriteBoolean("complete", Complete);
                writer.WriteString("fallback", Fallback);
                writer.WriteStartArray("incompleteReasons");
                foreach (var reason in IncompleteReasons) writer.WriteStringValue(reason);
                writer.WriteEndArray();
                writer.WriteStartArray("edges");
                foreach (var edge in Edges)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", edge.Path);
                    writer.WriteStartArray("classes");
                    foreach (var entry in edge.Classes)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("project", entry.Project);
                        writer.WriteString("fullName", entry.FullName);
                        writer.WriteStartArray("kinds");
                        foreach (var kind in entry.Kinds) writer.WriteStringValue(kind);
                        writer.WriteEndArray();
                        writer.WriteStartArray("chain");
                        foreach (var link in entry.Chain) writer.WriteStringValue(link);
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>The release script closure (CARD-1820 S1R5): analysis of a supplied inventory, never execution.</summary>
    public static class ReleaseImpactScripts
    {
        /// <summary>The closure document's schema.</summary>
        public const string Schema = "antiphon-release-script-closure-v1";

        /// <summary>At most this many values per expression.</summary>
        public const int ValueCap = 64;

        /// <summary>At most this evaluation depth.</summary>
        public const int DepthCap = 24;

        /// <summary>The visit budget per consumer when none is given.</summary>
        public const int DefaultVisits = 10000;

        /// <summary>A reason detail longer than this keeps a fixed prefix plus a digest of the whole detail.</summary>
        public const int DetailCap = 160;

        /// <summary>The prefix a capped detail keeps.</summary>
        public const int DetailPrefix = 128;

        private const string Anchor = "|R|";
        private const string Outside = "|outside|";

        private static readonly string[] Extensions = { ".ps1", ".psm1", ".psd1", ".sh", ".bash", ".js", ".mjs", ".cjs", ".py" };

        // Switch parameters of the commands read below; a switch never consumes the next element.
        private static readonly string[] Switches =
        {
            "force", "global", "passthru", "disablenamechecking", "noclobber", "ascustomobject", "skipeditioncheck",
            "usewindowspowershell", "ignorewarnings", "noprofile", "nologo", "noninteractive", "noexit", "sta", "mta",
            "interactive", "login", "wait", "nonewwindow",
        };

        private static readonly string[] PathSwitches = { "resolve", "parent", "leaf", "leafbase", "extension", "qualifier", "noqualifier", "isabsolute" };

        // C# string literals, comments and char literals. A lexer hint only: a seed it misses is never a negative
        // proof, because the result is never complete.
        private static readonly Regex CSharpLexer = new Regex(
            @"(?<c>//[^\n]*|/\*[\s\S]*?\*/)" +
            @"|(?<raw>\$*(?<q>""{3,})[\s\S]*?\k<q>)" +
            @"|(?<verb>(?:\$+@|@\$+|@)""(?:[^""]|"""")*"")" +
            @"|(?<reg>\$*""(?:[^""\\\n]|\\.)*"")" +
            @"|(?<ch>'(?:[^'\\\n]|\\.){1,8}')",
            RegexOptions.CultureInvariant);

        private static readonly Regex Escape = new Regex(@"\\(.)", RegexOptions.CultureInvariant);
        private static readonly Regex DoubledBraces = new Regex(@"\{\{|\}\}", RegexOptions.CultureInvariant);
        private static readonly Regex Hole = new Regex(@"\{[^{}]*\}", RegexOptions.CultureInvariant);
        private static readonly Regex Quoted = new Regex("'([^'\\r\\n]+)'|\"([^\"\\r\\n]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex Separators = new Regex("[\\s()\\[\\];,=|&<>`'\"]+", RegexOptions.CultureInvariant);
        private static readonly Regex Drive = new Regex("^[A-Za-z]:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Whitespace = new Regex(@"\s", RegexOptions.CultureInvariant);
        private static readonly Regex Wildcard = new Regex("[*?$%{}]", RegexOptions.CultureInvariant);
        private static readonly Regex Scheme = new Regex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ModuleQualified = new Regex(@"^[a-z][a-z0-9_.]*\\[a-z][a-z0-9_-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ReadCommands = new Regex(
            @"^(import-module|ipmo|invoke-expression|iex|add-type|pwsh|pwsh\.exe|powershell|powershell\.exe|bash|sh|node|python|python3)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PathType = new Regex(@"^(System\.)?IO\.Path$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ProjectItems = new Regex(
            "<(?:Compile|Content|None|EmbeddedResource|ProjectReference|Import)\\b[^>]*?\\b(?:Include|Project)\\s*=\\s*\"([^\"]*)\"",
            RegexOptions.CultureInvariant);
        private static readonly Regex ProjectWildcard = new Regex("[*?$%@]", RegexOptions.CultureInvariant);

        /// <summary>
        /// Closes each consumer over the inventory (repo-relative path to content; null content means a file whose text
        /// was not supplied). A non-positive maxVisits means the default budget. Throws when a path is not repo-relative.
        /// </summary>
        public static ReleaseScriptClosure Close(IReadOnlyDictionary<string, string> files, IReadOnlyList<ReleaseScriptConsumer> consumers, int maxVisits = 0)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (consumers == null) throw new ArgumentNullException(nameof(consumers));
            if (maxVisits <= 0) maxVisits = DefaultVisits;
            var inventory = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in files)
            {
                string path = pair.Key.Replace('\\', '/');
                var normal = ToPath(Anchor + path);
                if (normal.Kind != PathKind.Anchored || !string.Equals(normal.Path, path, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("release-impact script inventory path is not repo-relative: " + path, nameof(files));
                inventory[path] = pair.Value;
            }
            var graph = new Graph(inventory);
            var edges = new SortedDictionary<string, SortedDictionary<string, EdgeEntry>>(StringComparer.Ordinal);
            foreach (var consumer in consumers)
            {
                string project = consumer.Project ?? "";
                string fullName = consumer.FullName ?? "";
                string identity = project + "::" + fullName;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var queue = new Queue<(string Path, string[] Chain)>();
                void Record(string target, string kind, string[] chain)
                {
                    if (!edges.TryGetValue(target, out var classes))
                        edges[target] = classes = new SortedDictionary<string, EdgeEntry>(StringComparer.Ordinal);
                    if (!classes.TryGetValue(identity, out var entry))
                        classes[identity] = entry = new EdgeEntry { Project = project, FullName = fullName, Chain = chain };
                    entry.Kinds.Add(kind);
                }
                foreach (string raw in Sorted(consumer.DeclarationPaths ?? Array.Empty<string>()))
                {
                    string declaration = (raw ?? "").Replace('\\', '/');
                    if (!inventory.ContainsKey(declaration))
                    {
                        graph.Reason(declaration, "content-unavailable");
                        continue;
                    }
                    var seeds = Seeds(graph, declaration);
                    foreach (string seed in Sorted(seeds.Keys))
                    {
                        Record(seed, seeds[seed], new[] { declaration, seed });
                        if (visited.Add(seed)) queue.Enqueue((seed, new[] { declaration, seed }));
                    }
                }
                int expanded = 0;
                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();
                    if (expanded >= maxVisits)
                    {
                        // Bound exhaustion: this node's own closure is unknown, never truncated-complete.
                        graph.Reason(node.Path, "budget");
                        continue;
                    }
                    expanded++;
                    foreach (var dependency in Dependencies(graph, node.Path))
                    {
                        var chain = node.Chain.Append(dependency.Path).ToArray();
                        Record(dependency.Path, "script:closure", chain);
                        if (visited.Add(dependency.Path)) queue.Enqueue((dependency.Path, chain));
                    }
                }
            }
            var reasons = new SortedSet<string>(StringComparer.Ordinal) { "script-closure-uncertified" };
            reasons.UnionWith(graph.Reasons);
            var edgeList = edges.Select(edge => new ReleaseScriptEdge(edge.Key, edge.Value.Values
                    .Select(entry => new ReleaseScriptEdgeClass(entry.Project, entry.FullName, entry.Kinds.ToArray(), entry.Chain))
                    .ToArray()))
                .ToArray();
            return new ReleaseScriptClosure(Schema, false, "Full", reasons.ToArray(), edgeList);
        }

        private sealed class EdgeEntry
        {
            internal string Project;
            internal string FullName;
            internal string[] Chain;
            internal readonly SortedSet<string> Kinds = new SortedSet<string>(StringComparer.Ordinal);
        }

        private sealed class Graph
        {
            internal Graph(Dictionary<string, string> files)
            {
                Files = files;
                foreach (string path in files.Keys)
                {
                    string directory = DirectoryOf(path);
                    if (path.EndsWith(".csproj", StringComparison.Ordinal)) Projects.Add(directory);
                    string leaf = LeafOf(path);
                    AddTo(Leaves, leaf, path);
                    // Case-folded views exist only to name a casing miss; they never resolve a target silently.
                    AddTo(FoldedFiles, path, path);
                    AddTo(FoldedLeaves, leaf, path);
                }
            }

            internal readonly Dictionary<string, string> Files;
            internal readonly HashSet<string> Projects = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<string>> Leaves = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<string>> FoldedFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, List<string>> FoldedLeaves = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            internal readonly SortedSet<string> Reasons = new SortedSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<(string Path, string Kind)>> Dependencies =
                new Dictionary<string, List<(string Path, string Kind)>>(StringComparer.Ordinal);

            private static void AddTo(Dictionary<string, List<string>> map, string key, string path)
            {
                if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
                list.Add(path);
            }

            internal void Reason(string caller, string detail)
            {
                if (detail.Length > DetailCap)
                {
                    string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(detail))).ToLowerInvariant();
                    detail = detail.Substring(0, DetailPrefix) + "~" + hex.Substring(0, 16);
                }
                Reasons.Add("script-target-unresolved:" + caller + ":" + detail);
            }
        }

        // ---- paths ----

        private enum PathKind { Anchored, Outside, Unknown, Relative }

        private readonly record struct ScriptPath(PathKind Kind, string Path);

        private static string[] Sorted(IEnumerable<string> values)
        {
            var array = values.ToArray();
            Array.Sort(array, StringComparer.Ordinal);
            return array;
        }

        private static string DirectoryOf(string path)
        {
            int index = path.LastIndexOf('/');
            return index < 0 ? "" : path.Substring(0, index);
        }

        private static string LeafOf(string path)
        {
            int index = path.LastIndexOf('/');
            return index < 0 ? path : path.Substring(index + 1);
        }

        private static bool IsScriptExtension(string path)
        {
            string leaf = LeafOf(path).ToLowerInvariant();
            foreach (string extension in Extensions)
                if (leaf.EndsWith(extension, StringComparison.Ordinal)) return true;
            return false;
        }

        // Classifies an evaluated string: an anchored repo path, escaping or absolute, unknown, or unanchored text.
        private static ScriptPath ToPath(string value)
        {
            string text = value.Replace('\\', '/');
            if (text.StartsWith(Outside, StringComparison.Ordinal)) return new ScriptPath(PathKind.Outside, text);
            if (text.StartsWith(Anchor, StringComparison.Ordinal))
            {
                string rest = text.Substring(Anchor.Length);
                if (rest.Contains(Anchor, StringComparison.Ordinal) || rest.Contains(Outside, StringComparison.Ordinal))
                    return new ScriptPath(PathKind.Unknown, text);
                var parts = new List<string>();
                foreach (string segment in rest.Split('/'))
                {
                    if (segment.Length == 0 || segment == ".") continue;
                    if (segment == "..")
                    {
                        if (parts.Count == 0) return new ScriptPath(PathKind.Outside, text);
                        parts.RemoveAt(parts.Count - 1);
                        continue;
                    }
                    parts.Add(segment);
                }
                return new ScriptPath(PathKind.Anchored, string.Join("/", parts));
            }
            if (text.Contains(Anchor, StringComparison.Ordinal)) return new ScriptPath(PathKind.Unknown, text);
            if (text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith("~", StringComparison.Ordinal) || Drive.IsMatch(text))
                return new ScriptPath(PathKind.Outside, text);
            return new ScriptPath(PathKind.Relative, text);
        }

        private static string JoinValue(string left, string right)
        {
            right = right.Replace('\\', '/');
            if (right.StartsWith(Anchor, StringComparison.Ordinal) || right.StartsWith(Outside, StringComparison.Ordinal)) return right;
            left = left.Replace('\\', '/');
            if (left.EndsWith("/", StringComparison.Ordinal) || left.EndsWith(Anchor, StringComparison.Ordinal)) return left + right.TrimStart('/');
            return left + "/" + right.TrimStart('/');
        }

        private static string ParentValue(string value)
        {
            var path = ToPath(value);
            if (path.Kind != PathKind.Anchored) return null;
            if (path.Path.Length == 0) return Outside;
            return Anchor + DirectoryOf(path.Path);
        }

        // ---- values ----

        private static string[] Product(string[] left, string[] right, Func<string, string, string> combine)
        {
            if (left == null || right == null) return null;
            var values = new List<string>();
            foreach (string a in left)
            {
                foreach (string b in right)
                {
                    values.Add(combine(a, b));
                    if (values.Count > ValueCap) return null;
                }
            }
            return values.ToArray();
        }

        private static bool AddValues(List<string> target, string[] values)
        {
            if (values == null) return false;
            target.AddRange(values);
            return target.Count <= ValueCap;
        }

        private static string Concat(string a, string b) => a + b;

        private sealed class Context
        {
            internal Context(string path, string text, List<PsAst> nodes)
            {
                Path = path;
                Text = text;
                Directory = DirectoryOf(path);
                foreach (var node in nodes)
                {
                    if (node.Kind == PsKind.Parameter)
                    {
                        if (node.Child != null) Parameters.Add(VariableName(node.Child));
                        continue;
                    }
                    if (node.Kind != PsKind.Assignment) continue;
                    var left = node.Left;
                    if (left != null && left.Kind == PsKind.Convert) left = left.Child;
                    if (left == null || left.Kind != PsKind.Variable) continue;
                    string name = VariableName(left);
                    if (!Assignments.TryGetValue(name, out var list)) Assignments[name] = list = new List<PsAst>();
                    list.Add(node);
                }
            }

            internal readonly string Path;
            internal readonly string Directory;
            internal readonly string Text;
            internal readonly Dictionary<string, List<PsAst>> Assignments = new Dictionary<string, List<PsAst>>(StringComparer.Ordinal);
            internal readonly HashSet<string> Parameters = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> Resolving = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, string[]> Memo = new Dictionary<string, string[]>(StringComparer.Ordinal);
            internal readonly Dictionary<PsAst, string[]> Loops = new Dictionary<PsAst, string[]>(ReferenceEqualityComparer.Instance);
            internal int Depth;

            internal string ExtentText(PsAst node) => Text.Substring(node.Start, node.End - node.Start);
        }

        private static string VariableName(PsAst variable)
        {
            string name = variable.Value ?? "";
            foreach (string scope in new[] { "script:", "global:", "local:", "private:" })
                if (name.StartsWith(scope, StringComparison.OrdinalIgnoreCase)) name = name.Substring(scope.Length);
            return name.ToLowerInvariant();
        }

        private static string[] Resolve(Context context, PsAst variable)
        {
            string name = VariableName(variable);
            if (name == "psscriptroot") return new[] { Anchor + context.Directory };
            if (name == "pscommandpath") return new[] { Anchor + context.Path };
            // A constant foreach binds its loop variable to the bounded array it walks.
            for (var parent = variable.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.Kind == PsKind.ForEach && parent.Child != null && VariableName(parent.Child) == name
                    && !ReferenceEquals(parent.Child, variable))
                {
                    if (!context.Loops.ContainsKey(parent)) context.Loops[parent] = Value(context, parent.Condition);
                    return context.Loops[parent];
                }
            }
            if (context.Parameters.Contains(name) || !context.Assignments.ContainsKey(name) || context.Resolving.Contains(name)) return null;
            if (context.Memo.TryGetValue(name, out var memo)) return memo;
            context.Resolving.Add(name);
            string[] result = null;
            try
            {
                var values = new List<string>();
                bool known = true;
                foreach (var assignment in context.Assignments[name])
                {
                    if (assignment.Value != "=" || !AddValues(values, Value(context, assignment.Right)))
                    {
                        known = false;
                        break;
                    }
                }
                if (known) result = values.ToArray();
            }
            finally
            {
                context.Resolving.Remove(name);
            }
            // Every assignment in the file is unioned; one unknown assignment makes the variable unknown.
            context.Memo[name] = result;
            return result;
        }

        // A composite path expression is evaluated once, at its outermost composing node.
        private static bool IsOutermost(PsAst node)
        {
            var parent = node.Parent;
            while (parent != null && (parent.Kind == PsKind.Paren || parent.Kind == PsKind.Pipeline || parent.Kind == PsKind.CommandExpression
                       || parent.Kind == PsKind.ArrayLiteral))
                parent = parent.Parent;
            if (parent == null) return true;
            if (parent.Kind == PsKind.Binary || parent.Kind == PsKind.ExpandableString || parent.Kind == PsKind.InvokeMember
                || parent.Kind == PsKind.Convert)
                return false;
            if (parent.Kind == PsKind.Command)
            {
                string name = parent.CommandName;
                if (name != null && (IsCommand(name, "Join-Path") || IsCommand(name, "Split-Path"))) return false;
            }
            return true;
        }

        private static bool IsCommand(string name, string command) => string.Equals(name, command, StringComparison.OrdinalIgnoreCase);

        private sealed class Arguments
        {
            // A null value is a switch (PowerShell's $true).
            internal readonly List<KeyValuePair<string, PsAst>> Named = new List<KeyValuePair<string, PsAst>>();
            internal readonly List<PsAst> Positional = new List<PsAst>();

            internal bool HasNamed(string key) => Named.Any(pair => pair.Key == key);

            internal void SetNamed(string key, PsAst value)
            {
                int index = Named.FindIndex(pair => pair.Key == key);
                if (index >= 0) Named[index] = new KeyValuePair<string, PsAst>(key, value);
                else Named.Add(new KeyValuePair<string, PsAst>(key, value));
            }
        }

        private static Arguments ReadArguments(PsAst command, string[] switches)
        {
            var arguments = new Arguments();
            var elements = command.Items;
            for (int i = 1; i < elements.Count; i++)
            {
                var element = elements[i];
                if (element.Kind == PsKind.CommandParameter)
                {
                    string key = (element.Value ?? "").ToLowerInvariant();
                    bool isSwitch = switches.Any(candidate => candidate.StartsWith(key, StringComparison.Ordinal));
                    if (element.Child != null) arguments.SetNamed(key, element.Child);
                    else if (!isSwitch && i + 1 < elements.Count && elements[i + 1].Kind != PsKind.CommandParameter)
                    {
                        arguments.SetNamed(key, elements[i + 1]);
                        i++;
                    }
                    else arguments.SetNamed(key, null);
                    continue;
                }
                arguments.Positional.Add(element);
            }
            return arguments;
        }

        private static PsAst FindArgument(Arguments arguments, string[] names, int position)
        {
            foreach (string name in names)
                foreach (var pair in arguments.Named)
                    if (name.StartsWith(pair.Key, StringComparison.Ordinal) && pair.Value != null) return pair.Value;
            if (position >= 0 && position < arguments.Positional.Count) return arguments.Positional[position];
            return null;
        }

        private static string[] CommandValue(Context context, PsAst command)
        {
            string name = command.CommandName;
            if (name == null || command.Invocation != '\0') return null;
            var arguments = ReadArguments(command, PathSwitches);
            switch (name.ToLowerInvariant())
            {
                case "join-path":
                {
                    var left = FindArgument(arguments, new[] { "path" }, 0);
                    var right = FindArgument(arguments, new[] { "childpath" }, 1);
                    if (left == null || right == null || arguments.Positional.Count > 2) return null;
                    var leftValues = Value(context, left);
                    var rightValues = Value(context, right);
                    return Product(leftValues, rightValues, JoinValue);
                }
                case "split-path":
                {
                    if (arguments.HasNamed("leaf") || arguments.HasNamed("qualifier") || arguments.HasNamed("noqualifier")) return null;
                    var target = FindArgument(arguments, new[] { "path", "literalpath" }, 0);
                    if (target == null) return null;
                    return Parents(Value(context, target));
                }
            }
            return null;
        }

        private static string[] Parents(string[] values)
        {
            if (values == null) return null;
            var parents = new List<string>();
            foreach (string value in values)
            {
                string parent = ParentValue(value);
                if (parent == null) return null;
                parents.Add(parent);
            }
            return parents.ToArray();
        }

        private static string[] Value(Context context, PsAst ast)
        {
            if (ast == null) return null;
            if (context.Depth >= DepthCap) return null;
            context.Depth++;
            try
            {
                return ValueCore(context, ast);
            }
            finally
            {
                context.Depth--;
            }
        }

        private static string[] ValueCore(Context context, PsAst ast)
        {
            switch (ast.Kind)
            {
                case PsKind.StringConstant:
                    return new[] { ast.Value ?? "" };
                case PsKind.ExpandableString:
                    return ExpandableValue(context, ast);
                case PsKind.Variable:
                    return Resolve(context, ast);
                case PsKind.Paren:
                case PsKind.Convert:
                case PsKind.CommandExpression:
                    return Value(context, ast.Child);
                case PsKind.Pipeline:
                    return ast.Items.Count != 1 ? null : Value(context, ast.Items[0]);
                case PsKind.Command:
                    return CommandValue(context, ast);
                case PsKind.ArrayLiteral:
                {
                    var values = new List<string>();
                    foreach (var element in ast.Items)
                        if (!AddValues(values, Value(context, element))) return null;
                    return values.ToArray();
                }
                case PsKind.ArrayExpression:
                case PsKind.SubExpression:
                {
                    var block = ast.Child;
                    if (block == null || block.HasTraps) return null;
                    var values = new List<string>();
                    foreach (var statement in block.Items)
                        if (!AddValues(values, Value(context, statement))) return null;
                    return values.ToArray();
                }
                case PsKind.Binary:
                {
                    if (ast.Value != "+") return null;
                    var left = Value(context, ast.Left);
                    var right = Value(context, ast.Right);
                    return Product(left, right, Concat);
                }
                case PsKind.InvokeMember:
                    return PathMethodValue(context, ast);
            }
            return null;
        }

        private static string[] ExpandableValue(Context context, PsAst ast)
        {
            string text = context.ExtentText(ast);
            if (ast.Items.Count == 0) return new[] { ast.Value ?? "" };
            if (text.StartsWith("@", StringComparison.Ordinal)) return null;
            bool quoted = text.StartsWith("\"", StringComparison.Ordinal);
            int cursor = quoted ? 1 : 0;
            string[] values = { "" };
            foreach (var nested in ast.Items.OrderBy(node => node.Start))
            {
                int start = nested.Start - ast.Start;
                // An unterminated string's extent can end inside its last expression: its value is unknown.
                if (start < cursor) return null;
                string literal = text.Substring(cursor, start - cursor);
                if (literal.Contains('`')) return null;
                values = Product(values, new[] { literal }, Concat);
                values = Product(values, Value(context, nested), Concat);
                if (values == null) return null;
                cursor = nested.End - ast.Start;
            }
            int end = text.Length;
            if (quoted) end--;
            if (end < cursor) return null;
            string tail = text.Substring(cursor, end - cursor);
            if (tail.Contains('`')) return null;
            return Product(values, new[] { tail }, Concat);
        }

        // [IO.Path]::Combine(...) and [IO.Path]::GetDirectoryName(x).
        private static string[] PathMethodValue(Context context, PsAst ast)
        {
            if (!ast.Static || ast.Left == null || ast.Left.Kind != PsKind.TypeExpression || !PathType.IsMatch(ast.Left.Value ?? "")
                || ast.Right == null || ast.Right.Kind != PsKind.StringConstant)
                return null;
            string member = (ast.Right.Value ?? "").ToLowerInvariant();
            var arguments = ast.Items;
            if (member == "combine" && arguments.Count >= 2)
            {
                var values = Value(context, arguments[0]);
                for (int i = 1; i < arguments.Count; i++)
                {
                    var next = Value(context, arguments[i]);
                    values = Product(values, next, JoinValue);
                }
                return values;
            }
            if (member == "getdirectoryname" && arguments.Count == 1) return Parents(Value(context, arguments[0]));
            return null;
        }

        // ---- dependencies ----

        // A project directory stands for every input under it except build outputs. Compile items outside it come
        // from the project file itself; MSBuild evaluation is never claimed.
        private static List<string> ProjectFiles(Graph graph, string directory)
        {
            string prefix = directory + "/";
            var files = new List<string>();
            foreach (string path in graph.Files.Keys)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string rest = path.Substring(prefix.Length);
                if (rest.StartsWith("bin/", StringComparison.Ordinal) || rest.StartsWith("obj/", StringComparison.Ordinal)
                    || rest.Contains("/bin/", StringComparison.Ordinal) || rest.Contains("/obj/", StringComparison.Ordinal))
                    continue;
                files.Add(path);
            }
            return files;
        }

        // Records a dependency target and returns whether it reached an inventory path.
        private static bool AddTarget(Graph graph, Dictionary<string, string> targets, string caller, string value, string kind, bool required)
        {
            var path = ToPath(value);
            switch (path.Kind)
            {
                case PathKind.Outside:
                    if (required) graph.Reason(caller, "outside");
                    return false;
                case PathKind.Unknown:
                    if (required) graph.Reason(caller, "dynamic");
                    return false;
                case PathKind.Anchored:
                    if (graph.Files.ContainsKey(path.Path))
                    {
                        targets[path.Path] = kind;
                        return true;
                    }
                    if (graph.Projects.Contains(path.Path))
                    {
                        foreach (string file in ProjectFiles(graph, path.Path)) targets[file] = kind;
                        return true;
                    }
                    if (required) graph.Reason(caller, "missing:" + path.Path);
                    return false;
            }
            // Unanchored text: a bare command or module name is not a path here.
            string text = path.Path;
            if (!text.Contains('/') && !IsScriptExtension(text))
            {
                if (required && kind == "import") graph.Reason(caller, "external:" + text);
                return false;
            }
            if (!required)
            {
                var relative = ToPath(Anchor + text);
                if (relative.Kind == PathKind.Anchored && graph.Files.ContainsKey(relative.Path))
                {
                    targets[relative.Path] = kind;
                    return true;
                }
                return false;
            }
            // The working directory is not proven. Both plausible bases are positives; the reason stays.
            graph.Reason(caller, "cwd:" + text);
            bool found = false;
            foreach (string directory in new[] { "", DirectoryOf(caller) })
            {
                var candidate = ToPath(Anchor + directory + "/" + text);
                if (candidate.Kind == PathKind.Anchored && graph.Files.ContainsKey(candidate.Path))
                {
                    targets[candidate.Path] = kind;
                    found = true;
                }
            }
            return found;
        }

        private static void AddRequired(Graph graph, Dictionary<string, string> targets, Context context, PsAst ast, string kind)
        {
            if (ast == null)
            {
                graph.Reason(context.Path, "dynamic");
                return;
            }
            if (ast.Kind == PsKind.ScriptBlockExpression) return;
            var values = Value(context, ast);
            if (values == null)
            {
                graph.Reason(context.Path, "dynamic");
                return;
            }
            foreach (string value in values) AddTarget(graph, targets, context.Path, value, kind, true);
        }

        private static void PowerShellDependencies(Graph graph, string path, string text, Dictionary<string, string> targets)
        {
            var root = PsReader.Parse(text, out bool failed);
            if (failed) graph.Reason(path, "parse");
            var nodes = root.FindAll();
            var context = new Context(path, text, nodes);
            foreach (var command in nodes)
            {
                if (command.Kind != PsKind.Command || command.Items.Count == 0) continue;
                var first = command.Items[0];
                if (command.Invocation == '.' || command.Invocation == '&')
                {
                    string kind = command.Invocation == '.' ? "dotSource" : "invoke";
                    if (first.Kind == PsKind.ScriptBlockExpression) continue;
                    if (first.Kind == PsKind.StringConstant && !first.Value.Contains('/') && !first.Value.Contains('\\')
                        && !IsScriptExtension(first.Value))
                        continue;
                    AddRequired(graph, targets, context, first, kind);
                    continue;
                }
                string name = command.CommandName;
                if (name == null) continue;
                string lower = name.ToLowerInvariant();
                // Module-qualified command names (Module\Command) are commands, not paths.
                if (ModuleQualified.IsMatch(lower)) continue;
                if (lower.Contains('/') || lower.Contains('\\') || (lower.Contains('.') && IsScriptExtension(lower)))
                {
                    AddRequired(graph, targets, context, first, "invoke");
                    continue;
                }
                if (!ReadCommands.IsMatch(lower)) continue;
                var arguments = ReadArguments(command, Switches);
                switch (lower)
                {
                    case "import-module":
                    case "ipmo":
                        AddRequired(graph, targets, context, FindArgument(arguments, new[] { "name", "path" }, 0), "import");
                        break;
                    case "invoke-expression":
                    case "iex":
                        graph.Reason(path, "dynamic");
                        break;
                    case "add-type":
                        AddTypeDependencies(graph, targets, context, arguments);
                        break;
                    case "pwsh":
                    case "pwsh.exe":
                    case "powershell":
                    case "powershell.exe":
                    {
                        var file = FindArgument(arguments, new[] { "file" }, -1);
                        if (file != null)
                        {
                            AddRequired(graph, targets, context, file, "process");
                            break;
                        }
                        var code = FindArgument(arguments, new[] { "command", "encodedcommand" }, -1);
                        if (code != null && code.Kind != PsKind.ScriptBlockExpression && code.Kind != PsKind.StringConstant)
                            graph.Reason(path, "dynamic");
                        break;
                    }
                    default:
                        // bash, sh, node, python, python3: the first argument that is not a single option names the script.
                        foreach (var argument in arguments.Positional)
                        {
                            var values = Value(context, argument);
                            if (values == null)
                            {
                                graph.Reason(path, "dynamic");
                                break;
                            }
                            if (values.Length == 1 && values[0].StartsWith("-", StringComparison.Ordinal)) continue;
                            foreach (string each in values) AddTarget(graph, targets, path, each, "process", true);
                            break;
                        }
                        break;
                }
            }
            // Any constant path expression elsewhere (reads, compiled helper projects, tool inputs) is a positive.
            foreach (var node in nodes)
            {
                switch (node.Kind)
                {
                    case PsKind.StringConstant:
                    {
                        string value = (node.Value ?? "").Replace('\\', '/');
                        if (value.Contains('/') && graph.Files.ContainsKey(value)) targets[value] = "reference";
                        continue;
                    }
                    case PsKind.Command:
                    {
                        string name = node.CommandName;
                        if (name == null || (!IsCommand(name, "Join-Path") && !IsCommand(name, "Split-Path"))) continue;
                        break;
                    }
                    case PsKind.ExpandableString:
                        if (node.Items.Count == 0) continue;
                        break;
                    case PsKind.Binary:
                        if (node.Value != "+") continue;
                        break;
                    case PsKind.InvokeMember:
                        if (!node.Static) continue;
                        break;
                    default:
                        continue;
                }
                // Only variables can carry an anchored root into an expression.
                if (!context.ExtentText(node).Contains('$') || !IsOutermost(node)) continue;
                var values = Value(context, node);
                if (values == null) continue;
                foreach (string value in values)
                    if (ToPath(value).Kind == PathKind.Anchored) AddTarget(graph, targets, path, value, "reference", false);
            }
        }

        private static void AddTypeDependencies(Graph graph, Dictionary<string, string> targets, Context context, Arguments arguments)
        {
            var source = FindArgument(arguments, new[] { "path", "literalpath" }, -1);
            if (source != null)
            {
                AddRequired(graph, targets, context, source, "compile");
                return;
            }
            var definition = FindArgument(arguments, new[] { "typedefinition", "memberdefinition" }, 0);
            if (definition == null || definition.Kind == PsKind.StringConstant) return;
            if (definition.Kind == PsKind.ExpandableString && definition.Items.Count == 0) return;
            // Inline source read from a resolved file is a positive; anything else is unproven.
            int before = targets.Count;
            foreach (var node in definition.FindAll())
            {
                var values = Value(context, node);
                if (values == null) continue;
                foreach (string value in values) AddTarget(graph, targets, context.Path, value, "compile", false);
            }
            if (targets.Count == before) graph.Reason(context.Path, "compile");
        }

        private static void ProjectFileDependencies(Graph graph, string path, string text, Dictionary<string, string> targets)
        {
            string directory = DirectoryOf(path);
            foreach (string file in ProjectFiles(graph, directory))
                if (!string.Equals(file, path, StringComparison.OrdinalIgnoreCase)) targets[file] = "project";
            foreach (Match match in ProjectItems.Matches(text))
            {
                string value = match.Groups[1].Value;
                if (ProjectWildcard.IsMatch(value) || value.Contains(';'))
                {
                    graph.Reason(path, "project-input");
                    continue;
                }
                AddTarget(graph, targets, path, Anchor + directory + "/" + value, "project", true);
            }
            // SDK, Directory.Build and property evaluation are not performed here.
            graph.Reason(path, "project-evaluation");
        }

        private static List<(string Path, string Kind)> Dependencies(Graph graph, string path)
        {
            if (graph.Dependencies.TryGetValue(path, out var cached)) return cached;
            var targets = new Dictionary<string, string>(StringComparer.Ordinal);
            graph.Files.TryGetValue(path, out string text);
            string lower = path.ToLowerInvariant();
            if (text == null)
            {
                if (IsScriptExtension(lower) || lower.EndsWith(".csproj", StringComparison.Ordinal)) graph.Reason(path, "content-unavailable");
            }
            else if (lower.EndsWith(".ps1", StringComparison.Ordinal) || lower.EndsWith(".psm1", StringComparison.Ordinal))
                PowerShellDependencies(graph, path, text, targets);
            else if (lower.EndsWith(".csproj", StringComparison.Ordinal))
                ProjectFileDependencies(graph, path, text, targets);
            else if (IsScriptExtension(lower))
            {
                // Other languages: exact repo-relative tokens are positives; their closure is not proven.
                foreach (string token in Tokens(text))
                {
                    string candidate = token.Replace('\\', '/');
                    if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase) && graph.Files.ContainsKey(candidate))
                        targets[candidate] = "reference";
                }
                graph.Reason(path, "language");
            }
            targets.Remove(path);
            var ordered = Sorted(targets.Keys).Select(target => (target, targets[target])).ToList();
            graph.Dependencies[path] = ordered;
            return ordered;
        }

        // ---- seeds ----

        private static List<string> CSharpLiterals(string text)
        {
            var values = new List<string>();
            foreach (Match match in CSharpLexer.Matches(text))
            {
                string value = null;
                bool interpolated = false;
                string all = match.Value;
                if (match.Groups["raw"].Success)
                {
                    interpolated = all.StartsWith("$", StringComparison.Ordinal);
                    int quotes = match.Groups["q"].Value.Length;
                    int start = all.IndexOf('"');
                    value = all.Substring(start + quotes, all.Length - start - 2 * quotes);
                }
                else if (match.Groups["verb"].Success)
                {
                    interpolated = all.Contains('$');
                    int start = all.IndexOf('"');
                    value = all.Substring(start + 1, all.Length - start - 2).Replace("\"\"", "\"");
                }
                else if (match.Groups["reg"].Success)
                {
                    interpolated = all.StartsWith("$", StringComparison.Ordinal);
                    int start = all.IndexOf('"');
                    string body = all.Substring(start + 1, all.Length - start - 2);
                    value = Escape.Replace(body, m => m.Groups[1].Value switch
                    {
                        "n" => "\n",
                        "t" => "\t",
                        "r" => "\r",
                        "0" => " ",
                        var other => other,
                    });
                }
                if (value == null) continue;
                if (interpolated)
                {
                    // A hole is unknown text. It separates tokens and never joins them into a path.
                    value = DoubledBraces.Replace(value, " ");
                    value = Hole.Replace(value, " ");
                }
                values.Add(value);
            }
            return values;
        }

        private static List<string> Tokens(string value)
        {
            var tokens = new List<string> { value };
            foreach (Match match in Quoted.Matches(value)) tokens.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            foreach (string piece in Separators.Split(value))
                if (piece.Length > 0) tokens.Add(piece);
            return tokens;
        }

        // A path-shaped seed token: its leaf names a script, or it walks a '..' segment. A token with whitespace is
        // covered by its split pieces; prose, filters and bare words are not paths.
        private static bool IsSeedShape(string token)
        {
            if (Whitespace.IsMatch(token)) return false;
            if (IsScriptExtension(token)) return true;
            return token.Contains('/') && ("/" + token + "/").Contains("/../", StringComparison.Ordinal);
        }

        // Classifies a path-shaped seed token that missed an exact inventory key; it never drops one. C# seed literals
        // are repository-relative (the exact-key convention), so '.' and '..' resolve lexically against the repository
        // root. Nothing is read, followed through a link or guessed.
        private static void AddSeedPath(Graph graph, Dictionary<string, string> seeds, string caller, string token, string kind)
        {
            if (Wildcard.IsMatch(token))
            {
                graph.Reason(caller, "dynamic");
                return;
            }
            if (Scheme.IsMatch(token))
            {
                graph.Reason(caller, "outside");
                return;
            }
            var raw = ToPath(token);
            if (raw.Kind == PathKind.Outside)
            {
                graph.Reason(caller, "outside");
                return;
            }
            if (raw.Kind != PathKind.Relative)
            {
                graph.Reason(caller, "dynamic");
                return;
            }
            var normal = ToPath(Anchor + raw.Path);
            if (normal.Kind == PathKind.Outside)
            {
                graph.Reason(caller, "outside");
                return;
            }
            if (normal.Kind != PathKind.Anchored)
            {
                graph.Reason(caller, "dynamic");
                return;
            }
            string target = normal.Path;
            if (graph.Files.ContainsKey(target))
            {
                seeds.TryAdd(target, kind);
                return;
            }
            // An ordinal miss: a casing-only match is named, never silently picked; every match stays a positive.
            List<string> found = null;
            if (!token.Contains('/')) graph.FoldedLeaves.TryGetValue(target, out found);
            else graph.FoldedFiles.TryGetValue(target, out found);
            if (found == null || found.Count == 0)
            {
                graph.Reason(caller, "missing:" + target);
                return;
            }
            graph.Reason(caller, (found.Count > 1 ? "ambiguous:" : "case:") + target);
            foreach (string match in found) seeds.TryAdd(match, kind);
        }

        // Seeds from a consumer's C# declaration: whole-literal paths, quoted/embedded command paths, and script
        // basenames (the ScriptHarness convention) resolved against the supplied inventory only.
        private static Dictionary<string, string> Seeds(Graph graph, string declarationPath)
        {
            var seeds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!graph.Files.TryGetValue(declarationPath, out string text) || text == null) return seeds;
            foreach (string literal in CSharpLiterals(text))
            {
                foreach (string token in Tokens(literal))
                {
                    string candidate = token.Replace('\\', '/');
                    if (candidate.StartsWith("./", StringComparison.Ordinal)) candidate = candidate.Substring(2);
                    if (candidate.Length == 0 || string.Equals(candidate, declarationPath, StringComparison.OrdinalIgnoreCase)) continue;
                    bool whole = string.Equals(token, literal, StringComparison.OrdinalIgnoreCase);
                    if (graph.Files.ContainsKey(candidate))
                    {
                        seeds.TryAdd(candidate, whole ? "script:path" : "script:embedded");
                        continue;
                    }
                    bool basename = !candidate.Contains('/');
                    if (basename && IsScriptExtension(candidate) && graph.Leaves.TryGetValue(candidate, out var leaves))
                    {
                        if (leaves.Count > 1) graph.Reason(declarationPath, "ambiguous:" + candidate);
                        foreach (string match in leaves) seeds.TryAdd(match, "script:basename");
                        continue;
                    }
                    if (!IsSeedShape(candidate)) continue;
                    string kind = basename ? "script:basename" : whole ? "script:path" : "script:embedded";
                    AddSeedPath(graph, seeds, declarationPath, candidate, kind);
                }
            }
            return seeds;
        }
    }
}
