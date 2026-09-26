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
    private readonly IBuildSlotClient _slots;
    private readonly TimeSpan _timeout;

    public List<string> ToolRuns { get; } = [];

    public BaselineComparer(IDriver driver, IBuildSlotClient? slots = null, TimeSpan? timeout = null)
    {
        _driver = driver;
        _slots = slots ?? new FixedSlotClient("off");
        _timeout = timeout ?? TimeSpan.FromMinutes(15);
    }

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

        var deadline = DateTimeOffset.UtcNow + _timeout;
        var session = await _slots.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var fetch = await RunLeased(
            session,
            "tool-run: baseline git fetch " + baselineRef,
            new DriverRequest("git", ["fetch", "origin", BranchOf(baselineRef)], worktree),
            deadline,
            cancellationToken).ConfigureAwait(false);
        _ = fetch;

        var rev = await RunLeased(
            session,
            "tool-run: baseline git rev-parse " + baselineRef,
            new DriverRequest("git", ["rev-parse", baselineRef], worktree),
            deadline,
            cancellationToken).ConfigureAwait(false);
        var sha = rev.Stdout.Trim();
        if (rev.ExitCode != 0 || sha.Length == 0 || rev.TimedOut)
            return results;

        var runId = Path.GetFileName(runDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var group in redRows.Where(row => row.Failures.Count > 0 && !string.IsNullOrWhiteSpace(row.Filter)).GroupBy(row => row.Build))
        {
            var build = builds.FirstOrDefault(item => item.Id == group.Key);
            if (build is null)
                continue;
            var checkout = CheckoutPath(worktree, runId, build.Id);
            var added = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(checkout)!);
                var add = await RunLeased(
                    session,
                    "tool-run: baseline git worktree add " + build.Id,
                    new DriverRequest("git", ["worktree", "add", "--detach", checkout, sha], worktree),
                    deadline,
                    cancellationToken).ConfigureAwait(false);
                added = add.ExitCode == 0 && !add.TimedOut;
                if (!added)
                    continue;

                var properties = BuildStep.PropertyArguments([], !OperatingSystem.IsWindows() ? false : OperatingSystem.IsWindows());
                var buildArgs = BuildStep.BuildArguments(build.Project, build.OutputPath, properties, 4);
                var buildLog = Path.Combine(runDirectory, "baseline", build.Id, "build.log");
                var built = await RunLeased(
                    session,
                    "tool-run: baseline build " + build.Id,
                    new DriverRequest("dotnet", buildArgs, checkout, buildLog),
                    deadline,
                    cancellationToken).ConfigureAwait(false);
                if (built.ExitCode != 0 || built.TimedOut)
                    continue;

                var byClass = new Dictionary<string, List<(ReportRow Row, ReportFailure Failure)>>(StringComparer.Ordinal);
                foreach (var row in group)
                {
                    foreach (var failure in row.Failures)
                    {
                        var filters = RerunPolicy.MethodFilters([failure.Name]);
                        var key = filters.Count == 0 ? "" : ClassKey(filters[0]);
                        if (!byClass.TryGetValue(key, out var list))
                            byClass[key] = list = [];
                        list.Add((row, failure));
                    }
                }

                foreach (var (classKey, failures) in byClass)
                {
                    var names = failures.Select(item => item.Failure.Name).Distinct().ToList();
                    var filters = RerunPolicy.MethodFilters(names);
                    if (filters.Count == 0)
                        continue;
                    var rowId = failures[0].Row.Id;
                    var resultsDir = Path.Combine(runDirectory, "baseline", "rows", rowId, classKey);
                    Directory.CreateDirectory(resultsDir);
                    var runArgs = BuildStep.RunArguments(build.Project, build.OutputPath, properties, filters[0], resultsDir, "run.trx");
                    await RunLeased(
                        session,
                        "tool-run: baseline run " + build.Id + " " + filters[0],
                        new DriverRequest("dotnet", runArgs, checkout),
                        deadline,
                        cancellationToken).ConfigureAwait(false);
                    var trxPath = Path.Combine(resultsDir, "run.trx");
                    var parsed = File.Exists(trxPath) ? TrxReport.Parse(trxPath) : null;
                    foreach (var (row, failure) in failures)
                    {
                        var kind = Classify(failure.Name, parsed);
                        failure.Baseline = kind;
                        results.Add(new BaselineClassification { Name = failure.Name, Kind = kind, RowId = row.Id });
                    }
                }

                Directory.CreateDirectory(Path.Combine(runDirectory, "baseline"));
                var classification = string.Join('\n', results.Select(item => item.Name + " " + item.Kind));
                File.WriteAllText(Path.Combine(runDirectory, "baseline", "classification.txt"), classification);
            }
            finally
            {
                if (added || Directory.Exists(checkout))
                {
                    await RunLeased(
                        session,
                        "tool-run: baseline git worktree remove " + build.Id,
                        new DriverRequest("git", ["worktree", "remove", "--force", checkout], worktree),
                        DateTimeOffset.UtcNow.AddMinutes(1),
                        CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        if (Directory.Exists(checkout))
                            Directory.Delete(checkout, recursive: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        return results;
    }

    public static string CheckoutPath(string sourceWorktree, string runId, string buildId)
    {
        var name = string.IsNullOrWhiteSpace(runId) ? "run" : runId;
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "antiphon-checkpoints-baseline", name, buildId));
        var source = Path.GetFullPath(sourceWorktree);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.StartsWith(prefix, comparison))
            path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "antiphon-checkpoints-baseline", Guid.NewGuid().ToString("N"), buildId));
        return path;
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

    private async Task<DriverResult> RunLeased(
        SlotSession session,
        string label,
        DriverRequest request,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        ToolRuns.Add(label);
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return new DriverResult(ExitCodes.Timeout, "", "baseline timeout", TimedOut: true);
        await using var lease = await _slots.AcquireAsync(session, label, cancellationToken).ConfigureAwait(false);
        if (lease.ExitCode == ExitCodes.SlotTimeout)
            return new DriverResult(ExitCodes.SlotTimeout, "", "slot timeout", TimedOut: true);
        return await RowTimeout.RunWithDeadlineAsync(_driver, request, remaining, cancellationToken).ConfigureAwait(false);
    }

    private static string ClassKey(string filter)
    {
        var parts = filter.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : "class";
    }

    private static string BranchOf(string reference)
    {
        var text = reference.Trim();
        return text.StartsWith("origin/", StringComparison.Ordinal) ? text["origin/".Length..] : text;
    }
}
