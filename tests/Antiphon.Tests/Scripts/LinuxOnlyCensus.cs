using System.Collections;
using System.Text.RegularExpressions;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>What the Linux-only census found: its problems, the policy row keys, the marker keys and the marker files.</summary>
internal sealed record LinuxOnlyCensusResult(bool Ok, string[] Problems, string[] RowKeys, string[] MarkerKeys, string[] MarkerFiles);

/// <summary>
/// CARD-1686: the Linux-only census, in process since unit 18 of 2026-11-22-remaining-ps1-to-csharp.md (it was
/// Get-C1323LinuxOnlyCensus in scripts/test-rc-platform.ps1). The rc policy rows whose reason starts with
/// "Linux-only by construction:" are the only list. Each such row needs the Linux-only marker attribute under its
/// suite's project directory, and each marker needs a row. A marker file that a selection rule names must route to the
/// census set; an unnamed file already falls back to a full selection. The attribute's name is never spelled whole in
/// this file (Name joins two halves), so the census never reads its own source as a marker.
/// </summary>
internal static class LinuxOnlyCensus
{
    /// <summary>The marker attribute's name (without the Attribute suffix).</summary>
    public const string Name = "Native" + "LinuxOnly";

    /// <summary>The reason prefix of a Linux-only policy row.</summary>
    public const string Prefix = "Linux-only by construction:";

    /// <summary>This file: the census, which a selection rule must route to the census set.</summary>
    public const string SourcePath = "tests/Antiphon.Tests/Scripts/LinuxOnlyCensus.cs";

    /// <summary>The selection set that runs the census.</summary>
    public const string CensusSet = "release-gate.platform";

    /// <summary>Project directories the census skips (the native probe's deliberate fixture markers).</summary>
    public static readonly string[] FixtureProjects = ["tests/Antiphon.NativeLinuxPolicy.Probe/"];

    /// <summary>
    /// Files that name the type without marking a test: the definition (class and constructor declarations, an error
    /// string) and the witnesses that read, reflect over or construct it (typeof, new, a resource-name string). Only those
    /// reference forms are exempt; any other use in these files fails closed like anywhere else.
    /// </summary>
    public static readonly string[] ExemptFiles =
    [
        "tests/Shared/" + Name + "Attribute.cs",
        "tests/Shared/NativeLinuxPolicyWitness.cs",
        "tests/Shared/NativeLinuxPolicyWitnessTests.cs",
        "tests/Antiphon.Tests/Scripts/NativeLinuxPolicyWitnessMutationTests.cs",
        "tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs",
    ];

    /// <summary>The paths a selection rule must route to the census set explicitly: the policy and this census.</summary>
    public static readonly string[] RoutedPaths = ["tests/test-execution-policy.json", SourcePath];

    private const string Token = Name + "(?:Attribute)?";
    private const string Qualified = @"(?:global::)?(?:\w+\.)*" + Token;
    private static readonly Regex Word = new(@"\b" + Token + @"\b");
    private static readonly Regex References = new(@"\btypeof\(\s*" + Qualified + @"\s*\)|\bnew\s+" + Qualified + @"\s*\(|\bclass\s+" + Token + @"\b|\bpublic\s+"
        + Token + @"\s*\(|""[^""\\()\[\],/*]*\b" + Token + @"\b[^""\\()\[\],/*]*""");
    private static readonly Regex Marker = new(@"^\s*\[" + Name + @"\((?:""(?:[^""\\]|\\.)*""|[^)""])*\)\]");
    private static readonly Regex ClosedThenToken = new(@"\*/.*\b" + Token + @"\b");
    private static readonly Regex LeadingAttributes = new(@"^\s*(?:\[(?:[^\[\]]|\[[^\[\]]*\])*\]\s*)*");
    private static readonly Regex ClassDeclaration = new(@"^(?:(?:public|internal|private|protected|sealed|static|abstract|partial|file)\s+)*(?:class|record|struct)\s+(\w+)");
    private static readonly Regex MethodDeclaration = new(@"^(?:(?:public|internal|private|protected|static|async|override|virtual|new)\s+)+[\w<>\[\],.? ]+?\s(\w+)\s*(?:<[^>]*>)?\s*\(");
    private static readonly Regex OwnerDeclaration = new(@"^(\s*)(?:(?:public|internal|private|protected|sealed|static|abstract|partial|file)\s+)*(?:class|record|struct)\s+(\w+)");
    private static readonly Regex Owner = new("^CARD-[0-9]{4}$");
    private static readonly Regex Indent = new(@"^\s*");

