// CARD-1782, CARD-1820, CARD-1843: what the impacted-classes selector (ImpactedClasses.cs) reads. The 2026-11-22 plan
// (2026-11-22-remaining-ps1-to-csharp.md, unit 4) ported them from scripts/impacted-classes.ps1 and
// scripts/lib/SelectorCensus.cs, which are deleted:
// - the hand table tests/impacted-classes.json (ImpactedTable: the structural refusals, in the script's order);
// - the census of tests/ (ImpactedClassesCensus: class declarations and downgrade-pattern files, read fresh every run);
// - the derived impact index tests/derived-impact-index.json (ImpactedDerivedIndex);
// - the test projects of the selection catalog tests/test-selection-policy.json (remainderProjects);
// - the glob language of the table and the JSON the selector writes (ConvertTo-Json's key order).
// Values read from JSON are the text PowerShell's [string] gave a ConvertFrom-Json value (PsText). Readers match one
// canonical key spelling (the script's property reads ignored case). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Nightly
{
    internal sealed record ImpactedInvariantMethod(string Name, bool OptIn);
    internal sealed record ImpactedInvariantDeclaration(string File, string Namespace, string Class, ImpactedInvariantMethod[] Methods);

    /// <summary>One row of tests/impacted-classes.json, parsed and checked.</summary>
    public sealed class ImpactedKind
    {
        /// <summary>The row id (non-empty).</summary>
        public string Id { get; init; }

        /// <summary>The path globs.</summary>
        public IReadOnlyList<string> Globs { get; init; }

        /// <summary>The whole classes (each ends in Tests).</summary>
        public IReadOnlyList<string> Classes { get; init; }

        /// <summary>The Class/Method entries.</summary>
        public IReadOnlyList<string> Methods { get; init; }

        /// <summary>The added block's glob as a regex; null when the row has none.</summary>
        public Regex AddedRegex { get; init; }

        /// <summary>The added block's literals.</summary>
        public IReadOnlyList<string> AddedLiterals { get; init; }

        /// <summary>A removed line carrying a literal matches too.</summary>
        public bool AddedRemoved { get; init; }

        /// <summary>The row has derive patterns (the downgrade selector).</summary>
        public bool HasDerive { get; init; }

        /// <summary>"required", "only" or empty.</summary>
        public string Windows { get; init; }

        /// <summary>The globs as regexes.</summary>
        public IReadOnlyList<Regex> Regexes { get; init; }
    }

    /// <summary>A table that cannot be used: where (root, kinds[n] or a row id) and the problem.</summary>
    public sealed class ImpactedTableRefusal : Exception
    {
        /// <summary>The refusal.</summary>
        public ImpactedTableRefusal(string where, string problem) : base(where + ": " + problem)
        {
            Where = where;
            Problem = problem;
        }

        /// <summary>root, kinds[n] or the row id.</summary>
        public string Where { get; }

        /// <summary>What is wrong.</summary>
        public string Problem { get; }
    }

    /// <summary>The hand table, parsed: its rows, every derive pattern and every glob (the audit's).</summary>
    public sealed class ImpactedTable
    {
        private static readonly HashSet<string> RootKeys = new HashSet<string>(new[] { "schema", "card", "cache", "maximumStaleness", "kinds" }, StringComparer.Ordinal);
        private static readonly HashSet<string> KindKeys = new HashSet<string>(new[] { "id", "globs", "classes", "methods", "added", "derive", "windows" }, StringComparer.Ordinal);
        private static readonly HashSet<string> AddedKeys = new HashSet<string>(new[] { "glob", "literals", "removed" }, StringComparer.Ordinal);
        private static readonly HashSet<string> DeriveKeys = new HashSet<string>(new[] { "patterns" }, StringComparer.Ordinal);

        /// <summary>The rows in table order.</summary>
        public IReadOnlyList<ImpactedKind> Kinds { get; private init; }

        /// <summary>Every row's derive patterns in table order.</summary>
        public IReadOnlyList<string> DerivePatterns { get; private init; }

        /// <summary>Every glob and added glob in table order (repeats kept).</summary>
        public IReadOnlyList<string> AllGlobs { get; private init; }

        /// <summary>
        /// The table's structural checks in the script's order; the first failure is an ImpactedTableRefusal. A root
        /// that is not an object (an empty text, null or an array included), an unknown property, a schema other than
        /// 1, kinds that is not an array (one object is a one-element array), a kind that is not an object or has no id,
        /// a windows value other than the string required or only, an added or derive that is not an object, a removed
        /// that is not a boolean, a derive pattern that is not a regex, a kind with no globs or added, a kind with no
        /// classes, methods or derive, a methods entry that is not Class/Method, a class that does not end in Tests.
        /// </summary>
        public static ImpactedTable Parse(JsonElement? root)
        {
            if (root == null || root.Value.ValueKind != JsonValueKind.Object) throw new ImpactedTableRefusal("root", "expected an object with a kinds array");
            var doc = root.Value;
            foreach (var property in doc.EnumerateObject())
                if (!RootKeys.Contains(property.Name)) throw new ImpactedTableRefusal("root", "unknown property '" + property.Name + "'");
            var schema = ImpactedJson.Prop(doc, "schema");
            if ((schema == null ? "" : ImpactedJson.PsText(schema.Value) ?? "") != "1") throw new ImpactedTableRefusal("root", "schema must be 1");
            var kindsValue = ImpactedJson.Prop(doc, "kinds");
            if (kindsValue == null || kindsValue.Value.ValueKind == JsonValueKind.Null) throw new ImpactedTableRefusal("root", "kinds must be an array");
            List<JsonElement> nodes;
            if (kindsValue.Value.ValueKind == JsonValueKind.Array) nodes = kindsValue.Value.EnumerateArray().ToList();
            else if (kindsValue.Value.ValueKind == JsonValueKind.Object) nodes = new List<JsonElement> { kindsValue.Value };
            else throw new ImpactedTableRefusal("root", "kinds must be an array");

            var kinds = new List<ImpactedKind>();
            var derivePatterns = new List<string>();
            var allGlobs = new List<string>();
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                var where = "kinds[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (node.ValueKind != JsonValueKind.Object) throw new ImpactedTableRefusal(where, "expected an object");
                foreach (var property in node.EnumerateObject())
                    if (!KindKeys.Contains(property.Name)) throw new ImpactedTableRefusal(where, "unknown property '" + property.Name + "'");
                var idValue = ImpactedJson.Prop(node, "id");
                var id = idValue == null ? "" : ImpactedJson.PsText(idValue.Value) ?? "";
                if (string.IsNullOrWhiteSpace(id)) throw new ImpactedTableRefusal(where, "missing id");
                where = id;

                var globs = ImpactedJson.AsList(ImpactedJson.Prop(node, "globs"));
                var classes = ImpactedJson.AsList(ImpactedJson.Prop(node, "classes"));
                var methods = ImpactedJson.AsList(ImpactedJson.Prop(node, "methods"));

                var windows = "";
                var lane = ImpactedJson.Prop(node, "windows");
                if (lane != null)
                {
                    if (lane.Value.ValueKind != JsonValueKind.String || (lane.Value.GetString() != "required" && lane.Value.GetString() != "only"))
                        throw new ImpactedTableRefusal(where, "windows must be 'required' or 'only'");
                    windows = lane.Value.GetString();
                }

                var addedGlob = "";
                var addedLiterals = new List<string>();
                var addedRemoved = false;
                var hasAdded = false;
                var added = ImpactedJson.Prop(node, "added");
                if (added != null && added.Value.ValueKind != JsonValueKind.Null)
                {
                    if (added.Value.ValueKind != JsonValueKind.Object) throw new ImpactedTableRefusal(where, "added must be an object");
                    foreach (var property in added.Value.EnumerateObject())
                        if (!AddedKeys.Contains(property.Name)) throw new ImpactedTableRefusal(where, "unknown property '" + property.Name + "'");
                    var glob = ImpactedJson.Prop(added.Value, "glob");
                    if (glob != null && glob.Value.ValueKind != JsonValueKind.Null) addedGlob = ImpactedJson.PsText(glob.Value) ?? "";
                    addedLiterals = ImpactedJson.AsList(ImpactedJson.Prop(added.Value, "literals"));
                    var removed = ImpactedJson.Prop(added.Value, "removed");
                    if (removed != null)
                    {
                        if (removed.Value.ValueKind != JsonValueKind.True && removed.Value.ValueKind != JsonValueKind.False)
                            throw new ImpactedTableRefusal(where, "removed must be true or false");
                        addedRemoved = removed.Value.ValueKind == JsonValueKind.True;
                    }
                    if (!string.IsNullOrWhiteSpace(addedGlob) || addedLiterals.Count > 0) hasAdded = true;
                }

                var hasDerive = false;
                var derive = ImpactedJson.Prop(node, "derive");
                if (derive != null && derive.Value.ValueKind != JsonValueKind.Null)
                {
                    if (derive.Value.ValueKind != JsonValueKind.Object) throw new ImpactedTableRefusal(where, "derive must be an object");
                    foreach (var property in derive.Value.EnumerateObject())
                        if (!DeriveKeys.Contains(property.Name)) throw new ImpactedTableRefusal(where, "unknown property '" + property.Name + "'");
                    var patterns = ImpactedJson.AsList(ImpactedJson.Prop(derive.Value, "patterns"));
                    foreach (var pattern in patterns)
                    {
                        try { _ = new Regex(pattern, RegexOptions.CultureInvariant); }
                        catch (ArgumentException) { throw new ImpactedTableRefusal(where, "derive pattern '" + pattern + "' is not valid"); }
                        derivePatterns.Add(pattern);
                    }
                    if (patterns.Count > 0) hasDerive = true;
                }

                if (globs.Count < 1 && !hasAdded) throw new ImpactedTableRefusal(where, "no globs or added");
                if (classes.Count < 1 && methods.Count < 1 && !hasDerive) throw new ImpactedTableRefusal(where, "no classes, methods, or derive");
                foreach (var method in methods)
                {
                    var slash = method.IndexOf('/');
                    var last = method.LastIndexOf('/');
                    if (slash <= 0 || slash >= method.Length - 1 || slash != last) throw new ImpactedTableRefusal(where, "methods entry '" + method + "' is not Class/Method");
                    var methodClass = method.Substring(0, slash);
                    if (!methodClass.EndsWith("Tests", StringComparison.Ordinal)) throw new ImpactedTableRefusal(where, "class '" + methodClass + "' is not a *Tests class");
                }
                foreach (var className in classes)
                    if (!className.EndsWith("Tests", StringComparison.Ordinal)) throw new ImpactedTableRefusal(where, "class '" + className + "' is not a *Tests class");

                allGlobs.AddRange(globs);
                if (!string.IsNullOrWhiteSpace(addedGlob)) allGlobs.Add(addedGlob);
                kinds.Add(new ImpactedKind
                {
                    Id = id, Globs = globs, Classes = classes, Methods = methods,
                    AddedRegex = string.IsNullOrWhiteSpace(addedGlob) ? null : ImpactedGlob.Regex(addedGlob),
                    AddedLiterals = addedLiterals, AddedRemoved = addedRemoved, HasDerive = hasDerive, Windows = windows,
                    Regexes = globs.Select(ImpactedGlob.Regex).ToList(),
                });
            }
            return new ImpactedTable { Kinds = kinds, DerivePatterns = derivePatterns, AllGlobs = allGlobs };
        }
    }

    /// <summary>The table's globs: ** spans directories, * stays inside one, every other character is literal.</summary>
    public static class ImpactedGlob
    {
        /// <summary>Convert-GlobToRegex: "**/" is (?:.*/)?, "**" is .*, "*" is [^/]*, the regex metacharacters (and ?) are escaped; anchored.</summary>
        public static string Pattern(string glob)
        {
            var text = new StringBuilder("^");
            var i = 0;
            while (i < glob.Length)
            {
                if (i + 2 < glob.Length && string.CompareOrdinal(glob, i, "**/", 0, 3) == 0) { text.Append("(?:.*/)?"); i += 3; continue; }
                if (i + 1 < glob.Length && string.CompareOrdinal(glob, i, "**", 0, 2) == 0) { text.Append(".*"); i += 2; continue; }
                var c = glob[i];
                if (c == '*') text.Append("[^/]*");
                else if ("\\.+()[]{}|^$?".IndexOf(c) >= 0) text.Append('\\').Append(c);
                else text.Append(c);
                i++;
            }
            return text.Append('$').ToString();
        }

        /// <summary>The glob as a case-sensitive culture-invariant regex.</summary>
        public static Regex Regex(string glob) => new Regex(Pattern(glob), RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// The census of tests/ (scripts/lib/SelectorCensus.cs until 2026-11-22): every *.cs outside bin and obj, read once.
    /// No retained state: each Scan reads the current tree and returns a new census.
    /// </summary>
    public sealed class ImpactedClassesCensus
    {
        /// <summary>
        /// The declaration the selector counts: a line-start class declaration with optional access and sealed, static,
        /// abstract or partial words. Lexical: a match inside a comment or raw string counts too.
        /// </summary>
        public static readonly Regex Declaration = new Regex(
            @"(?m)^[ \t]*(?:(?:public|internal|file|private|protected)[ \t]+)?(?:(?:sealed|static|abstract|partial)[ \t]+)*class[ \t]+([A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.Compiled);

        /// <summary>Class name to the files declaring it (ordinal order). Names are compared ignoring case, as the script's hashtable did.</summary>
        public Dictionary<string, List<string>> FilesByClass { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Repository-relative file to the class names it declares, in declaration order.</summary>
        public Dictionary<string, List<string>> ClassesByFile { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>The files a derive pattern matches.</summary>
        public HashSet<string> DerivedFiles { get; } = new HashSet<string>(StringComparer.Ordinal);

        internal List<ImpactedInvariantDeclaration> InvariantDeclarations { get; } = new List<ImpactedInvariantDeclaration>();

        /// <summary>Time spent reading files.</summary>
        public double ReadMilliseconds { get; private set; }

        /// <summary>Time spent matching declarations.</summary>
        public double DeclarationMilliseconds { get; private set; }

        /// <summary>Time spent matching derive patterns.</summary>
        public double DeriveMilliseconds { get; private set; }

        /// <summary>
        /// Walks repo/tests (bin and obj directories skipped, any casing), reads each *.cs, records the declaration
        /// regex's group 1 for every candidate line (a line holding "class"), and marks the file derived when any
        /// derive regex matches its text.
        /// </summary>
        public static ImpactedClassesCensus Scan(string repo, Regex declaration, IReadOnlyList<Regex> derive, bool includeInvariants = false)
        {
            var result = new ImpactedClassesCensus();
            var root = Path.Combine(repo, "tests");
            if (!Directory.Exists(root)) return result;
            var prefix = repo.Replace('\\', '/').TrimEnd('/') + "/";
            long readTicks = 0, declarationTicks = 0, deriveTicks = 0;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count != 0)
            {
                var dir = stack.Pop();
                foreach (var child in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(child);
                    if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)) continue;
                    stack.Push(child);
                }
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
                {
                    var full = file.Replace('\\', '/');
                    var relative = full.StartsWith(prefix, StringComparison.Ordinal) ? full.Substring(prefix.Length) : full;
                    if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)) continue;
                    var phase = Stopwatch.GetTimestamp();
                    var text = File.ReadAllText(file);
                    readTicks += Stopwatch.GetTimestamp() - phase;
                    phase = Stopwatch.GetTimestamp();
                    foreach (var match in DeclarationLines(text, declaration))
                    {
                        var name = match.Groups[1].Value;
                        if (!result.FilesByClass.TryGetValue(name, out var files)) result.FilesByClass.Add(name, files = new List<string>());
                        files.Add(relative);
                        if (!result.ClassesByFile.TryGetValue(relative, out var names)) result.ClassesByFile.Add(relative, names = new List<string>());
                        if (!names.Contains(name)) names.Add(name);
                    }
                    if (includeInvariants && text.Contains(NightlyTestsPlan.RepositoryInvariantCategory, StringComparison.Ordinal))
                        result.ReadInvariantDeclarations(relative, text);
                    declarationTicks += Stopwatch.GetTimestamp() - phase;
                    phase = Stopwatch.GetTimestamp();
                    foreach (var pattern in derive ?? Array.Empty<Regex>())
                    {
                        if (!pattern.IsMatch(text)) continue;
                        result.DerivedFiles.Add(relative);
                        break;
                    }
                    deriveTicks += Stopwatch.GetTimestamp() - phase;
                }
            }
            foreach (var files in result.FilesByClass.Values)
            {
                var sorted = new SortedSet<string>(files, StringComparer.Ordinal);
                files.Clear();
                files.AddRange(sorted);
            }
            result.ReadMilliseconds = readTicks * 1000.0 / Stopwatch.Frequency;
            result.DeclarationMilliseconds = declarationTicks * 1000.0 / Stopwatch.Frequency;
            result.DeriveMilliseconds = deriveTicks * 1000.0 / Stopwatch.Frequency;
            return result;
        }

        // The declaration regex is line-bound and needs the literal "class": only the lines holding it are matched. The
        // verdict is the regex's own, lexical false positives included.
        private static IEnumerable<Match> DeclarationLines(string text, Regex declaration)
        {
            var offset = 0;
            while (offset < text.Length)
            {
                var word = text.IndexOf("class", offset, StringComparison.Ordinal);
                if (word < 0) yield break;
                var start = text.LastIndexOf('\n', word) + 1;
                var end = text.IndexOf('\n', word);
                if (end < 0) end = text.Length;
                foreach (Match match in declaration.Matches(text.Substring(start, end - start))) yield return match;
                offset = end < text.Length ? end + 1 : end;
            }
        }
        private void ReadInvariantDeclarations(string relative, string text)
        {
            var root = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var attributes = type.AttributeLists.SelectMany(list => list.Attributes).ToArray();
                if (!attributes.Any(attribute => Category(attribute, NightlyTestsPlan.RepositoryInvariantCategory))) continue;
                var ns = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
                if (!ns.EndsWith(".Invariants", StringComparison.Ordinal))
                    throw new NightlyRefusal("repository-invariant-namespace-missing:" + relative + ":" + type.Identifier.ValueText);
                var methods = new List<ImpactedInvariantMethod>();
                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                {
                    var methodAttributes = method.AttributeLists.SelectMany(list => list.Attributes).ToArray();
                    if (!methodAttributes.Any(attribute => AttributeName(attribute) == "Test")) continue;
                    var optIn = attributes.Concat(methodAttributes).Any(attribute =>
                        AttributeName(attribute) == "Explicit" || Category(attribute, "OptIn"));
                    methods.Add(new ImpactedInvariantMethod(method.Identifier.ValueText, optIn));
                }
                if (methods.Count == 0) throw new NightlyRefusal("repository-invariant-empty-type:" + relative + ":" + type.Identifier.ValueText);
                InvariantDeclarations.Add(new ImpactedInvariantDeclaration(relative, ns, type.Identifier.ValueText, methods.ToArray()));
            }

            static string AttributeName(AttributeSyntax attribute) => attribute.Name.GetLastToken().ValueText.Replace("Attribute", "");
            static bool Category(AttributeSyntax attribute, string value) => AttributeName(attribute) == "Category"
                && attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == value;
        }

    }

    /// <summary>A capped path of the derived index: why, how many classes are owed and the first ones.</summary>
    public sealed class ImpactedCappedPath
    {
        /// <summary>The cap reason (allowlist keeps the path out of the derived selection).</summary>
        public string Reason { get; init; } = "";

        /// <summary>The classes owed beyond the cap.</summary>
        public int Owed { get; init; }

        /// <summary>The first owed classes.</summary>
        public IReadOnlyList<string> First { get; init; } = Array.Empty<string>();
    }

    /// <summary>The derived index cannot be used.</summary>
    public sealed class ImpactedIndexRefusal : Exception
    {
        /// <summary>The refusal.</summary>
        public ImpactedIndexRefusal(string message) : base(message) { }
    }

    /// <summary>tests/derived-impact-index.json (CARD-1820): reverse edges, capped paths, the allowlist and the OS labels.</summary>
    public sealed class ImpactedDerivedIndex
    {
        /// <summary>Path to the classes that reference it.</summary>
        public Dictionary<string, List<string>> Edges { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>Path to its cap.</summary>
        public Dictionary<string, ImpactedCappedPath> Capped { get; } = new Dictionary<string, ImpactedCappedPath>(StringComparer.Ordinal);

        /// <summary>Paths too broad to derive from.</summary>
        public HashSet<string> Allow { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Classes that run on Windows only.</summary>
        public HashSet<string> WindowsOnly { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Classes that run on Linux only.</summary>
        public HashSet<string> LinuxOnly { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The index's text read as the script read it: an object with schema 1, edges and cappedPaths (present, not
        /// null); entries that are not objects or have no path are skipped; a later entry for a path wins. Paths use
        /// forward slashes. ImpactedIndexRefusal otherwise (the parser's message for text that is not JSON).
        /// </summary>
        public static ImpactedDerivedIndex Parse(string text)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(text, ImpactedJson.ReadOptions); }
            catch (JsonException e) { throw new ImpactedIndexRefusal(e.Message); }
            using (document)
            {
                var doc = document.RootElement;
                if (doc.ValueKind != JsonValueKind.Object) throw new ImpactedIndexRefusal("expected an object");
                var schema = ImpactedJson.Prop(doc, "schema");
                if ((schema == null ? "" : ImpactedJson.PsText(schema.Value) ?? "") != "1") throw new ImpactedIndexRefusal("schema must be 1");
                var edges = ImpactedJson.Prop(doc, "edges");
                var capped = ImpactedJson.Prop(doc, "cappedPaths");
                if (edges == null || edges.Value.ValueKind == JsonValueKind.Null || capped == null || capped.Value.ValueKind == JsonValueKind.Null)
                    throw new ImpactedIndexRefusal("edges and cappedPaths are required");
                var index = new ImpactedDerivedIndex();
                foreach (var edge in ImpactedJson.Elements(edges.Value))
                {
                    if (edge.ValueKind != JsonValueKind.Object) continue;
                    var path = PathOf(edge);
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    index.Edges[path] = ImpactedJson.AsList(ImpactedJson.Prop(edge, "classes"));
                }
                foreach (var row in ImpactedJson.Elements(capped.Value))
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    var path = PathOf(row);
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var reason = ImpactedJson.Prop(row, "reason");
                    var owed = ImpactedJson.Prop(row, "owed");
                    index.Capped[path] = new ImpactedCappedPath
                    {
                        Reason = reason == null ? "" : ImpactedJson.PsText(reason.Value) ?? "",
                        Owed = owed == null || owed.Value.ValueKind == JsonValueKind.Null ? 0 : ImpactedJson.PsInt(owed.Value),
                        First = ImpactedJson.AsList(ImpactedJson.Prop(row, "first")),
                    };
                }
                foreach (var name in ImpactedJson.AsList(ImpactedJson.Prop(doc, "windowsOnly"))) index.WindowsOnly.Add(name);
                foreach (var name in ImpactedJson.AsList(ImpactedJson.Prop(doc, "linuxOnly"))) index.LinuxOnly.Add(name);
                foreach (var path in ImpactedJson.AsList(ImpactedJson.Prop(doc, "allowlist")))
                {
                    var text2 = path.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(text2)) index.Allow.Add(text2);
                }
                return index;
            }
        }

        private static string PathOf(JsonElement row)
        {
            var path = ImpactedJson.Prop(row, "path");
            if (path == null || path.Value.ValueKind == JsonValueKind.Null) return "";
            return (ImpactedJson.PsText(path.Value) ?? "").Replace('\\', '/');
        }
    }

    /// <summary>The test projects that own their classes (CARD-1843).</summary>
    public static class ImpactedProjects
    {
        /// <summary>The project a class declared outside every other project runs in.</summary>
        public const string Default = "tests/Antiphon.Tests";

        /// <summary>The three projects of their own that predate the catalog's remainderProjects.</summary>
        public static readonly IReadOnlyList<string> Historical = new[] { "tests/Antiphon.E2E", "tests/Antiphon.Messaging.Tests", "tests/Antiphon.SessionRunner.Tests" };

        /// <summary>
        /// The historical projects, then the directory of every remainderProjects entry of the catalog text (a
        /// tests/.../*.csproj path), except tests/Antiphon.Tests and repeats. A null text (no catalog) is the historical
        /// three. Text that is not JSON, or a catalog without sets, throws (the selector refuses it).
        /// </summary>
        public static List<string> Directories(string catalogText)
        {
            var dirs = new List<string>(Historical);
            if (catalogText == null) return dirs;
            var entries = new List<string>();
            using (var document = JsonDocument.Parse(catalogText, ImpactedJson.ReadOptions))
            {
                var catalog = document.RootElement;
                if (catalog.ValueKind != JsonValueKind.Object) throw new InvalidDataException("expected an object with sets");
                var sets = ImpactedJson.Prop(catalog, "sets") ?? throw new InvalidDataException("The property 'sets' cannot be found on this object.");
                foreach (var set in ImpactedJson.Elements(sets))
                {
                    if (set.ValueKind != JsonValueKind.Object) continue;
                    var remainder = ImpactedJson.Prop(set, "remainderProjects");
                    if (remainder == null) continue;
                    foreach (var entry in ImpactedJson.Elements(remainder.Value))
                        if (entry.ValueKind != JsonValueKind.Null) entries.Add(ImpactedJson.PsText(entry) ?? "");
                }
            }
            foreach (var entry in entries)
            {
                var project = entry.Replace('\\', '/');
                var slash = project.LastIndexOf('/');
                if (!project.StartsWith("tests/", StringComparison.Ordinal) || !project.EndsWith(".csproj", StringComparison.Ordinal) || slash <= 6) continue;
                var dir = project.Substring(0, slash);
                if (!string.Equals(dir, Default, StringComparison.InvariantCultureIgnoreCase) && !dirs.Contains(dir)) dirs.Add(dir);
            }
            return dirs;
        }

        /// <summary>The longest project directory the path is under, else tests/Antiphon.Tests.</summary>
        public static string Of(IReadOnlyList<string> dirs, string relative)
        {
            var best = "";
            foreach (var dir in dirs)
                if (relative.StartsWith(dir + "/", StringComparison.Ordinal) && dir.Length > best.Length) best = dir;
            return best.Length > 0 ? best : Default;
        }

        /// <summary>A project's row slug: tests, e2e, messaging, sessionrunner, else the path after tests/ in lower case.</summary>
        public static string Slug(string project)
        {
            if (PsEq(project, Default)) return "tests";
            if (PsEq(project, "tests/Antiphon.E2E")) return "e2e";
            if (PsEq(project, "tests/Antiphon.Messaging.Tests")) return "messaging";
            if (PsEq(project, "tests/Antiphon.SessionRunner.Tests")) return "sessionrunner";
            return project.Replace("tests/", "").ToLowerInvariant();
        }

        /// <summary>PowerShell's -eq on two strings.</summary>
        internal static bool PsEq(string left, string right) => string.Equals(left, right, StringComparison.InvariantCultureIgnoreCase);
    }

    /// <summary>The JSON the selector reads and writes.</summary>
    public static class ImpactedJson
    {
        /// <summary>The reader's leniency: comments and trailing commas, as ConvertFrom-Json accepted them.</summary>
        public static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 1024 };

        private static readonly JsonWriterOptions Indented = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true };
        private static readonly JsonWriterOptions Compact = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

        /// <summary>The object's property of that exact name (the last when it repeats, as ConvertFrom-Json kept it); null when absent.</summary>
        public static JsonElement? Prop(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Object) return null;
            JsonElement? found = null;
            foreach (var property in value.EnumerateObject())
                if (property.NameEquals(name)) found = property.Value;
            return found;
        }

        /// <summary>@($value): an array's elements, anything else as itself.</summary>
        public static IEnumerable<JsonElement> Elements(JsonElement value) =>
            value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : new[] { value };

        /// <summary>
        /// [string] of a ConvertFrom-Json value: a string itself, an integer in invariant digits, a real in invariant
        /// round-trip form, True or False, an array's items joined with spaces, an object as @{name=value; ...}; null for
        /// JSON null. (ConvertFrom-Json also read date-like strings as dates; a table has none.)
        /// </summary>
        public static string PsText(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Number:
                    var raw = value.GetRawText();
                    if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return whole.ToString(CultureInfo.InvariantCulture);
                    if (System.Numerics.BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big)) return big.ToString(CultureInfo.InvariantCulture);
                    return value.GetDouble().ToString(CultureInfo.InvariantCulture);
                case JsonValueKind.True:
                    return "True";
                case JsonValueKind.False:
                    return "False";
                case JsonValueKind.Array:
                    return string.Join(" ", value.EnumerateArray().Select(item => PsText(item) ?? ""));
                case JsonValueKind.Object:
                    return "@{" + string.Join("; ", value.EnumerateObject().Select(p => p.Name + "=" + (PsText(p.Value) ?? ""))) + "}";
                default:
                    return null;
            }
        }

        /// <summary>[int] of a ConvertFrom-Json value: a whole number, a real rounded half to even, a numeric string, True as 1; InvalidCastException otherwise.</summary>
        public static int PsInt(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (value.TryGetInt32(out var whole)) return whole;
                    return checked((int)Math.Round(value.GetDouble(), MidpointRounding.ToEven));
                case JsonValueKind.String:
                    var text = value.GetString().Trim();
                    if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) return checked((int)Math.Round(real, MidpointRounding.ToEven));
                    break;
                case JsonValueKind.True:
                    return 1;
                case JsonValueKind.False:
                    return 0;
            }
            throw new InvalidCastException("Cannot convert value \"" + value.GetRawText() + "\" to type \"System.Int32\".");
        }

        /// <summary>As-List: the value's items (or the value itself), each [string], nulls and blank texts left out; empty for an absent or null value.</summary>
        public static List<string> AsList(JsonElement? value)
        {
            var list = new List<string>();
            if (value == null || value.Value.ValueKind == JsonValueKind.Null) return list;
            foreach (var item in Elements(value.Value))
            {
                if (item.ValueKind == JsonValueKind.Null) continue;
                var text = PsText(item);
                if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
            }
            return list;
        }

        /// <summary>The value as ConvertTo-Json wrote it (two-space indentation; non-ASCII text unescaped), or compressed.</summary>
        public static string Text(IJsonWritable value, bool indented = true)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, indented ? Indented : Compact)) value.WriteJson(writer);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    /// <summary>A JSON object whose keys keep the order they were added in (a [pscustomobject] literal's).</summary>
    public sealed class ImpactedRecord : IJsonWritable
    {
        private readonly List<KeyValuePair<string, object>> _fields = new List<KeyValuePair<string, object>>();

        /// <summary>Adds a field: null, a string, a bool, a number, a record, or a sequence of those.</summary>
        public ImpactedRecord Add(string name, object value)
        {
            _fields.Add(new KeyValuePair<string, object>(name, value));
            return this;
        }

        /// <summary>A field's value; null when it is absent or null.</summary>
        public object this[string name] => _fields.LastOrDefault(field => field.Key == name).Value;

        /// <summary>The field names in order.</summary>
        public IEnumerable<string> Names => _fields.Select(field => field.Key);

        /// <inheritdoc />
        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            foreach (var field in _fields)
            {
                writer.WritePropertyName(field.Key);
                Write(writer, field.Value);
            }
            writer.WriteEndObject();
        }

        private static void Write(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null: writer.WriteNullValue(); break;
                case string text: writer.WriteStringValue(text); break;
                case bool flag: writer.WriteBooleanValue(flag); break;
                case int number: writer.WriteNumberValue(number); break;
                case long number: writer.WriteNumberValue(number); break;
                case double number: writer.WriteNumberValue(number); break;
                case IJsonWritable record: record.WriteJson(writer); break;
                case IEnumerable items:
                    writer.WriteStartArray();
                    foreach (var item in items) Write(writer, item);
                    writer.WriteEndArray();
                    break;
                default: throw new ArgumentException("unsupported JSON value: " + value.GetType().Name);
            }
        }
    }
}
