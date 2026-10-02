namespace Antiphon.Checkpoints;

public static class ReportMerger
{
    public static ReportModel Merge(IReadOnlyList<ReportModel> runs)
    {
        if (runs.Count == 0)
            throw new InvalidOperationException("no runs to merge");
        var ordered = runs.OrderBy(run => run.EndedAt).ToList();
        var latest = ordered[^1];
        // Every attempt contributes to the merged run's provenance, including a row later
        // replaced by the same checkpoint ID. Validate before replacement can hide it.
        if (ordered.Any(run => run.SchemaVersion is not (2 or 3) || run.Commit != latest.Commit ||
            !ReportValidator.IsSourceEligible(run.Source, latest.Commit) ||
            run.Source.Start.Fingerprint != latest.Source.Start.Fingerprint ||
            run.Rows.Any(row => !ReportValidator.IsSourceEligible(row.Source, latest.Commit) ||
                row.Source.Start.Fingerprint != latest.Source.Start.Fingerprint ||
                (row.Command is null ? row.Source.BuildSource != latest.Source.BuildSource
                    : row.Source.BuildSource != "notApplicable"))))
            throw new InvalidOperationException("cannot merge incompatible or unknown checkpoint source evidence");
        var byId = new Dictionary<string, (ReportRow Row, int Earlier)>(StringComparer.Ordinal);
        foreach (var run in ordered)
        {
            foreach (var row in run.Rows)
            {
                if (byId.TryGetValue(row.Id, out var existing))
                {
                    if (existing.Row.Repeat?.Requested != row.Repeat?.Requested || existing.Row.Build != row.Build)
                        throw new InvalidOperationException("cannot merge incompatible repeat or build binding for row " + row.Id);
                    byId[row.Id] = (Clone(row, existing.Earlier + 1), existing.Earlier + 1);
                }
                else
                    byId[row.Id] = (Clone(row, 0), 0);
            }
        }

        var merged = new ReportModel
        {
            SchemaVersion = byId.Values.Any(item => item.Row.Repeat is not null) ? 3 : 2,
            Source = latest.Source,
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
            MaxConcurrentRows = ordered.Max(run => run.MaxConcurrentRows),
            MaxConcurrentBuilds = ordered.Max(run => run.MaxConcurrentBuilds),
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
            Slot = row.Slot,
            SlotReason = row.SlotReason,
            WaitedSeconds = row.WaitedSeconds,
            Executed = row.Executed,
            Passed = row.Passed,
            Failed = row.Failed,
            Skipped = row.Skipped,
            Reruns = row.Reruns + (row.Repeat is null ? earlierAttempts : 0),
            Trx = row.Trx,
            Seconds = row.Seconds,
            Line = row.Line,
            Failures = row.Failures,
            RerunLines = row.RerunLines.ToList(),
            SlowClasses = row.SlowClasses,
            Attempt = row.Attempt,
            Source = row.Source,
            Repeat = row.Repeat,
            Timings = row.Timings,
            Attempt = earlierAttempts + 1,
        };
        if (row.Repeat is null && earlierAttempts > 0 && copy.Line is not null && !copy.Line.Contains("reruns=", StringComparison.Ordinal))
            copy.Line += " reruns=" + copy.Reruns;
        return copy;
    }
}
