using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Checkpoints.Coverage;

/// <summary>Trusted, syntax-only selection built from imported rows and their own project candidates.</summary>
internal sealed class ClassCensusSelection
{
    private sealed record Origin(string Id, int Line, int Column);
    private readonly Dictionary<(string Path, int Span), (IndexedMethod Method, Origin Row)> _selected = [];
    private readonly List<CoverageDiagnostic> _unmapped = [];

    internal static bool Supports(CheckpointSpec row, out string reason)
    {
        reason = "";
        if (row.IsCommand) { reason = "command selection"; return false; }
        var parts = row.Filter?.Split('/');
        if (parts is null || parts.Length != 5 || parts[0] != "") { reason = "unsupported filter syntax"; return false; }
        if (parts.Any(p => p.Contains('['))) { reason = "category selection"; return false; }
        if (parts[1] != "*") { reason = "assembly operand must be wildcard"; return false; }
        if (parts[2] != "*" && !Regex.IsMatch(parts[2], @"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$")) { reason = "namespace must be wildcard or exact"; return false; }
        if (parts[4] != "*") { reason = "method segment must be wildcard"; return false; }
        // Parentheses may wrap each OR operand, but cannot introduce other operators.
        if (parts[3].Split('|').Any(s => !Regex.IsMatch(s, @"^(?:[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\*?|\([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\*?\))$")))
        { reason = "unsupported class operand grammar"; return false; }
        return true;
    }

    internal void Add(CheckpointSpec row, string plan, TestAssertionIndex? index)
    {
        var origin = Locate(row.Id, plan);
        if (!Supports(row, out var reason)) { Unmapped(origin, reason); return; }
        if (index is null) throw new InvalidOperationException("supported selection needs its project index");
        var parts = row.Filter!.Split('/');
        var operands = parts[3].Split('|').Select(s => s.Trim('(', ')')).ToArray();
        var classes = index.Declarations.Where(c => (parts[2] == "*" || parts[2] == c.Namespace) && operands.Any(o => Matches(c, o))).ToArray();
        foreach (var c in classes)
        {
            var unsafeReason = Shape(c, index, new HashSet<string>(StringComparer.Ordinal), false);
            if (unsafeReason is not null) { Unmapped(origin, unsafeReason, c); continue; }
            foreach (var method in index.Methods.Where(m => m.Path == c.Path && ReferenceEquals(m.Syntax.Parent, c.Syntax) && IsTest(m.Syntax)))
            {
                var identity = (method.Path, method.Syntax.SpanStart);
                if (!_selected.TryGetValue(identity, out var previous) || origin.Line < previous.Row.Line)
                    _selected[identity] = (method, origin);
            }
        }
    }

