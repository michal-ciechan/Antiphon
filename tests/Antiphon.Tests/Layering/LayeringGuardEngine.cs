using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Layering;

/// <summary>Syntax-only, path-based boundary census. It intentionally does not compile the server.</summary>
internal static class LayeringGuardEngine
{
    internal sealed record SourceFile(string Path, string Text);
    internal sealed record Violation(string FromFile, string TypeName, string ToFile)
    {
        public override string ToString() => $"{FromFile} -> {TypeName} @ {ToFile}";
    }
    internal sealed record Ambiguity(string FromFile, string TypeName, string[] Candidates, bool IsViolation)
    {
        public override string ToString() => $"ambiguous:{FromFile} -> {TypeName} @ [{string.Join(", ", Candidates)}]";
    }
    internal sealed record Rule(string Pattern, string Layer, Regex[] Globs);
    internal sealed record Mapping(Dictionary<string, string> Layers, List<string> Errors, List<Rule> Rules,
        Dictionary<string, HashSet<string>> Edges);
    internal sealed record Allowlist(bool Strict, HashSet<Violation> Rows, List<string> Errors);
    internal sealed record Result(Mapping Map, Allowlist Allowlist, List<string> ParseErrors,
        Violation[] Unlisted, Violation[] Listed, Violation[] Stale, Ambiguity[] Ambiguous)
    {
        public bool Passes => Map.Errors.Count == 0 && ParseErrors.Count == 0
            && (!Allowlist.Strict || (Unlisted.Length == 0 && Stale.Length == 0
                && !Ambiguous.Any(a => a.IsViolation)));
        public string Inventory => string.Join("\n", new[]
        {
            $"VIOLATIONS {Unlisted.Length + Ambiguous.Count(a => a.IsViolation)} LISTED {Listed.Length} STALE {Stale.Length} AMBIGUOUS {Ambiguous.Length}"
        }.Concat(Unlisted.Select(v => v.ToString()))
            .Concat(Stale.Select(v => "stale-row:" + v))
            .Concat(Ambiguous.Select(a => a.ToString()))
            .Concat(Map.Errors).Concat(ParseErrors));
        public int UnlistedCount => Unlisted.Length + Ambiguous.Count(a => a.IsViolation);
    }

    private static readonly HashSet<string> LayerNames =
        new(["Host", "Core", "Persistence", "Git", "Runners", "Integrations", "Application"], StringComparer.Ordinal);

