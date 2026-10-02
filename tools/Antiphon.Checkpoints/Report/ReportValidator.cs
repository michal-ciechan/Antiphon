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

    public static string? Validate(ReportModel report, string expectedSha, IReadOnlyList<string>? selectedIds = null, int expectedRepeat = 1)
    {
        if (expectedRepeat < 1 || report.SchemaVersion is not (2 or 3)
            || report.SchemaVersion == 2 && report.Rows.Any(row => row.Repeat is not null)
            || report.SchemaVersion == 3 && report.Rows.All(row => row.Repeat is null)
            || !IsSourceEligible(report.Source, expectedSha) || report.Commit != expectedSha)
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
            if (expectedRepeat > 1)
            {
                if (row.Repeat is null || row.Repeat.Requested != expectedRepeat
                    || row.Repeat.HostInvocations != 1 || row.Repeat.Passed != expectedRepeat
                    || row.Repeat.Completed != expectedRepeat || row.Reruns != 0)
                    return "repeat_missing_or_incomplete";
                if (!fields.TryGetValue("repeat", out var repeatToken) || repeatToken != expectedRepeat.ToString()
                    || !fields.TryGetValue("repetitions", out var rounds) || rounds != $"{expectedRepeat}/{expectedRepeat}"
                    || !fields.TryGetValue("hostInvocations", out var invocations) || invocations != "1")
                    return "repeat_receipt_disagreement";
                foreach (var (key, actual) in new[]
                {
                    ("passed", row.Passed), ("failed", row.Failed), ("skipped", row.Skipped),
                })
                    if (!fields.TryGetValue(key, out var written) || written != actual?.ToString())
                        return "repeat_receipt_count_disagreement";
                if (row.Trx is null || !File.Exists(row.Trx)) return "repeat_trx_missing";
                var trx = TrxReport.Parse(row.Trx);
                var checkedRepeat = RepeatEvidenceValidator.Validate(trx, expectedRepeat, row.Repeat.Nonce, 1, []);
                if (!checkedRepeat.Ok || !SameRepeat(checkedRepeat.Evidence, row.Repeat)
                    || row.Executed != trx.Executed || row.Passed != trx.Passed
                    || row.Failed != trx.Failed || row.Skipped != trx.Skipped)
                    return "repeat_trx_disagreement";
            }
            else if (row.Repeat is not null)
                return "repeat_requires_expected_repeat";
        }
        return null;
    }

    private static bool SameRepeat(RepeatEvidence actual, RepeatEvidence stored) =>
        System.Text.Json.JsonSerializer.Serialize(actual) == System.Text.Json.JsonSerializer.Serialize(stored);
}