    // CARD-1711 A-2: C# lines end at CR LF, CR, LF, U+2028, U+2029 or U+0085.
    private static readonly Regex LineEnd = new("\r\n|[\r\n\u2028\u2029\u0085]");

    // CARD-1711 A-1, D-1 and A-3: an interpolation hole is code with balanced braces and plain string and char literals
    // (no comment, no nested verbatim, raw or interpolated string, no bare backslash). Every $ run is anchored at its first
    // $ and consumed atomically, so the walk is linear.
    private const string Hole = @"\{(?>(?:[^{}""'/@$\\]|/(?![/*])|""(?:[^""\\\n]|\\[^\n])*""|'(?:[^'\\\n]|\\[^\n])+'|(?<d>\{)|(?<-d>\}))*)(?(d)(?!))\}";
    private static readonly Regex Tokens = new(@"//[^\n]*|(?<block>/\*[\s\S]*?(?:\*/|\z))|(?<str>(?:\$@|@\$)""(?>(?:[^""{]|""""|\{\{|" + Hole + @")*)(?:""|\z|(?<lost>))"
        + @"|@""(?:[^""]|"""")*(?:""|\z)|(?<!\$)(?<raw>(?>\$*))(?<q>""{3,})[\s\S]*?(?:\k<q>|\z)"
        + @"|(?<!\$)(?>\$+)""(?>(?:[^""\\\n{]|\\[^\n]|\{\{|" + Hole + @")*)(?:""|(?<lost>(?=\{))|))|""(?:[^""\\\n]|\\[^\n])*""?|'(?:[^'\\\n]|\\[^\n])*'?");
    private static readonly Regex Literal = new(@"\G(?:(?>\$+)""(?>(?:[^""\\\n{]|\\[^\n]|\{\{|" + Hole + @")*)""|""(?:[^""\\\n]|\\[^\n])*""|'(?:[^'\\\n]|\\[^\n])+')");
    private static readonly Regex RawPieces = new(@"\{+|\}+|\$*@\$*""|\$+""{3,}|\$+""|""+|'|/[/*]");
    private static readonly Regex InterpolatedQuote = new(@"^\$*""$");

