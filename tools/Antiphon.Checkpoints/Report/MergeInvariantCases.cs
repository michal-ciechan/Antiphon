using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

/// <summary>Reconciles a source- and selection-bound compiled inventory with the invariant row's TRX.</summary>
internal static class MergeInvariantCases
{
    private const string RowPrefix = "CP-invariants-";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Inventory(string SourceSha, RequiredRow[] Rows);
    private sealed record RequiredRow(string Id, string Project, string Filter, string[] Uids);

    internal static string? Validate(ReportModel report, string expectedSha, IReadOnlyList<string> selectedIds,
        int expectedRepeat, string? requiredCases)
    {
        var selected = report.Rows.Where(row => selectedIds.Count == 0 || selectedIds.Contains(row.Id, StringComparer.Ordinal)).ToArray();
        if (requiredCases is null)
            return selected.Any(row => row.Id.StartsWith(RowPrefix, StringComparison.Ordinal)) ? "invariant_case_roster_required" : null;
        var inventory = JsonSerializer.Deserialize<Inventory>(requiredCases, Json);
        if (inventory is null || inventory.SourceSha != expectedSha || inventory.Rows is not { Length: > 0 }
            || inventory.Rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.Id)
                || string.IsNullOrWhiteSpace(row.Project) || string.IsNullOrWhiteSpace(row.Filter)
                || row.Uids is not { Length: > 0 } || row.Uids.Any(string.IsNullOrWhiteSpace)
                || row.Uids.Distinct(StringComparer.Ordinal).Count() != row.Uids.Length)
            || inventory.Rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != inventory.Rows.Length
            || expectedRepeat != 1)
            return "invariant_case_roster_invalid";

        var required = inventory.Rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var checkedCount = 0;
        foreach (var row in selected)
        {
            if (!required.TryGetValue(row.Id, out var obligation))
            {
                if (row.Id.StartsWith(RowPrefix, StringComparison.Ordinal))
                    return "invariant_case_row_unbound:" + row.Id;
                continue;
            }
            var builds = report.Builds.Where(build => build.Id == row.Build).ToArray();
            if (builds.Length != 1 || builds[0].State != "ok" || !SameProject(builds[0].Project, obligation.Project)
                || !ReportValidator.IsSourceEligible(builds[0].Source, expectedSha)
                || builds[0].Source.Start.Fingerprint != report.Source.Start.Fingerprint
                || row.Filter != obligation.Filter || row.Command is not null)
                return "invariant_case_selection_mismatch:" + row.Id;
            if (row.Trx is null || row.Line is null || !row.Line.Contains(" trx=" + row.Trx + " slot=", StringComparison.Ordinal))
                return "invariant_case_trx_unbound:" + row.Id;

            var trx = StrictTrxReport.Parse(row.Trx, (row.Executed ?? 0) + (row.Skipped ?? 0));
            if (!trx.Ok || trx.Executed != row.Executed || trx.Passed != row.Passed || trx.Skipped != row.Skipped)
                return "invariant_case_trx_ineligible:" + row.Id;

            var observed = new HashSet<Guid>();
            foreach (var result in trx.Results)
                if (!Guid.TryParse(result.TestId, out var id) || !observed.Add(id))
                    return "invariant_case_result_identity:" + row.Id;
            foreach (var uid in obligation.Uids)
                if (!observed.Remove(TrxId(uid)))
                    return "invariant_case_missing:" + obligation.Project + "/" + uid;
            if (observed.Count != 0)
                return "invariant_case_unexpected:" + row.Id;
            checkedCount++;
        }

        return checkedCount == 0 ? "invariant_case_rows_missing" : null;
    }

    // MTP TRX 2.2.2's TestFx GuidFromString uses XXH128 over UTF-16LE. Unlike display names or method names,
    // the UID carries the TUnit argument slot. A reporter encoding change makes this join fail closed.
    private static Guid TrxId(string uid) => new(XxHash128.Hash(Encoding.Unicode.GetBytes(uid)));

    private static bool SameProject(string a, string b)
    {
        static string Canonical(string project)
        {
            var path = project.Replace('\\', '/').TrimEnd('/');
            return path.EndsWith(".csproj", StringComparison.Ordinal) ? path : path + "/" + path.Split('/')[^1] + ".csproj";
        }
        return Canonical(a) == Canonical(b);
    }
}