    internal void Compare(PlanCoverageReport report, TestAssertionIndex index)
    {
        report.Diagnostics.AddRange(_unmapped);
        // An unsupported row may select any roster member; do not invent set differences
        // from an incomplete selection. The unmapped verdict already refuses certification.
        if (_unmapped.Count > 0) return;
        // Successfully bound checklist methods, independent of requirement family or display alias.
        var roster = report.Obligations.Where(o => o.FromChecklist && o.Kind == "method" && o.Matches.Count > 0)
            .SelectMany(o => index.Resolve(o.Test).Select(m => (Method: m, Obligation: o)))
            .GroupBy(x => (x.Method.Path, x.Method.Syntax.SpanStart))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Obligation.PlanLine).ThenBy(x => x.Obligation.PlanColumn).First());
        foreach (var (identity, value) in _selected)
        {
            if (roster.ContainsKey(identity)) continue;
            var (m, row) = value;
            report.Diagnostics.Add(new("CLASS_CENSUS_MISMATCH", row.Line, row.Column, row.Id, m.Qualified,
                TestPath: m.Path, TestLine: Line(m.Syntax), Detail: "selected method absent from checklist"));
        }
        foreach (var (identity, value) in roster)
        {
            if (_selected.ContainsKey(identity)) continue;
            var (m, o) = value;
            report.Diagnostics.Add(new("CLASS_CENSUS_MISMATCH", o.PlanLine, o.PlanColumn, o.Id, m.Qualified,
                TestPath: m.Path, TestLine: Line(m.Syntax), Detail: "checklist method not selected"));
        }
    }

    private static bool Matches(IndexedClass c, string operand)
    {
        var wildcard = operand.EndsWith('*'); var token = operand.TrimEnd('*');
        var name = token.Contains('.') ? c.Class : c.Syntax.Identifier.ValueText;
        return wildcard ? name.StartsWith(token, StringComparison.Ordinal) : name == token;
    }
    private static int Line(SyntaxNode n) => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    private static Origin Locate(string id, string plan)
    {
        var lines = plan.Replace("\r\n", "\n").Split('\n');
        var inCheckpoints = false; var fence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) { fence = !fence; continue; }
            if (fence) continue;
            if (lines[i].StartsWith("### Checkpoints", StringComparison.Ordinal)) { inCheckpoints = true; continue; }
            if (inCheckpoints && lines[i].StartsWith('#')) break;
            if (!inCheckpoints) continue;
            var match = Regex.Match(lines[i], @"^\s*\|\s*(" + Regex.Escape(id) + @")\s*\|");
            if (match.Success) return new(id, i + 1, match.Groups[1].Index + 1);
        }
        throw new InvalidDataException("checkpoint selection has no original row");
    }
    private void Unmapped(Origin row, string reason, IndexedClass? c = null) =>
        _unmapped.Add(new("CLASS_CENSUS_UNMAPPED", row.Line, row.Column, row.Id, c?.Class ?? "", TestPath: c?.Path ?? "", TestLine: c is null ? 0 : Line(c.Syntax), Detail: reason));

    private static IEnumerable<AttributeSyntax> Attributes(SyntaxNode node) => node switch
    {
        ClassDeclarationSyntax c => c.AttributeLists.SelectMany(l => l.Attributes),
        MethodDeclarationSyntax m => m.AttributeLists.SelectMany(l => l.Attributes),
        _ => []
    };
    private static string AttributeName(AttributeSyntax a) => a.Name.ToString().Replace("global::", "", StringComparison.Ordinal);
    private static bool IsTest(MethodDeclarationSyntax m) => Attributes(m).Any(a => AttributeName(a) is "Test" or "TestAttribute" or "TUnit.Core.Test" or "TUnit.Core.TestAttribute");

    private static string? Shape(IndexedClass c, TestAssertionIndex index, HashSet<string> visiting, bool baseClass)
    {
        if (!visiting.Add(c.Class)) return "cyclic or ambiguous base chain";
        try
        {
            var root = c.Syntax.SyntaxTree.GetRoot();
            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Where(u => u.Parent is CompilationUnitSyntax || c.Syntax.Ancestors().Any(a => ReferenceEquals(a, u.Parent))).ToArray();
            var direct = c.Syntax.Members.OfType<MethodDeclarationSyntax>().ToArray();
            var attrs = Attributes(c.Syntax).Concat(direct.SelectMany(Attributes)).ToArray();
            var aliases = usings.Where(u => u.Alias is not null).Select(u => u.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
            if (attrs.Any(a => aliases.Contains(a.Name.ToString().Split('.')[0]))) return "ambiguous test attribute alias";
            if (direct.Any(m => Attributes(m).Any(a => AttributeName(a) is "Test" or "TestAttribute")) && index.Declarations.Any(d =>
                    d.Syntax.Identifier.ValueText is "Test" or "TestAttribute" && d.Namespace == c.Namespace)) return "shadowed test attribute";
            if (c.Path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || c.Path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
                || root.GetLeadingTrivia().ToFullString().Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)
                || attrs.Any(a => AttributeName(a).Split('.').Last().Replace("Attribute", "", StringComparison.Ordinal) == "GeneratedCode")) return "generated test shape";
            var directives = root.DescendantTrivia(descendIntoTrivia: true).Select(t => t.GetStructure()).OfType<DirectiveTriviaSyntax>().OrderBy(d => d.SpanStart).ToArray();
            var depth = 0;
            foreach (var d in directives)
            {
                if (d.SpanStart >= c.Syntax.Span.Start && d.SpanStart < c.Syntax.Span.End) return "conditional test shape";
                if (d.SpanStart >= c.Syntax.SpanStart) break;
                if (d is IfDirectiveTriviaSyntax) depth++;
                if (d is EndIfDirectiveTriviaSyntax) depth--;
            }
            if (depth > 0) return "conditional test shape";
            if (attrs.Any(a => AttributeName(a).Split('.').Last().Contains("Skip", StringComparison.Ordinal))) return "runtime skip eligibility";
            foreach (var attribute in attrs)
            {
                var name = AttributeName(attribute);
                var declarations = index.Declarations.Where(d => d.Class == name || d.Class == name + "Attribute"
                    || d.Class == c.Namespace + "." + name || d.Class == c.Namespace + "." + name + "Attribute");
                if (declarations.Any(d => d.Syntax.BaseList?.Types.Any(b => b.Type.ToString().Split('.').Last().EndsWith("SkipAttribute", StringComparison.Ordinal)) == true))
                    return "runtime skip eligibility";
            }
            if (attrs.Any(a => AttributeName(a).Split('.').Last() is "Explicit" or "ExplicitAttribute")) return "explicit test eligibility";
            if (baseClass && direct.Any(IsTest)) return "inherited test execution";
            foreach (var type in c.Syntax.BaseList?.Types ?? [])
            {
                var name = type.Type.ToString();
                if (aliases.Contains(name.Split('.')[0])) return "ambiguous base alias";
                if (name is "object" or "System.Object" or "global::System.Object") continue;
                name = name.Replace("global::", "", StringComparison.Ordinal);
                var namespaces = usings.Where(u => u.Alias is null && u.StaticKeyword.ValueText.Length == 0)
                    .Select(u => u.Name?.ToString()).Where(n => n is not null).ToArray();
                var resolved = index.Declarations.Where(d => d.Class == name || d.Class == c.Namespace + "." + name
                    || !name.Contains('.') && namespaces.Any(ns => d.Class == ns + "." + name)).ToArray();
                if (resolved.Select(d => d.Class).Distinct().Count() != 1) return "unresolved or ambiguous base";
                foreach (var b in resolved)
                {
                    var reason = Shape(b, index, visiting, true);
                    if (reason is not null) return reason;
                }
            }
            return null;
        }
        finally { visiting.Remove(c.Class); }
    }
}