    internal static Mapping MapFiles(string text, IEnumerable<string> paths)
    {
        var errors = new List<string>();
        var rules = new List<Rule>();
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            if (line is "[edges]" or "[rules]") { section = line; continue; }
            if (section == "[edges]")
            {
                var parts = line.Split("->", StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !LayerNames.Contains(parts[0]))
                { errors.Add("malformed-edge:" + line); continue; }
                var destinations = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (destinations.Any(d => !LayerNames.Contains(d)) || !edges.TryAdd(parts[0], new(destinations, StringComparer.Ordinal)))
                    errors.Add("malformed-edge:" + line);
            }
            else if (section == "[rules]")
            {
                var match = Regex.Match(line, @"^(\S(?:.*\S)?)\s+(\w+)$", RegexOptions.CultureInvariant);
                if (!match.Success || !LayerNames.Contains(match.Groups[2].Value))
                { errors.Add("malformed-rule:" + line); continue; }
                var pattern = match.Groups[1].Value;
                try
                {
                    var globs = ExpandGlobs(pattern).Select(p => new Regex("^" + Regex.Escape(p)
                        .Replace(@"\*\*", "\u0001").Replace(@"\*", "[^/]*").Replace("\u0001", ".*") + "$",
                        RegexOptions.CultureInvariant)).ToArray();
                    rules.Add(new(pattern, match.Groups[2].Value, globs));
                }
                catch (ArgumentException) { errors.Add("malformed-rule:" + line); }
            }
            else errors.Add("unknown-map-section:" + line);
        }
        foreach (var layer in LayerNames.Where(l => !edges.ContainsKey(l))) errors.Add("missing-edges:" + layer);
        var layers = new Dictionary<string, string>(StringComparer.Ordinal);
        var allPaths = paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var path in allPaths)
        {
            var rule = rules.FirstOrDefault(r => r.Globs.Any(g => g.IsMatch(path)));
            if (rule is null) errors.Add("unmapped-file:" + path);
            else layers.Add(path, rule.Layer);
        }
        foreach (var rule in rules.Where(r => !allPaths.Any(p => r.Globs.Any(g => g.IsMatch(p)))))
            errors.Add("dead-rule:" + rule.Pattern);
        return new(layers, errors, rules, edges);
    }

    private static IEnumerable<string> ExpandGlobs(string pattern)
    {
        // Commas separate globs outside braces and alternatives inside braces.
        var depth = 0;
        var start = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '{') depth++;
            if (pattern[i] == '}') depth--;
            if (depth < 0 || depth > 1) throw new ArgumentException("unbalanced glob");
            if (pattern[i] != ',' || depth != 0) continue;
            foreach (var part in ExpandGlobs(pattern[start..i].Trim())) yield return part;
            start = i + 1;
        }
        if (depth != 0) throw new ArgumentException("unbalanced glob");
        var last = pattern[start..].Trim();
        var open = last.IndexOf('{');
        if (open >= 0)
        {
            var close = last.IndexOf('}', open);
            foreach (var alternative in last[(open + 1)..close].Split(','))
                foreach (var part in ExpandGlobs(last[..open] + alternative + last[(close + 1)..])) yield return part;
        }
        else
        {
            if (last.Length == 0 || last.StartsWith('/') || last.Contains('\\') || last.Split('/').Contains("..")
                || Regex.IsMatch(last, @"[^A-Za-z0-9_./*\-]")) throw new ArgumentException("invalid glob");
            yield return last;
        }
    }

    internal static Allowlist ParseAllowlist(string text, IEnumerable<string> paths)
    {
        var errors = new List<string>();
        var rows = new HashSet<Violation>();
        var lines = text.Replace("\r", "").Split('\n');
        var strict = lines[0] == "strict: true";
        if (lines[0] is not ("strict: true" or "strict: false")) errors.Add("missing-mode-header");
        var known = paths.ToHashSet(StringComparer.Ordinal);
        var grouped = false;
        foreach (var raw in lines.Skip(lines[0] is "strict: true" or "strict: false" ? 1 : 0))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("# slice ", StringComparison.Ordinal))
            { grouped = line[8..].Trim().Length > 0; continue; }
            if (line.StartsWith('#')) continue;
            var match = Regex.Match(line, @"^(\S+) -> ([A-Za-z_][A-Za-z0-9_]*) @ (\S+)$", RegexOptions.CultureInvariant);
            if (!match.Success) { errors.Add("malformed-row:" + line); continue; }
            var row = new Violation(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
            if (!grouped) errors.Add("ungrouped-row:" + line);
            if (!rows.Add(row)) errors.Add("duplicate-row:" + line);
            if (!known.Contains(row.FromFile) || !known.Contains(row.ToFile)) errors.Add("unknown-path:" + line);
        }
        return new(strict, rows, errors);
    }

    private sealed record Declaration(string Name, string Identity, string Namespace, string Path, string Layer, bool Partial);

    internal static Result Analyze(string mapText, string allowlistText, IReadOnlyList<SourceFile> files)
    {
        var map = MapFiles(mapText, files.Select(f => f.Path));
        var allowlist = ParseAllowlist(allowlistText, files.Select(f => f.Path));
        var parseErrors = new List<string>(allowlist.Errors);
        var roots = files.Where(f => map.Layers.ContainsKey(f.Path))
            .ToDictionary(f => f.Path, f => CSharpSyntaxTree.ParseText(f.Text).GetCompilationUnitRoot(), StringComparer.Ordinal);
        var declarations = new List<Declaration>();
        foreach (var (path, root) in roots)
        {
            foreach (var error in root.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
                parseErrors.Add($"syntax-error:{path}:{error}");
            foreach (var node in root.DescendantNodes().Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                var name = node is BaseTypeDeclarationSyntax type ? type.Identifier.ValueText : ((DelegateDeclarationSyntax)node).Identifier.ValueText;
                var ns = NamespaceOf(node);
                var identity = ns + "." + string.Join(".", node.Ancestors().OfType<BaseTypeDeclarationSyntax>()
                    .Reverse().Select(t => t.Identifier.ValueText).Append(name));
                var partial = node is TypeDeclarationSyntax td && td.Modifiers.Any(SyntaxKind.PartialKeyword);
                declarations.Add(new(name, identity, ns, path, map.Layers[path], partial));
            }
        }
        foreach (var group in declarations.GroupBy(d => d.Identity).Where(g => g.Any(d => d.Partial) && g.Select(d => d.Layer).Distinct().Count() > 1))
            parseErrors.Add("split-type:" + group.Key);
        // Partial declarations in one layer are one type. Prefer its primary file for row identity.
        // The split-type check above still rejects declarations spanning different layers.
        var indexedDeclarations = declarations.GroupBy(d => d.Identity).SelectMany(g =>
            g.All(d => d.Partial) && g.Select(d => d.Layer).Distinct().Count() == 1
                ? g.OrderByDescending(d => System.IO.Path.GetFileNameWithoutExtension(d.Path) == d.Name)
                    .ThenBy(d => d.Path, StringComparer.Ordinal).Take(1)
                : g);
        var index = indexedDeclarations.GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.Distinct().ToArray(), StringComparer.Ordinal);
        var globalUsings = roots.Values.SelectMany(r => r.Usings).Where(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) && u.Alias is null)
            .Select(u => u.Name?.ToString() ?? "").ToHashSet(StringComparer.Ordinal);
        var violations = new HashSet<Violation>();
        var ambiguous = new Dictionary<string, Ambiguity>(StringComparer.Ordinal);
        foreach (var (path, root) in roots)
        {
            var own = declarations.Where(d => d.Path == path).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            var declaredNames = root.DescendantTokens().Where(IsDeclaredName).Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
            foreach (var node in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (node.Ancestors().Any(a => a is UsingDirectiveSyntax || a is BaseNamespaceDeclarationSyntax ns && ns.Name.Span.Contains(node.Span))) continue;
                var name = node.Identifier.ValueText;
                if (own.Contains(name) || declaredNames.Contains(name)) continue;
                var attribute = node.Ancestors().OfType<AttributeSyntax>().Any(a => a.Name.Span.Contains(node.Span));
                if (!index.TryGetValue(name, out var candidates) && !(attribute && index.TryGetValue(name + "Attribute", out candidates))) continue;
                // A qualified member is a type reference only when the indexed type has that
                // owner. This keeps value properties and enum members out of the type graph.
                if (node.Parent is MemberBindingExpressionSyntax) continue;
                var qualifiedName = node.Parent switch
                {
                    MemberAccessExpressionSyntax access when access.Name == node => access.Expression + "." + name,
                    QualifiedNameSyntax qualified when qualified.Right == node => qualified.Left + "." + name,
                    _ => null
                };
                if (qualifiedName is not null)
                {
                    qualifiedName = Regex.Replace(qualifiedName.Replace("global::", "", StringComparison.Ordinal), @"<[^<>]*>", "");
                    candidates = candidates.Where(c => c.Identity == qualifiedName || c.Identity.EndsWith("." + qualifiedName, StringComparison.Ordinal)).ToArray();
                    if (candidates.Length == 0) continue;
                }
                if (candidates.Length > 1)
                {
                    var usings = root.Usings.Concat(node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(n => n.Usings))
                        .Where(u => u.Alias is null).Select(u => u.Name?.ToString() ?? "").Concat(globalUsings).ToHashSet(StringComparer.Ordinal);
                    var preferred = candidates.Where(c => c.Namespace == NamespaceOf(node) || usings.Contains(c.Namespace)).ToArray();
                    if (preferred.Length > 0) candidates = preferred;
                }
                if (candidates.Length == 1)
                {
                    var target = candidates[0];
                    if (!Allowed(map, map.Layers[path], target.Layer)) violations.Add(new(path, target.Name, target.Path));
                }
                else
                {
                    var finding = new Ambiguity(path, name, candidates.Select(c => c.Path).Distinct().Order(StringComparer.Ordinal).ToArray(),
                        candidates.All(c => !Allowed(map, map.Layers[path], c.Layer)));
                    ambiguous[path + "\0" + name] = finding;
                }
            }
        }
        Violation[] Sorted(IEnumerable<Violation> rows) => rows.OrderBy(r => r.ToString(), StringComparer.Ordinal).ToArray();
        return new(map, allowlist, parseErrors, Sorted(violations.Except(allowlist.Rows)),
            Sorted(violations.Intersect(allowlist.Rows)), Sorted(allowlist.Rows.Except(violations)),
            ambiguous.Values.OrderBy(a => a.ToString(), StringComparer.Ordinal).ToArray());
    }

    private static bool Allowed(Mapping map, string from, string to) => from == to || map.Edges.GetValueOrDefault(from)?.Contains(to) == true;
    private static string NamespaceOf(SyntaxNode node) => string.Join(".", node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
    private static bool IsDeclaredName(SyntaxToken token) => token.Parent switch
    {
        VariableDeclaratorSyntax n => n.Identifier == token,
        ParameterSyntax n => n.Identifier == token,
        MemberDeclarationSyntax n => n switch
        {
            MethodDeclarationSyntax m => m.Identifier == token,
            PropertyDeclarationSyntax p => p.Identifier == token,
            EventDeclarationSyntax e => e.Identifier == token,
            _ => false
        },
        LabeledStatementSyntax n => n.Identifier == token,
        EnumMemberDeclarationSyntax n => n.Identifier == token,
        SingleVariableDesignationSyntax n => n.Identifier == token,
        TypeParameterSyntax n => n.Identifier == token,
        ForEachStatementSyntax n => n.Identifier == token,
        CatchDeclarationSyntax n => n.Identifier == token,
        _ => false
    };
}
