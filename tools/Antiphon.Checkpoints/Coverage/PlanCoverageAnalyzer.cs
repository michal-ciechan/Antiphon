using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Checkpoints.Coverage;

public sealed class PlanCoverageAnalyzer
{
    public PlanCoverageReport Analyze(string plan, string text, IReadOnlyList<CoverageSource> sources, string? checklist = null)
        => Analyze(plan, text, sources, checklist, null);

    internal PlanCoverageReport Analyze(string plan, string text, IReadOnlyList<CoverageSource> sources, string? checklist, ClassCensusSelection? selection)
    {
        var report = new PlanCoverageReader().Read(plan, text, checklist);
        var index = new TestAssertionIndex(sources);
        report.Sources = sources.OrderBy(s => s.Path, StringComparer.Ordinal).Select(s => new CoverageSelection(s.Path, PlanCoverageReport.Hash(s.Text), index.Classes(s.Path))).ToList();
        report.InputsSha256 = PlanCoverageReport.Hash(report.PlanSha256 + "\n" + report.ChecklistSha256 + "\n"
            + string.Join('\n', report.Sources.Select(s => s.Path + "\0" + s.Sha256)));
        report.Diagnostics.AddRange(index.Diagnostics);
        if (sources.Count == 0 || index.Diagnostics.Any()) report.Invalid = true;
        var cache = new Dictionary<IndexedMethod, (List<IndexedAssertion> Assertions, List<CoverageDiagnostic> Unknown)>();
        foreach (var obligation in report.Obligations)
        {
            if (obligation.Kind == "unmapped") { Finding("PROSE_UNMAPPED", "explicit member mapping required"); continue; }
            var methods = index.Resolve(obligation.Test);
            if (methods.Count != 1)
            {
                Finding(methods.Count == 0 ? "MISSING_METHOD" : "METHOD_UNMAPPED", methods.Count == 0 ? "no selected method" : "ambiguous method binding");
                if (obligation.FromChecklist) report.Invalid = true;
                if (obligation.Id.StartsWith("PC-", StringComparison.Ordinal) && obligation.Kind == "label")
                    report.Pcs.Add(new(obligation.Id, methods.Count == 0 ? "missing-target" : "unmapped", obligation.Name, obligation.Test, 0, 0, []));
                continue;
            }
            var method = methods[0];
            if (obligation.Kind == "method") { obligation.Matches.Add(new(method.Path, method.Syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1)); continue; }
            if (!cache.TryGetValue(method, out var evidence))
            {
                var unknown = new List<CoverageDiagnostic>(); evidence = (index.Assertions(method, unknown), unknown); cache[method] = evidence;
                foreach (var diagnostic in unknown)
                    report.Diagnostics.Add(diagnostic with { Id = obligation.Id, Test = obligation.Test, PlanLine = obligation.PlanLine, PlanColumn = obligation.PlanColumn });
            }
            foreach (var assertion in evidence.Assertions.Where(a => Matches(a, obligation))) obligation.Matches.Add(new(assertion.Path, assertion.Line));
            if (obligation.Matches.Count == 0) Finding("MISSING_" + obligation.Kind.ToUpperInvariant(), "no associated " + obligation.Kind + " assertion", method.Path);
            if (obligation.Id.StartsWith("PC-", StringComparison.Ordinal) && obligation.Kind == "label") AnalyzePc(report, index, obligation, evidence.Assertions, evidence.Unknown);
            void Finding(string code, string detail, string path = "") => report.Diagnostics.Add(new(code, obligation.PlanLine, obligation.PlanColumn, obligation.Id, obligation.Test, obligation.Name, path, Detail: detail));
        }
        ValidateCounts(report, index);
        if (report.SelectedClassCensus && !report.Invalid)
        {
            if (selection is null) report.Diagnostics.Add(new("CLASS_CENSUS_UNMAPPED", Detail: "trusted checkpoint selection context required"));
            else selection.Compare(report, index);
        }
        report.Obligations = report.Obligations.OrderBy(o => o.PlanLine).ThenBy(o => o.PlanColumn).ThenBy(o => o.Id, StringComparer.Ordinal)
            .ThenBy(o => o.Kind, StringComparer.Ordinal).ThenBy(o => o.Name, StringComparer.Ordinal).ThenBy(o => o.Test, StringComparer.Ordinal).ToList();
        report.Diagnostics = report.Diagnostics.Distinct().OrderBy(d => d.PlanLine).ThenBy(d => d.PlanColumn).ThenBy(d => d.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.TestPath, StringComparer.Ordinal).ThenBy(d => d.TestLine).ThenBy(d => d.Code, StringComparer.Ordinal).ToList();
        report.Pcs = report.Pcs.DistinctBy(p => (p.Id, p.Test, p.Target)).ToList();
        return report;
    }
    private static void ValidateCounts(PlanCoverageReport report, TestAssertionIndex index)
    {
        foreach (var promise in report.CountPromises.Distinct())
        {
            var methods = report.Obligations.Where(o => o.Id == promise.Id && o.Kind == "method" && o.Matches.Count > 0)
                .SelectMany(o => index.Resolve(o.Test))
                .Where(m => promise.Class.Length == 0 || m.Class == promise.Class || m.Class.EndsWith("." + promise.Class, StringComparison.Ordinal))
                .DistinctBy(m => (m.Path, m.Syntax.SpanStart)).ToArray();
            // A class-only row describes its run census without promising checklist methods.
            // The ID-wide total remains a promise even when no methods bind.
            if (promise.Class.Length > 0 && methods.Length == 0) continue;
            var actual = methods.Length;
            if (promise.Unit == "results")
            {
                var counts = methods.Select(ResultCount).ToArray();
                if (counts.Any(c => c is null))
                {
                    report.Diagnostics.Add(new("CHECKLIST_COUNT_UNMAPPED", promise.PlanLine, promise.PlanColumn, promise.Id, promise.Class,
                        Detail: "result count requires explicit census for dynamic data sources or class/parameter expansion"));
                    continue;
                }
                actual = counts.Sum(c => c!.Value);
            }
            if (actual != promise.Expected)
                report.Diagnostics.Add(new("CHECKLIST_COUNT_MISMATCH", promise.PlanLine, promise.PlanColumn, promise.Id, promise.Class,
                    Detail: $"{promise.Unit} expected={promise.Expected} actual={actual}"));
        }
    }
    private static int? ResultCount(IndexedMethod method)
    {
        var attributes = method.Syntax.AttributeLists.SelectMany(l => l.Attributes).Select(AttributeName).ToArray();
        var surrounding = method.Syntax.Ancestors().OfType<ClassDeclarationSyntax>().SelectMany(c => c.AttributeLists).SelectMany(l => l.Attributes)
            .Concat(method.Syntax.ParameterList.Parameters.SelectMany(p => p.AttributeLists).SelectMany(l => l.Attributes)).Select(AttributeName);
        if (attributes.Any(Dynamic) || surrounding.Any(n => n == "Arguments" || Dynamic(n))) return null;
        return Math.Max(1, attributes.Count(n => n == "Arguments"));

        static bool Dynamic(string name) => name.Contains("DataSource", StringComparison.Ordinal) || name is "Matrix" or "Repeat";
        static string AttributeName(AttributeSyntax attribute)
        {
            var name = attribute.Name switch
            {
                SimpleNameSyntax s => s.Identifier.ValueText,
                QualifiedNameSyntax q => q.Right.Identifier.ValueText,
                AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
                _ => attribute.Name.ToString()
            };
            return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^9] : name;
        }
    }
    private static bool Matches(IndexedAssertion assertion, CoverageObligation obligation) => obligation.Kind switch
    {
        "label" => TestAssertionIndex.HasLabel(assertion, obligation.Name),
        "canary" => assertion.Canaries.Contains(obligation.Name, StringComparer.Ordinal),
        "member" => assertion.Members.Contains(obligation.Name, StringComparer.Ordinal),
        "empty" => assertion.Empty && assertion.Members.Contains(obligation.Name, StringComparer.Ordinal),
        "null" => assertion.Null && assertion.Members.Contains(obligation.Name, StringComparer.Ordinal),
        "value" => assertion.Expected.Contains(obligation.Name, StringComparer.Ordinal),
        _ => false
    };
    private static void AnalyzePc(PlanCoverageReport report, TestAssertionIndex index, CoverageObligation obligation,
        List<IndexedAssertion> assertions, List<CoverageDiagnostic> unknown)
    {
        var target = assertions.FindIndex(a => TestAssertionIndex.HasLabel(a, obligation.Name));
        if (target < 0)
        {
            var elsewhere = index.Methods.Where(m => !index.Resolve(obligation.Test).Contains(m))
                .Any(m => index.Assertions(m, []).Any(a => TestAssertionIndex.HasLabel(a, obligation.Name)));
            if (elsewhere) report.Diagnostics.Add(new("PC_LABEL_NOT_IN_METHOD", obligation.PlanLine, obligation.PlanColumn, obligation.Id, obligation.Test, obligation.Name, Detail: "target label belongs to another selected method"));
            report.Pcs.Add(new(obligation.Id, "missing-target", obligation.Name, obligation.Test, 0, 0, [])); return;
        }
        var predecessors = assertions.Take(target).ToArray();
        var unlabeled = predecessors.Where(a => TestAssertionIndex.Labels(a).Count == 0).ToArray();
        var otherLabels = predecessors.SelectMany(TestAssertionIndex.Labels).Where(l => l != obligation.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var a in unlabeled.DistinctBy(a => (a.Path, a.Line)))
            report.Diagnostics.Add(new("PC_UNLABELED_PREDECESSOR", obligation.PlanLine, obligation.PlanColumn, obligation.Id, obligation.Test, obligation.Name, a.Path, a.Line, "unlabeled or unresolved-message assertion before target"));
        if (otherLabels.Length > 0)
            report.Diagnostics.Add(new("PC_EARLIER_OTHER_LABEL", obligation.PlanLine, obligation.PlanColumn, obligation.Id, obligation.Test, obligation.Name,
                predecessors[0].Path, predecessors[0].Line, "earlier labels=" + System.Text.Json.JsonSerializer.Serialize(otherLabels) + "; execution order unproven"));
        report.Pcs.Add(new(obligation.Id, unlabeled.Length > 0 ? "unlabeled-predecessor" : unknown.Count > 0 ? "unmapped" : "static-labeled", obligation.Name,
            obligation.Test, assertions[target].Line, unlabeled.FirstOrDefault()?.Line ?? 0, otherLabels));
    }
}
