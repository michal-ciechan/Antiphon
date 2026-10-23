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
    private readonly RunBudgetClock? _budget;
    private readonly TimeSpan _slotWait;
    private RunBudgetClock? _activeBudget;
    private readonly Func<CancellationToken, Task>? _beforeLaunch;

    public List<string> ToolRuns { get; } = [];

    public BaselineComparer(IDriver driver, IBuildSlotClient? slots = null, TimeSpan? timeout = null,
        Func<CancellationToken, Task>? beforeLaunch = null) : this(driver, slots, timeout, beforeLaunch, null, TimeSpan.FromMinutes(45)) { }

    internal BaselineComparer(IDriver driver, IBuildSlotClient? slots, TimeSpan? timeout,
        Func<CancellationToken, Task>? beforeLaunch, RunBudgetClock? budget, TimeSpan slotWait)
    {
        _budget = budget;
        _slotWait = slotWait;
        _driver = driver;
        _slots = slots ?? new FixedSlotClient("off");
        _timeout = timeout ?? TimeSpan.FromMinutes(15);
        _beforeLaunch = beforeLaunch;
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

        if (_timeout <= TimeSpan.Zero)
        {
            RecordExhaustedFetch(baselineRef);
            return results;
        }
        using var ownedBudget = _budget is null ? new RunBudgetClock(TimeProvider.System, _timeout, _timeout + _slotWait) : null;
        _activeBudget = _budget ?? ownedBudget;
        if (_activeBudget!.Token.IsCancellationRequested)
        {
            RecordExhaustedFetch(baselineRef);
            return results;
        }
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activeBudget.Token);
        cancellationToken = work.Token;
        var deadline = _activeBudget.HardWallTimeoutAt;
        var session = await _slots.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var fetch = await RunLeased(
            session,
            "tool-run: baseline git fetch " + baselineRef,
            new DriverRequest("git", ["fetch", "origin", BranchOf(baselineRef)], worktree),
            deadline,
            cancellationToken).ConfigureAwait(false);
        if (fetch.ExitCode != 0 || fetch.TimedOut)
            return results;

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
            var existedBefore = Directory.Exists(checkout);
            var added = false;
            var createdByUs = false;
            try
            {
                var checkoutParent = Path.GetDirectoryName(checkout)!;
                if (SafeWrite.CreateDirectory(checkoutParent, SafeWrite.Under(checkoutParent)) is not null)
                    continue;
                var add = await RunLeased(
                    session,
                    "tool-run: baseline git worktree add " + build.Id,
                    new DriverRequest("git", ["worktree", "add", "--detach", checkout, sha], worktree),
                    deadline,
                    cancellationToken).ConfigureAwait(false);
                added = add.ExitCode == 0 && !add.TimedOut;
                createdByUs = !existedBefore && added;
                if (!added)
                    continue;

                var properties = BuildStep.PropertyArguments([], !OperatingSystem.IsWindows() ? false : OperatingSystem.IsWindows());
                if (ProjectWriteRoots.Preflight(checkout, build.Project, build.OutputPath) is not null)
                    continue;
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
                    if (SafeWrite.CreateDirectory(resultsDir, SafeWrite.Under(runDirectory)) is not null)
                        continue;
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

                var classification = string.Join('\n', results.Select(item => item.Name + " " + item.Kind));
                var classificationRefusal = SafeWrite.Text(
                    Path.Combine(runDirectory, "baseline", "classification.txt"),
                    classification,
                    SafeWrite.Under(runDirectory));
                if (classificationRefusal is not null)
                    throw new IOException("refused: " + classificationRefusal);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested && _activeBudget!.RemainingExecution > TimeSpan.Zero && _activeBudget.RemainingWall > TimeSpan.Zero && createdByUs)
                {
                    await RunLeased(
                        session,
                        "tool-run: baseline git worktree remove " + build.Id,
                        new DriverRequest("git", ["worktree", "remove", "--force", checkout], worktree),
                        deadline,
                        cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (Directory.Exists(checkout) && CanRemoveBaselineCheckout(worktree, checkout))
                            SafeDelete.DeleteCreatedTree(checkout);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                else if (createdByUs || cancellationToken.IsCancellationRequested && (added || Directory.Exists(checkout)))
                    ToolRuns.Add("tool-run: baseline retained canceled workspace " + checkout);
            }
        }

        return results;
    }

    private void RecordExhaustedFetch(string baselineRef) =>
        ToolRuns.Add("tool-run: baseline git fetch " + baselineRef + " skipped: baseline budget exhausted");

    public static string CheckoutPath(string sourceWorktree, string runId, string buildId)
    {
        var name = string.IsNullOrWhiteSpace(runId) ? "run" : runId;
        if (!OutputCleanup.IsPathSegment(name) || !OutputCleanup.IsIsolatedOutputId(buildId))
            throw new InvalidOperationException("refusing unsafe baseline checkout path");
        // CARD-1231: repository censuses skip a path with a bin-* segment, so a checkout named
        // after the build id would scan nothing at the base and pass.
        var leaf = "baseline-" + buildId["bin-".Length..];
        var baselineRoot = Path.Combine(Path.GetTempPath(), "antiphon-checkpoints-baseline");
        var path = Path.GetFullPath(Path.Combine(baselineRoot, name, leaf));
        if (!OutputCleanup.IsStrictlyInside(baselineRoot, path))
            throw new InvalidOperationException("refusing baseline checkout outside the isolated root");
        var source = Path.GetFullPath(sourceWorktree);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.StartsWith(prefix, comparison))
        {
            path = Path.GetFullPath(Path.Combine(baselineRoot, Guid.NewGuid().ToString("N"), leaf));
            if (!OutputCleanup.IsStrictlyInside(baselineRoot, path))
                throw new InvalidOperationException("refusing baseline checkout outside the isolated root");
        }

        return path;
    }

    private static bool CanRemoveBaselineCheckout(string sourceWorktree, string checkout)
    {
        try
        {
            var full = Path.GetFullPath(checkout);
            var baselineRoot = Path.Combine(Path.GetTempPath(), "antiphon-checkpoints-baseline");
            if (!OutputCleanup.IsStrictlyInside(baselineRoot, full))
                return false;
            var source = Path.GetFullPath(sourceWorktree);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var sourcePath = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(sourcePath, comparison) || OutputCleanup.IsStrictlyInside(source, full))
                return false;
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                return false;
            return ContainedCleanup.SafeAncestors(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
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
        var remaining = _activeBudget?.RemainingExecution ?? deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return new DriverResult(ExitCodes.Timeout, "", "baseline timeout", TimedOut: true);
        using var activity = _activeBudget?.EnterActivity();
        SlotLease lease;
        _activeBudget?.SchedulerIdle(true);
        try { lease = await BudgetAdmission.AcquireAsync(_slots, session, label, _activeBudget, activity,
            _activeBudget?.Time ?? TimeProvider.System, _slotWait, null, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new DriverResult(ExitCodes.Timeout, "", "baseline budget exhausted", TimedOut: true); }
        finally { _activeBudget?.SchedulerIdle(false); }
        await using var ownedLease = lease;
        if (lease.ExitCode != 0)
        {
            ToolRuns.Add(label + " slot=" + lease.State + " slot-reason=" + (lease.SlotReason ?? "unknown")
                + " waited=" + lease.WaitedSeconds + "s"
                + (lease.Diagnostic is null ? "" : " " + lease.Diagnostic.Line(label)));
            return new DriverResult(lease.ExitCode, "", "slot " + lease.State + ": " + lease.SlotReason,
                TimedOut: lease.ExitCode == ExitCodes.SlotTimeout);
        }
        remaining = _activeBudget?.RemainingExecution ?? deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
            return new DriverResult(ExitCodes.Timeout, "", "baseline budget exhausted", TimedOut: true);
        if (_beforeLaunch is not null)
            await _beforeLaunch(cancellationToken).ConfigureAwait(false);
        try { return await RowTimeout.RunWithDeadlineAsync(_driver, request, remaining, _activeBudget?.Time ?? TimeProvider.System, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new DriverResult(ExitCodes.Timeout, "", "baseline budget exhausted", TimedOut: true); }
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