    /// <summary>
    /// The census of root's tests tree against the policy's rc rows. catalog: the selection catalog (the -AsHashtable
    /// reading, or a fixture's dictionary); null skips the routing check.
    /// </summary>
    public static LinuxOnlyCensusResult Run(string root, ExecutionPolicy policy, IDictionary? catalog, string censusSet = CensusSet,
        string[]? fixtureProjects = null, string[]? exemptFiles = null, string[]? routedPaths = null)
    {
        fixtureProjects ??= FixtureProjects;
        exemptFiles ??= ExemptFiles;
        routedPaths ??= RoutedPaths;
        var problems = new List<string>();
        var suiteDirs = new List<(string Id, string Dir)>();
        foreach (var suite in policy.Suites.Values)
        {
            var project = suite.Project ?? "";
            if (project.Length > 0) suiteDirs.Add((suite.Id, project.Substring(0, project.LastIndexOf('/') + 1)));
        }
        bool KnownSuite(string id) => suiteDirs.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        var rowKeys = new List<string>();
        var rows = policy.Profiles != null && policy.Profiles.TryGetValue("rc", out var rc) ? rc.Exclusions : [];
        foreach (var row in rows.Where(r => (r.Reason ?? "").StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var methods = row.Methods.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal);
            var key = row.Suite + "|" + row.Class + "|" + string.Join(",", methods);
            rowKeys.Add(key);
            if (row.Platform != "windows") problems.Add("row-platform:" + key);
            if (!Owner.IsMatch(row.Owner ?? "")) problems.Add("row-owner:" + key);
            if (!KnownSuite(row.Suite ?? "")) problems.Add("row-suite:" + key);
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.Combine(root, "tests"));
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(dir))
                if (Path.GetFileName(child) is not ("bin" or "obj" or "node_modules" or ".git")) pending.Push(child);
            files.AddRange(Directory.EnumerateFiles(dir, "*.cs"));
        }

        var classMarkers = new List<string>();
        var classSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var methodMarkers = new List<(string Id, SortedSet<string> Methods)>();
        var markerFiles = new List<string>();
        var rootPrefix = Path.GetFullPath(root).TrimEnd('/', '\\').Length + 1;
        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains(Name, StringComparison.Ordinal)) continue;
            var rel = Path.GetFullPath(file).Substring(rootPrefix).Replace('\\', '/');
            if (fixtureProjects.Any(p => rel.StartsWith(p, StringComparison.Ordinal))) continue;
            // Line-based and fail closed. A line whose trimmed text starts with the canonical marker is a marker; a line
            // that starts with // is a comment line, and so is one that starts with /* unless a */ precedes the token (the
            // comment closed and code follows). CARD-1707: the block-comment state is carried across lines (strings and
            // char literals skipped), so a line that starts with * is a comment line only inside a block comment that
            // opened at the start of its line; any other line inside an open block comment, or inside a multi-line string,
            // that names the token fails closed instead of counting. Any other line (and any second use on a marker line)
            // that names the whole-word token fails closed; an exempt file strips its reference forms first. CARD-1711
            // A-2: lines are C# lines. A line from one whose lexical state the walk lost (an interpolation hole it cannot
            // lex) that names the token fails closed.
            var lines = LineEnd.Split(text);
            var exempt = exemptFiles.Contains(rel, StringComparer.Ordinal);
            var hits = new List<int>();
            var states = LineStates(string.Join("\n", lines));
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                var scan = line;
                if (states.LostFrom >= 0 && i >= states.LostFrom)
                {
                    if (Word.IsMatch(line)) problems.Add("marker-noncanonical:" + rel + ":" + (i + 1));
                    continue;
                }
                if (states.Modes[i] == Mode.Block)
                {
                    var close = line.IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0 || !Word.IsMatch(line.Substring(close + 2)))
                    {
                        if (Word.IsMatch(line) && !(states.AtStart[i] && (trimmed.StartsWith('*') || trimmed.StartsWith("/*", StringComparison.Ordinal))))
                            problems.Add("marker-noncanonical:" + rel + ":" + (i + 1));
                        continue;
                    }
                    if (exempt) scan = References.Replace(line, "");
                }
                else if (states.Modes[i] != Mode.Code)
                {
                    if (exempt) scan = References.Replace(line, "");
                }
                else if (trimmed.StartsWith("[" + Name + "(", StringComparison.Ordinal))
                {
                    hits.Add(i);
                    scan = Marker.Replace(line, "");
                    if (scan == line) scan = trimmed.Substring(("[" + Name).Length);
                }
                else if (trimmed.StartsWith("//", StringComparison.Ordinal) || (trimmed.StartsWith("/*", StringComparison.Ordinal) && !ClosedThenToken.IsMatch(trimmed)))
                {
                    continue;
                }
                else if (exempt)
                {
                    scan = References.Replace(line, "");
                }
                if (Word.IsMatch(scan)) problems.Add("marker-noncanonical:" + rel + ":" + (i + 1));
            }
            if (hits.Count == 0) continue;
            var suites = suiteDirs.Where(s => rel.StartsWith(s.Dir, StringComparison.Ordinal)).Select(s => s.Id).ToArray();
            if (suites.Length != 1)
            {
                problems.Add("marker-outside-suite:" + rel);
                continue;
            }
            markerFiles.Add(rel);
            foreach (var i in hits)
            {
                var indent = Indent.Match(lines[i]).Length;
                var decl = Marker.Replace(lines[i], "");
                var j = i;
                while (true)
                {
                    decl = LeadingAttributes.Replace(decl, "");
                    if (decl.Length > 0 && !decl.StartsWith("//", StringComparison.Ordinal)) break;
                    j++;
                    if (j >= lines.Length)
                    {
                        decl = "";
                        break;
                    }
                    decl = lines[j];
                }
                var cls = ClassDeclaration.Match(decl);
                var method = MethodDeclaration.Match(decl);
                if (cls.Success)
                {
                    var key = suites[0] + "|" + cls.Groups[1].Value;
                    if (classSeen.Add(key)) classMarkers.Add(key);
                }
                else if (method.Success)
                {
                    string? owner = null;
                    for (var k = i - 1; k >= 0; k--)
                    {
                        var c = OwnerDeclaration.Match(lines[k]);
                        if (c.Success && c.Groups[1].Length < indent)
                        {
                            owner = c.Groups[2].Value;
                            break;
                        }
                    }
                    if (owner == null)
                    {
                        problems.Add("marker-unparsed:" + rel + ":" + (i + 1));
                        continue;
                    }
                    var id = suites[0] + "|" + owner;
                    var at = methodMarkers.FindIndex(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (at < 0)
                    {
                        methodMarkers.Add((id, new SortedSet<string>(StringComparer.Ordinal)));
                        at = methodMarkers.Count - 1;
                    }
                    methodMarkers[at].Methods.Add(method.Groups[1].Value);
                }
                else
                {
                    problems.Add("marker-unparsed:" + rel + ":" + (i + 1));
                }
            }
        }

        // A class row is satisfied by a class marker or by method markers: the compiled witness then proves every case of
        // the class is marked. A method row must name exactly the marked methods of a class with no class marker.
        var markerKeys = classMarkers.Select(k => k + "|").ToList();
        foreach (var (id, methods) in methodMarkers)
            if (!classSeen.Contains(id)) markerKeys.Add(id + "|" + string.Join(",", methods));
        foreach (var key in markerKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var classKey = key.Substring(0, key.LastIndexOf('|') + 1);
            if (!rowKeys.Contains(key, StringComparer.Ordinal) && !rowKeys.Contains(classKey, StringComparer.Ordinal)) problems.Add("marker-without-row:" + key);
        }
        foreach (var key in rowKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var id = key.Substring(0, key.LastIndexOf('|'));
            var found = key.EndsWith('|')
                ? classSeen.Contains(id) || methodMarkers.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
                : markerKeys.Contains(key, StringComparer.Ordinal);
            if (!found) problems.Add("row-without-marker:" + key);
        }
        if (catalog != null)
        {
            foreach (var path in routedPaths.Concat(markerFiles))
            {
                var sets = new List<string>();
                var full = false;
                var named = false;
                foreach (var rule in Items(catalog["rules"]))
                {
                    var match = Items(Field(rule, "paths")).Any(p => Text(p) == path);
                    var pattern = Text(Field(rule, "pattern"));
                    if (!match && pattern.Length > 0) match = Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant);
                    if (!match) continue;
                    named = true;
                    sets.AddRange(Items(Field(rule, "sets")).Select(Text));
                    if (PsValue.IsTrue(Field(rule, "full"))) full = true;
                }
                foreach (var edge in Items(catalog["edges"]))
                {
                    if (Text(Field(edge, "path")) != path) continue;
                    named = true;
                    sets.AddRange(Items(Field(edge, "sets")).Select(Text));
                }
                var isExplicit = sets.Contains(censusSet, StringComparer.Ordinal);
                var ok = routedPaths.Contains(path, StringComparer.Ordinal) ? isExplicit : isExplicit || full || !named;
                if (!ok) problems.Add("marker-file-unrouted:" + path);
            }
        }
        return new LinuxOnlyCensusResult(problems.Count == 0, problems.ToArray(), rowKeys.ToArray(), markerKeys.ToArray(), markerFiles.ToArray());
    }

    /// <summary>The lexical state at the start of a line.</summary>
    internal enum Mode { Code, String, Block }

    /// <summary>The state of each line, whether an open block comment's /* was the first text on its line, and the first lost line (-1: none).</summary>
    internal sealed record States(Mode[] Modes, bool[] AtStart, int LostFrom);

    /// <summary>
    /// CARD-1707: the C# lexical state at the start of each line, so a line that starts with * is a comment only inside an
    /// open block comment. Comments, strings and char literals are consumed in source order so a /* inside a literal opens
    /// nothing; verbatim and raw strings may span lines. text has LF line ends only (the census folds the others).
    /// CARD-1711 A-1: holes of regular and verbatim interpolated strings are lexed (code with balanced braces and plain
    /// string and char literals). A hole the walk cannot lex (a comment or a nested verbatim, raw or interpolated string)
    /// stops the string at its {, and a raw interpolated string whose closing quote run lies inside a hole is detected;
    /// either way LostFrom names the first line whose state is unknown. CARD-1711 D-1: hole code may hold no bare
    /// backslash (a format clause's \" would flip the walk's string parity), so such a hole is unlexable instead.
    /// </summary>
    internal static States LineStates(string text)
    {
        var starts = new List<int> { 0 };
        for (var k = text.IndexOf('\n'); k >= 0; k = text.IndexOf('\n', k + 1)) starts.Add(k + 1);
        var modes = new Mode[starts.Count];
        var atStart = new bool[starts.Count];
        var lost = -1;
        var line = 0;
        foreach (Match m in Tokens.Matches(text))
        {
            var end = m.Index + m.Length;
            while (line < starts.Count && starts[line] <= m.Index) line++;
            if (lost < 0 && (m.Groups["lost"].Success || (m.Groups["raw"].Length > 0 && RawHoleLost(m.Value, m.Groups["raw"].Length, m.Groups["q"].Length)))) lost = line - 1;
            if (text.IndexOf('\n', m.Index, m.Length) < 0) continue;
            Mode mode;
            if (m.Groups["block"].Success) mode = Mode.Block;
            else if (m.Groups["str"].Success) mode = Mode.String;
            else continue;
            var lead = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            var first = m.Index == lead || text.Substring(lead, m.Index - lead).Trim().Length == 0;
            while (line < starts.Count && starts[line] < end)
            {
                modes[line] = mode;
                atStart[line] = first;
                line++;
            }
        }
        return new States(modes, atStart, lost);
    }

    /// <summary>
    /// CARD-1711: the census ends a raw interpolated string at its first closing quote run, which is the real end unless
    /// that run lies inside a hole (a nested raw string). True when it does, or when a hole holds a comment, a nested
    /// verbatim or raw string, or a literal the literal pattern cannot skip, so that the brace count cannot be trusted.
    /// </summary>
    internal static bool RawHoleLost(string token, int dollars, int quotes)
    {
        var depth = -1;
        var skip = dollars + quotes;
        foreach (Match e in RawPieces.Matches(token))
        {
            if (e.Index < skip) continue;
            var v = e.Value;
            if (v[0] == '{')
            {
                if (depth >= 0) depth += v.Length;
                else if (v.Length >= dollars) depth = 0;
            }
            else if (v[0] == '}')
            {
                if (depth >= 0) depth = v.Length <= depth ? depth - v.Length : -1;
            }
            else if (depth < 0 || v == "\"\"")
            {
                continue;
            }
            else if (InterpolatedQuote.IsMatch(v) || v == "'")
            {
                var m = Literal.Match(token, e.Index);
                if (!m.Success) return true;
                skip = e.Index + m.Length;
            }
            else
            {
                return true;
            }
        }
        return depth >= 0;
    }

    // A catalog value as foreach enumerates it: null is none, a string or dictionary one item, another enumerable its items.
    private static IEnumerable<object?> Items(object? value)
    {
        if (value == null) yield break;
        if (value is string || value is IDictionary || value is not IEnumerable items)
        {
            yield return value;
            yield break;
        }
        foreach (var item in items) yield return item;
    }

    private static object? Field(object? o, string name) => o is IDictionary d && d.Contains(name) ? d[name] : null;

    private static string Text(object? o) => o as string ?? (o == null ? "" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? "");
}
