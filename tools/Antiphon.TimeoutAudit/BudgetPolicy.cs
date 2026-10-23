using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.TimeoutAudit;

internal static class BudgetPolicy
{
    public static AuditReport Run(AuditOptions options)
    {
        if (!Directory.Exists(options.Repository)) throw new ArgumentException("Repository does not exist");
        foreach (var root in options.ReceiptRoots)
            if (!Directory.Exists(root)) throw new ArgumentException("Receipt root does not exist: " + root);
        var report = new AuditReport();
        SourceInventory.Read(Path.GetFullPath(options.Repository), report);
        ReceiptMeasurements.Read(options.ReceiptRoots, report);
        var exceptions = ReadExceptions(options.Exceptions);
        foreach (var declaration in report.Inventory)
        {
            var subject = declaration.Kind == "test" ? declaration.Test : declaration.Kind == "row" ? declaration.Filter : null;
            IGrouping<string, Observation>[] groups = subject == null ? [] : report.Observations.Where(x => x.Subject == subject).GroupBy(x => x.Context).ToArray();
            if (groups.Length == 0)
            {
                report.Budgets.Add(new(declaration.Id, subject ?? declaration.Expression, "unknown", 0, null, null, null, null, "provisional", []));
                continue;
            }
            foreach (var group in groups)
            {
                var samples = group.Where(x => x.Outcome == "Passed" && x.Seconds > 0 && double.IsFinite(x.Seconds)).ToArray();
                var values = samples.Select(x => x.Seconds).Order().ToArray();
                var p95 = Rank(values, .95);
                var median = values.Length == 0 ? (double?)null : values.Length % 2 == 0 ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2 : values[values.Length / 2];
                // At least 20 comparable successful independent runs are needed for a p95 claim.
                // No guessed load factor: unrecorded host or load makes this provisional.
                var provisional = values.Length < 20 || group.Key.Contains("host:unknown", StringComparison.Ordinal) || group.Key.Contains("load:unknown", StringComparison.Ordinal) || group.Key.Contains("concurrency:unknown", StringComparison.Ordinal);
                var recommendation = p95 == null ? (double?)null : Math.Max(1, Math.Ceiling(2 * p95.Value));
                var excess = declaration.Seconds != null && recommendation != null && declaration.Seconds > recommendation;
                var excepted = exceptions.Contains(declaration.Id);
                var status = provisional ? "provisional" : declaration.Seconds == null ? "unresolved" : excepted && excess ? "justified" : excess ? "candidate" : "already-tight";
                report.Budgets.Add(new(declaration.Id, subject!, group.Key, values.Length, median, p95, values.Length == 0 ? null : values[^1], recommendation, status, samples.Select(x => x.Evidence).Distinct().ToArray()));
                if (!provisional && excess && !excepted && !report.Violations.Any(x => x.DeclarationId == declaration.Id))
                    report.Violations.Add(new(declaration.Id, "exceeds-2x-p95"));
                if (!provisional && declaration.Seconds is <= 0 && !report.Violations.Any(x => x.DeclarationId == declaration.Id))
                    report.Violations.Add(new(declaration.Id, "nonpositive-deadline"));
            }
        }
        return report;
    }
    private static double? Rank(double[] values, double percentile) => values.Length == 0 ? null : values[(int)Math.Ceiling(values.Length * percentile) - 1];
    private static HashSet<string> ReadExceptions(string? path)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (path == null) return result;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string Get(string key) => item.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
            if (Get("declarationId").Length > 0 && Get("justification").Trim().Length > 0 && Get("measurement").Trim().Length > 0 && Regex.IsMatch(Get("card"), @"^CARD-[0-9]+$"))
                result.Add(Get("declarationId"));
        }
        return result;
    }
}
