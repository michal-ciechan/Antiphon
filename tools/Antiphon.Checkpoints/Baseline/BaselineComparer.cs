namespace Antiphon.Checkpoints;

public sealed class BaselineClassification
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string RowId { get; init; } = "";
}

public sealed class BaselineComparer
{
    private readonly IDriver _driver;

    public BaselineComparer(IDriver driver) => _driver = driver;

    public async Task<IReadOnlyList<BaselineClassification>> CompareAsync(
        string worktree,
        string runDirectory,
        string baselineRef,
        IReadOnlyList<ReportRow> redRows,
        IReadOnlyList<BuildSpec> builds,
        CancellationToken cancellationToken)
    {
        var results = new List<BaselineClassification>();
        if (redRows.Count == 0 || string.IsNullOrWhiteSpace(baselineRef))
            return results;

        var fetch = await _driver.RunAsync(
            new DriverRequest("git", ["fetch", "origin", BranchOf(baselineRef)], worktree),
            cancellationToken).ConfigureAwait(false);
        _ = fetch;

        var rev = await _driver.RunAsync(
            new DriverRequest("git", ["rev-parse", baselineRef], worktree),
            cancellationToken).ConfigureAwait(false);
        var sha = rev.Stdout.Trim();
        if (rev.ExitCode != 0 || sha.Length == 0)
            return results;

        foreach (var group in redRows.Where(row => row.Failures.Count > 0 && !string.IsNullOrWhiteSpace(row.Filter)).GroupBy(row => row.Build))
        {
            var build = builds.FirstOrDefault(item => item.Id == group.Key);
            if (build is null)
                continue;
            var baselineRoot = Path.Combine(runDirectory, "baseline");
            Directory.CreateDirectory(baselineRoot);
            var added = false;
            try
            {
                var add = await _driver.RunAsync(
                    new DriverRequest("git", ["worktree", "add", "--detach", baselineRoot, sha], worktree),
                    cancellationToken).ConfigureAwait(false);
                added = add.ExitCode == 0;
                if (!added)
                    continue;

                var properties = BuildStep.PropertyArguments([], !OperatingSystem.IsWindows() ? false : OperatingSystem.IsWindows());
                var buildArgs = BuildStep.BuildArguments(build.Project, build.OutputPath, properties, 4);
                var built = await _driver.RunAsync(
                    new DriverRequest("dotnet", buildArgs, baselineRoot, Path.Combine(runDirectory, "baseline", build.Id + "-build.log")),
                    cancellationToken).ConfigureAwait(false);
                if (built.ExitCode != 0)
                    continue;

                var names = group.SelectMany(row => row.Failures.Select(failure => failure.Name)).Distinct().ToList();
                var filter = RerunPolicy.MethodFilter(names);
                var resultsDir = Path.Combine(runDirectory, "baseline", "rows");
                Directory.CreateDirectory(resultsDir);
                var runArgs = BuildStep.RunArguments(build.Project, build.OutputPath, properties, filter, resultsDir, "run.trx");
                await _driver.RunAsync(new DriverRequest("dotnet", runArgs, baselineRoot), cancellationToken).ConfigureAwait(false);
                var trxPath = Path.Combine(resultsDir, "run.trx");
                var parsed = File.Exists(trxPath) ? TrxReport.Parse(trxPath) : null;
                foreach (var row in group)
                {
                    foreach (var failure in row.Failures)
                    {
                        var kind = Classify(failure.Name, parsed);
                        failure.Baseline = kind;
                        results.Add(new BaselineClassification { Name = failure.Name, Kind = kind, RowId = row.Id });
                    }
                }

                var classification = string.Join('\n', results.Select(item => item.Name + " " + item.Kind));
                File.WriteAllText(Path.Combine(runDirectory, "baseline", "classification.txt"), classification);
            }
            finally
            {
                if (Directory.Exists(baselineRoot) || added)
                {
                    await _driver.RunAsync(
                        new DriverRequest("git", ["worktree", "remove", "--force", baselineRoot], worktree),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        return results;
    }

    public static string Classify(string name, TrxParseResult? baseline)
    {
        if (baseline is null || !baseline.Ok)
            return "NEW";
        if (baseline.FailureNames.Contains(name))
            return "INHERITED";
        if (baseline.ExecutedNames.Contains(name))
            return "INTRODUCED";
        return "NEW";
    }

    private static string BranchOf(string reference)
    {
        var text = reference.Trim();
        return text.StartsWith("origin/", StringComparison.Ordinal) ? text["origin/".Length..] : text;
    }
}
