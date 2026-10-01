using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public static class ReportValidator
{
    private static readonly Regex FullOid = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.Compiled);
    private static readonly Regex Field = new(@"(?:^|\s)([A-Za-z][A-Za-z0-9]*)=([^\s]*)", RegexOptions.Compiled);

    public static bool IsSourceEligible(SourceEvidence? source, string expectedSha)
    {
        if (!FullOid.IsMatch(expectedSha) || source is null || source.Version != 1 || source.End is null)
            return false;
        var start = source.Start;
        var end = source.End;
        return source.State == "clean" && (source.BuildSource is "verified" or "notApplicable")
            && start.CaptureStatus == "known" && end.CaptureStatus == "known"
            && start.Commit == expectedSha && end.Commit == expectedSha
            && start.DirtyFiles == 0 && end.DirtyFiles == 0
            && start.Fingerprint is not null && Regex.IsMatch(start.Fingerprint, "^[0-9a-f]{64}$")
            && start.Fingerprint == end.Fingerprint;
    }

    public static string? Validate(ReportModel report, string expectedSha, IReadOnlyList<string>? selectedIds = null)
    {
        if (report.SchemaVersion != 2 || !IsSourceEligible(report.Source, expectedSha) || report.Commit != expectedSha)
            return "report_source_ineligible";
        var rows = selectedIds is { Count: > 0 }
            ? report.Rows.Where(row => selectedIds.Contains(row.Id, StringComparer.Ordinal)).ToList()
            : report.Rows;
        if (rows.Count == 0 || selectedIds is { Count: > 0 } && rows.Count != selectedIds.Distinct(StringComparer.Ordinal).Count())
            return "selected_rows_missing";
        foreach (var row in rows)
        {
            if (row.ExitCode != 0 || row.State != "green" ||
                (row.Command is null && (row.Executed is not > 0 || row.Passed != row.Executed || row.Failed != 0 || row.Skipped != 0)))
                return "row_failed";
            if (!IsSourceEligible(row.Source, expectedSha) || row.Source.Start.Fingerprint != report.Source.Start.Fingerprint ||
                (row.Command is null ? row.Source.BuildSource != report.Source.BuildSource
                    : row.Source.BuildSource != "notApplicable"))
                return "row_source_disagreement";
            if (row.Line is null || !row.Line.StartsWith("CHECKPOINT " + row.Id + " ", StringComparison.Ordinal))
                return "row_receipt_missing";
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Field.Matches(row.Line))
            {
                if (!fields.TryAdd(match.Groups[1].Value, match.Groups[2].Value)) return "duplicate_receipt_token";
            }
            if (!fields.TryGetValue("source", out var token) || token != expectedSha ||
                !fields.TryGetValue("dirty", out var dirty) || dirty != "0" ||
                !fields.TryGetValue("sourceState", out var state) || state != "clean" ||
                !fields.TryGetValue("buildSource", out var build) || build != row.Source.BuildSource ||
                !fields.TryGetValue("commit", out var commit) || commit != expectedSha)
                return "receipt_source_disagreement";
            if (row.Executed is int count &&
                (!fields.TryGetValue("executed", out var executed) || executed != count.ToString()))
                return "receipt_count_disagreement";
        }
        return null;
    }
}
