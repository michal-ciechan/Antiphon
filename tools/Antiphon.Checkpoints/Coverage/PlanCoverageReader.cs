using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints.Coverage;

/// <summary>Reads promises, never commands. All coordinates refer to the original Markdown.</summary>
public sealed class PlanCoverageReader
{
    private sealed record Row(string Id, string Text, int Line, string Test, bool Pc, int Offset = 0);
    private sealed record Span(string Value, int Column, int Start, int End);

    public PlanCoverageReport Read(string plan, string text, string? checklist = null)
    {
        var report = new PlanCoverageReport { Plan = plan, PlanSha256 = PlanCoverageReport.Hash(text) };
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var rows = new List<Row>();
        var inSection = false; var fenced = false; var checklistFence = false; var checkpoint = false;
        var inline = new StringBuilder(); string? inlineJson = null; string current = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i]; var trim = line.Trim();
            if (!inSection) { if (trim == "## Verification design") inSection = true; continue; }
            if (!fenced && Regex.IsMatch(trim, @"^#{1,2} ")) break;
            if (trim.StartsWith("```", StringComparison.Ordinal))
            {
                if (!fenced) { checklistFence = trim == "```plan-coverage-v1"; inline.Clear(); }
                else if (checklistFence)
                {
                    if (inlineJson is not null) Invalid(report, "CHECKLIST_INVALID", i + 1, "multiple inline checklists");
                    inlineJson = inline.ToString();
                }
                fenced = !fenced; continue;
            }
            if (fenced) { if (checklistFence) inline.AppendLine(line); continue; }
            if (trim.StartsWith("### ", StringComparison.Ordinal)) { checkpoint = trim == "### Checkpoints"; current = ""; continue; }
            if (checkpoint) continue;
            var idMatch = Regex.Match(trim, @"^(?:\|\s*)?(?<id>(?:V|R)-[0-9]+|PC-[0-9]+[A-Z]?)(?=\s|\||:)");
            if (idMatch.Success)
            {
                current = idMatch.Groups["id"].Value;
                var cells = trim.StartsWith('|') ? PlanTableImporter.SplitRow(line) : [line];
                // A PC mutation cell is deliberately not interpreted as a promise.
                var body = current.StartsWith("PC-", StringComparison.Ordinal) && cells.Count > 2 ? cells[^1] : line;
                var spans = CodeSpans(body, i + 1, report);
                var method = spans.Select(s => NormalizeMethod(s.Value)).FirstOrDefault(s => s is not null && (s.Contains('.') || s.Contains('_') || Regex.IsMatch(body, @"^V-\d+\s+`"))) ?? "";
                if (current.StartsWith("PC-", StringComparison.Ordinal) && method.Length == 0)
                    method = "@" + Regex.Match(body, @"\bV-\d+\b").Value;
                rows.Add(new(current, body, i + 1, method, current.StartsWith("PC-", StringComparison.Ordinal), line.IndexOf(body, StringComparison.Ordinal)));
            }
            else if (current.Length > 0 && !trim.StartsWith('|') && !trim.StartsWith('#')
                && (Regex.IsMatch(trim, @"^(For each |[0-9]+\. )") || Regex.IsMatch(trim, @"^(Verify|Check|Assert|Preserve)\b", RegexOptions.IgnoreCase)))
                rows.Add(new(current, line, i + 1, "", false));
            else if (trim.StartsWith('|'))
                foreach (var span in CodeSpans(line, i + 1, report)) report.Exclusions.Add(new("EXAMPLE", i + 1, span.Column, Name: span.Value, Detail: "input, display or non-verification table"));
        }
        if (!inSection) Invalid(report, "PLAN_INVALID", 0, "missing Verification design section");
        if (fenced) Invalid(report, "PLAN_PARSE", lines.Length, "unterminated fence");
        var bindings = rows.Where(r => !r.Pc && r.Test.Length > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key,
            g => g.Select(r => r.Test).OrderByDescending(t => t.Count(c => c == '.')).Distinct().ToArray(), StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var test = row.Test;
            if (test.StartsWith('@')) test = Resolve(test[1..]);
            if (test.Length == 0) test = Resolve(row.Id);
            // A qualified matrix declaration refines an earlier unqualified paragraph.
            if (!test.Contains('.') && bindings.TryGetValue(row.Id, out var bound))
                test = bound.FirstOrDefault(t => t.EndsWith("." + test, StringComparison.Ordinal)) ?? test;
            var spans = CodeSpans(row.Text, row.Line, report);
            var methodSpans = spans.Where(s => NormalizeMethod(s.Value) is string m && (m == row.Test || m == test)).ToArray();
            if (!row.Pc && test.Length > 0 && row.Test.Length > 0) Add("method", test, methodSpans.FirstOrDefault()?.Column ?? 1);
            foreach (var span in spans)
            {
                var name = span.Value;
                if (methodSpans.Contains(span)) continue;
                var before = row.Text[..span.Start]; var after = row.Text[span.End..];
                if (!row.Pc && (Regex.IsMatch(before, @"PC-\d+[A-Z]? targets\b", RegexOptions.IgnoreCase) || Regex.IsMatch(after, @"^\s*label forms\b", RegexOptions.IgnoreCase)))
                    report.Exclusions.Add(new("EXAMPLE", row.Line, span.Column, row.Id, test, name, Detail: "case key or input label form"));
                else if (row.Pc || Regex.IsMatch(before, @"(?:label|witness|assertion|fails at)\s*$", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(name, @"^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$") && row.Text.StartsWith('|') && !Regex.IsMatch(before, @"check|absent|canary", RegexOptions.IgnoreCase)) Add("label", name, span.Column);
                else if (Regex.IsMatch(before, @"canary\s*$", RegexOptions.IgnoreCase) || Regex.IsMatch(before, @"(?:check|exclude|exclusion|omit|canary)\b[^.;]*$", RegexOptions.IgnoreCase) && Regex.IsMatch(after, @"absent|exclude|canary", RegexOptions.IgnoreCase)) Add("canary", name, span.Column);
                else if (Regex.IsMatch(before, @"(?:verify|check|assert|preserve)\s*$", RegexOptions.IgnoreCase) && Regex.IsMatch(name, @"^[A-Za-z_]\w*$")) Add("member", name, span.Column);
                else if (name.EndsWith("Tests", StringComparison.Ordinal) || row.Text.Contains("explicit internal cases", StringComparison.Ordinal) || row.Text.Contains("Known safe claim Source", StringComparison.Ordinal) || name.Contains('/') || name.Contains('\\') || name.Contains(".cs") || name.StartsWith('[') || name is "Arguments" or "Test" or "string" or "long" or "null" or "true" or "false" or "int" || Regex.IsMatch(before, @"(?:keys|cases|Source|Origin|AgentKind|version|expose|value|equals)\s*$", RegexOptions.IgnoreCase))
                    report.Exclusions.Add(new("NON_OBLIGATION", row.Line, span.Column, row.Id, test, name, Detail: "example, type or expected display value"));
                else Add("unmapped", name, span.Column);
            }
            if (!row.Pc)
            {
            // Legacy lists keep exact clauses; fuzzy English is a finding, not inferred proof.
            foreach (Match sentence in Regex.Matches(row.Text, @"\bVerify\s+([^.;]+)", RegexOptions.IgnoreCase))
                foreach (var clause in Regex.Split(sentence.Groups[1].Value, @",|/|\s+and\s+").Select(c => c.Trim()).Where(c => c.Length > 0))
                {
                    var exact = Regex.Replace(clause, @"\s+against\s+.*$", "", RegexOptions.IgnoreCase).Trim();
                    if (exact.StartsWith('`')) continue;
                    if (Regex.IsMatch(exact, @"^[A-Z][A-Za-z0-9_]*$") && exact != "IDs") Add("member", exact, row.Text.IndexOf(exact, StringComparison.Ordinal) + 1);
                    else Add("unmapped", exact, row.Text.IndexOf(exact, StringComparison.Ordinal) + 1);
                }
            foreach (Match predicate in Regex.Matches(row.Text, @"\b(empty|null)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase))
            {
                var member = predicate.Groups[2].Value;
                if (member.Length == 0 || !char.IsUpper(member[0]))
                {
                    if (member == "foreground") member = "Foreground";
                    else continue; // These compound descriptions need the explicit checklist.
                }
                Add(predicate.Groups[1].Value.ToLowerInvariant(), member, predicate.Index + 1);
            }
            foreach (Match value in Regex.Matches(row.Text, @"exact\s+([A-Z][A-Za-z0-9_]*)\s+blocker")) Add("value", value.Groups[1].Value, value.Index + 1);
            }
            void Add(string kind, string name, int column) => report.Obligations.Add(new(row.Id, test, kind, name, row.Line, column + row.Offset));
        }
        if (inlineJson is not null && checklist is not null) Invalid(report, "CHECKLIST_INVALID", 0, "inline and external checklist conflict");
        var json = checklist ?? inlineJson;
        if (json is not null) MergeChecklist(report, lines, json);
        var merged = new List<CoverageObligation>();
        foreach (var group in report.Obligations.GroupBy(o => (o.Id, o.Test, o.Kind, o.Name)))
        {
            var first = group.OrderBy(o => o.PlanLine).ThenBy(o => o.PlanColumn).First();
            merged.Add(first with { FromChecklist = group.Any(o => o.FromChecklist), Locations = group.Select(o => new CoverageLocation(o.PlanLine, o.PlanColumn)).Distinct().OrderBy(l => l.Line).ThenBy(l => l.Column).ToList() });
        }
        report.Obligations = merged;
        return report;

        string Resolve(string id) => bindings.TryGetValue(id, out var b)
            && b.Select(t => t.Contains('.') ? t[(t.LastIndexOf('.') + 1)..] : t).Distinct().Count() == 1 ? b[0] : "";
    }

    private static string? NormalizeMethod(string value)
    {
        var name = Regex.Replace(value, @"\([^)]*\)$", "");
        return Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$") ? name : null;
    }

    private static List<Span> CodeSpans(string text, int line, PlanCoverageReport report)
    {
        var spans = new List<Span>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '`') continue;
            if (i > 0 && text[i - 1] == '\\') { Invalid(report, "PLAN_PARSE", line, "escaped backtick", i + 1); continue; }
            var start = i; while (i + 1 < text.Length && text[i + 1] == '`') i++;
            var delimiter = text[start..(i + 1)]; var content = i + 1; var end = text.IndexOf(delimiter, content, StringComparison.Ordinal);
            while (end >= 0 && ((end > 0 && text[end - 1] == '`') || end + delimiter.Length < text.Length && text[end + delimiter.Length] == '`'))
                end = text.IndexOf(delimiter, end + delimiter.Length, StringComparison.Ordinal);
            if (end < 0) { Invalid(report, "PLAN_PARSE", line, "unmatched code span", start + 1); break; }
            spans.Add(new(text[content..end], content + 1, start, end + delimiter.Length)); i = end + delimiter.Length - 1;
        }
        return spans;
    }

    private static void MergeChecklist(PlanCoverageReport report, string[] lines, string json)
    {
        report.ChecklistSha256 = PlanCoverageReport.Hash(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            Keys(document.RootElement, ["version", "items"]);
            if (document.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException();
            var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var maps = new List<(string Id, string Test, int Line, string Clause)>();
            foreach (var item in items)
            {
                Keys(item, ["id", "test", "kind", "name", "planLine", "maps"]);
                var id = item.GetProperty("id").GetString()!; var test = item.GetProperty("test").GetString()!;
                var kind = item.GetProperty("kind").GetString()!; var name = item.GetProperty("name").GetString()!; var line = item.GetProperty("planLine").GetInt32();
                var mapping = item.TryGetProperty("maps", out var map) ? map.GetString() : null;
                if (!Regex.IsMatch(id, @"^(?:(?:V|R)-\d+|PC-\d+[A-Z]?)$") || !Regex.IsMatch(test, @"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+$")
                    || kind is not ("method" or "label" or "canary" or "member" or "empty" or "null" or "value") || name is null || name.Length == 0 && kind != "value"
                    || line < 1 || line > lines.Length || lines[line - 1].Length == 0
                    || mapping is not null && !lines[line - 1].Contains(mapping, StringComparison.Ordinal)) throw new InvalidDataException();
                // Lines may extend a V paragraph, but must have an extracted association to that ID.
                if (!report.Obligations.Any(o => o.Id == id && o.PlanLine == line) && !Regex.IsMatch(lines[line - 1], @"\b" + Regex.Escape(id) + @"\b")) throw new InvalidDataException();
                if (mapping is not null) maps.Add((id, test, line, mapping));
                report.Obligations.Add(new(id, test, kind, name, line, Math.Max(1, lines[line - 1].IndexOf(name, StringComparison.Ordinal) + 1), mapping) { FromChecklist = true });
            }
            report.Obligations.RemoveAll(o => o.Kind == "unmapped" && maps.Any(m => m.Id == o.Id && m.Test == o.Test && m.Line == o.PlanLine && m.Clause.Contains(o.Name, StringComparison.Ordinal)));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
        { Invalid(report, "CHECKLIST_INVALID", 0, "malformed, unsupported or stale checklist"); }
    }

    private static void Keys(JsonElement element, string[] allowed)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name)) throw new InvalidDataException();
    }
    private static void Invalid(PlanCoverageReport report, string code, int line, string detail, int column = 1)
    { report.Invalid = true; report.Diagnostics.Add(new(code, line, column, Detail: detail)); }
}
