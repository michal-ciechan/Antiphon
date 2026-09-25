namespace Antiphon.Checkpoints;

public static class ReportMerger
{
    public static ReportModel Merge(IReadOnlyList<ReportModel> runs)
    {
        if (runs.Count == 0)
            throw new InvalidOperationException("no runs to merge");
        var ordered = runs.OrderBy(run => run.EndedAt).ToList();
        var latest = ordered[^1];
        var byId = new Dictionary<string, (ReportRow Row, int Earlier)>(StringComparer.Ordinal);
        foreach (var run in ordered)
        {
            foreach (var row in run.Rows)
            {
                if (byId.TryGetValue(row.Id, out var existing))
                    byId[row.Id] = (Clone(row, existing.Earlier + 1), existing.Earlier + 1);
                else
                    byId[row.Id] = (Clone(row, 0), 0);
            }
        }

        var merged = new ReportModel
        {
            SchemaVersion = 1,
            RunId = latest.RunId,
            ManifestPath = latest.ManifestPath,
            ManifestHash = latest.ManifestHash,
            Commit = latest.Commit,
            Branch = latest.Branch,
            Worktree = latest.Worktree,
            Host = latest.Host,
            StartedAt = ordered[0].StartedAt,
            EndedAt = latest.EndedAt,
            WallSeconds = (latest.EndedAt - ordered[0].StartedAt).TotalSeconds,
            ExitCode = ExitCodes.FromRowStates(byId.Values.Select(item => item.Row.ExitCode)),
            Unlisted = latest.Unlisted,
            Evidence = latest.Evidence,
            Builds = latest.Builds,
            Rows = byId.Values.Select(item => item.Row).ToList(),
        };
        merged.SequentialEquivalentSeconds = merged.Rows.Sum(row => row.Seconds);
        merged.Verdict = merged.ExitCode == 0 ? "GREEN" : "RED";
        return merged;
    }

    private static ReportRow Clone(ReportRow row, int earlierAttempts)
    {
        var copy = new ReportRow
        {
            Id = row.Id,
            Group = row.Group,
            Filter = row.Filter,
            Command = row.Command,
            Build = row.Build,
            State = row.State,
            ExitCode = row.ExitCode,
            Executed = row.Executed,
            Passed = row.Passed,
            Failed = row.Failed,
            Skipped = row.Skipped,
            Reruns = row.Reruns + earlierAttempts,
            Trx = row.Trx,
            Seconds = row.Seconds,
            Line = row.Line,
            Failures = row.Failures,
            RerunLines = row.RerunLines.ToList(),
            SlowClasses = row.SlowClasses,
            Attempt = row.Attempt,
        };
        if (earlierAttempts > 0 && copy.Line is not null && !copy.Line.Contains("reruns=", StringComparison.Ordinal))
            copy.Line += " reruns=" + copy.Reruns;
        return copy;
    }
}
