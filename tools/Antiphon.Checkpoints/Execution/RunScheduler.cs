namespace Antiphon.Checkpoints;

public sealed class SchedulerRequest
{
    public required CheckpointManifest Manifest { get; init; }
    public required IReadOnlyList<CheckpointSpec> Rows { get; init; }
    public required string RunDirectory { get; init; }
    public required string WorkingDirectory { get; init; }
    public required RunState State { get; init; }
    public required IBuildSlotClient Slots { get; init; }
    public SlotSession? Session { get; init; }
    public string Commit { get; init; } = "";
    public IReadOnlyList<string> KnownFlaky { get; init; } = [];
    public int Width { get; init; } = 2;
    public bool SerialAll { get; init; }
    public TimeSpan? RowTimeoutOverride { get; init; }
    public TimeSpan? TotalTimeout { get; init; }
    public TimeSpan? HardWallTimeout { get; init; }
    public RunBudgetClock? Budget { get; init; }
    internal RunBudgetClock? ActiveBudget { get; set; }
    public Action? Publish { get; init; }
    public Func<CancellationToken, Task>? BeforeLaunch { get; init; }
    public Func<string>? CancellationReason { get; init; }
    public Func<string?>? AdmissionBlock { get; init; }
    public bool OwnerBound { get; init; }
    public SourceRunGuard? SourceGuard { get; init; }
    public string? ExpectedSourceSha { get; init; }
    public string? TimingRoot { get; init; }
}

public sealed class SchedulerResult
{
    public int ExitCode { get; init; }
    public List<RowRunResult> Rows { get; init; } = [];
    public RunState State { get; init; } = new();
}

public sealed class RunScheduler
{
    private readonly IDriver _driver;
    private readonly IPlatform _platform;
    private readonly RowRunner _rows;
    private readonly TimeProvider _buildTimeProvider;

    public RunScheduler(IDriver driver, IPlatform platform)
        : this(driver, platform, TimeProvider.System)
    {
    }

    internal RunScheduler(IDriver driver, IPlatform platform, TimeProvider buildTimeProvider)
    {
        _driver = driver;
        _platform = platform;
        _rows = new RowRunner(driver, platform, buildTimeProvider);
        _buildTimeProvider = buildTimeProvider;
    }

    public async Task<SchedulerResult> RunAsync(SchedulerRequest request, CancellationToken cancellationToken)
    {
        await using var ownedBudget = request.Budget is null ? new RunBudgetClock(request.TotalTimeout,
            request.HardWallTimeout ?? (request.TotalTimeout is TimeSpan execution
                ? execution + TimeSpan.FromMinutes(request.Manifest.Build.SlotWaitMinutes) : null), _buildTimeProvider) : null;
        var budget = request.Budget ?? ownedBudget!;
        request.ActiveBudget = budget;
        request.State.HardWallTimeoutAt = budget.HardWallLimit is TimeSpan wallLimit ? budget.StartedAt + wallLimit : null;
        using var schedulerWork = budget.Work();
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);

        SlotSession session;
        try { session = request.Session ?? await request.Slots.ProbeAsync(total.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (budget.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            request.State.Reason = budget.Reason;
            request.State.ExitCode = ExitCodes.Timeout;
            var closed = request.Rows.Select(row => Placeholder(row, budget.Reason ?? "timeout", ExitCodes.Timeout)).ToList();
            request.State.Rows = closed.Select(row => new RowProgress { Id = row.Id, State = row.State, ExitCode = row.ExitCode }).ToList();
            Publish(request);
            return new SchedulerResult { ExitCode = ExitCodes.Timeout, Rows = closed, State = request.State };
        }
        var buildStates = request.Manifest.Builds.ToDictionary(
            build => build.Id,
            build => new BuildProgress { Id = build.Id, State = "pending" },
            StringComparer.Ordinal);
        var neededBuilds = request.Rows.Where(row => !row.IsCommand).Select(row => row.Build).ToHashSet(StringComparer.Ordinal);
        foreach (var build in buildStates.Values.Where(build => !neededBuilds.Contains(build.Id)))
            build.State = "unused";
        request.State.Builds = buildStates.Values.ToList();
        request.State.Rows = request.Rows.Select(row => new RowProgress { Id = row.Id, State = "queued" }).ToList();
        Publish(request);

        var pending = request.Rows.ToList();
        var reportedBuilds = new HashSet<string>(StringComparer.Ordinal);
        var running = new List<(CheckpointSpec Spec, Task<RowRunResult> Task, RowProgress Progress)>();
        var finished = new List<RowRunResult>();
        Task? buildTask = null;

        try
        {
        while (pending.Count > 0 || running.Count > 0)
        {
            var externallyCanceled = cancellationToken.IsCancellationRequested;
            var ownerEnded = externallyCanceled && request.OwnerBound;
            var totalTimedOut = total.IsCancellationRequested && !externallyCanceled;
            if (totalTimedOut || externallyCanceled)
            {
                var state = ownerEnded ? request.CancellationReason?.Invoke() ?? "owner-ended" : "skipped";
                var code = ownerEnded ? ExitCodes.OwnerEnded : ExitCodes.Timeout;
                foreach (var row in pending.ToList())
                {
                    pending.Remove(row);
                    Mark(request, row.Id, state, code);
                    finished.Add(Placeholder(row, state, code));
                }

                SkipPendingBuilds(request, buildStates);

                if (totalTimedOut || externallyCanceled && !ownerEnded)
                    _driver.Kill(entireProcessTree: true);
                Publish(request);
                break;
            }

            var admissionBlock = request.AdmissionBlock?.Invoke();
            if (admissionBlock is null && request.SourceGuard?.Reason is not null)
                admissionBlock = request.SourceGuard.Reason;
            if (admissionBlock is not null)
            {
                var admissionCode = admissionBlock.StartsWith("source_", StringComparison.Ordinal)
                    ? ExitCodes.Invalid : ExitCodes.OwnerEnded;
                foreach (var row in pending.ToList())
                {
                    pending.Remove(row);
                    Mark(request, row.Id, admissionBlock, admissionCode);
                    var refused = Placeholder(row, admissionBlock, admissionCode);
                    refused.Source = request.SourceGuard?.Copy("unknown") ?? new SourceEvidence();
                    finished.Add(refused);
                }
                SkipPendingBuilds(request, buildStates);
                Publish(request);
            }
            else
            {
                foreach (var row in pending.Where(row => BuildFailed(row, buildStates)).ToList())
                {
                    pending.Remove(row);
                    var build = buildStates[row.Build!];
                    var admission = build.AdmissionExitCode != 0;
                    var state = admission ? build.SlotReason is "source_changed" or "source_unknown" ? build.SlotReason
                        : build.Slot == "timeout" ? "slot-timeout" : "slot-refused"
                        : build.TerminalReason ?? "build-failed";
                    var code = admission ? build.AdmissionExitCode : ExitCodes.Invalid;
                    var receipt = Placeholder(row, state, code);
                    receipt.TerminalReason = build.TerminalReason;
                    receipt.ExecutedSeconds = 0;
                    receipt.WallSeconds = 0;
                    if (admission)
                    {
                        receipt.Slot = build.Slot;
                        receipt.SlotReason = build.SlotReason;
                        receipt.WaitedSeconds = build.WaitedSeconds;
                        receipt.Line = CheckpointLine.Format(new CheckpointLineModel
                        {
                            Name = row.Id, Commit = request.Commit, Build = "failed", Filter = row.Filter ?? "",
                            Executed = "0", Passed = "0", Failed = "0", Skipped = "0",
                            Slot = build.Slot, SlotReason = build.SlotReason, WaitedSeconds = build.WaitedSeconds,
                        });
                    }
                    Mark(request, row.Id, state, code);
                    var rowProgress = request.State.Rows.First(item => item.Id == row.Id);
                    rowProgress.Slot = receipt.Slot;
                    rowProgress.SlotReason = receipt.SlotReason;
                    rowProgress.WaitedSeconds = receipt.WaitedSeconds;
                    rowProgress.Line = receipt.Line;
                    rowProgress.ExecutedSeconds = receipt.ExecutedSeconds;
                    rowProgress.WallSeconds = receipt.WallSeconds;
                    rowProgress.TerminalReason = receipt.TerminalReason;
                    finished.Add(receipt);
                    receipt.Source = build.Source;
                }

                var exclusiveRunning = running.Any(item => IsExclusive(item.Spec, request));
                // A ready exclusive row claims the idle lane before the next build does.
                var readyExclusive = running.Count == 0 && pending.FirstOrDefault(row =>
                    row.IsCommand || buildStates.TryGetValue(row.Build ?? "", out var progress) && progress.State == "ok") is { } ready
                    && IsExclusive(ready, request) &&
                    (request.SerialAll || ready.IsCommand || !reportedBuilds.Contains(ready.Build ?? ""));
                if (buildTask is null && !exclusiveRunning && !readyExclusive)
                {
                    var nextBuild = pending.FirstOrDefault(row => !row.IsCommand &&
                        buildStates.TryGetValue(row.Build ?? "", out var progress) && progress.State == "pending");
                    if (nextBuild is not null)
                    {
                        var buildSpec = request.Manifest.Builds.First(build => build.Id == nextBuild.Build);
                        buildTask = BuildOneAsync(request, buildSpec, buildStates[buildSpec.Id], session, budget, total.Token);
                        Publish(request);
                    }
                }

                if (buildTask?.IsCompleted == true)
                {
                    await buildTask.ConfigureAwait(false);
                    buildTask = null;
                    continue;
                }

                while (true)
                {
                    exclusiveRunning = running.Any(item => IsExclusive(item.Spec, request));
                    if (Pick(pending, running.Count, request, buildStates, exclusiveRunning, buildTask is not null) is not CheckpointSpec next)
                        break;
                    pending.Remove(next);
                    var rowProgress = request.State.Rows.First(row => row.Id == next.Id);
                    rowProgress.State = "running";
                    rowProgress.StartedAt = DateTimeOffset.UtcNow;
                    rowProgress.LastOutputAt = rowProgress.StartedAt;
                    var concurrent = running.Count + 1;
                    if (concurrent > request.State.MaxConcurrentRows)
                        request.State.MaxConcurrentRows = concurrent;
                    Publish(request);
                    var buildState = next.IsCommand
                        ? "n/a"
                        : reportedBuilds.Add(next.Build ?? "") ? "ok" : "reused";
                    running.Add((next, RunRowAsync(request, next, session, rowProgress, buildState, budget, total.Token), rowProgress));
                }
            }

            if (running.Count == 0 && buildTask is null)
            {
                if (!pending.Any(row => !row.IsCommand && buildStates.TryGetValue(row.Build ?? "", out var build) && build.State == "pending"))
                    break;
                continue;
            }

            var tasks = running.Select(item => (Task)item.Task).ToList();
            if (buildTask is not null)
                tasks.Add(buildTask);
            Task completed;
            schedulerWork.SetWorking(false);
            try { completed = await Task.WhenAny(tasks).ConfigureAwait(false); }
            finally { schedulerWork.SetWorking(true); }
            if (completed == buildTask)
            {
                await buildTask!.ConfigureAwait(false);
                buildTask = null;
                continue;
            }
            var index = running.FindIndex(item => item.Task == completed);
            var (spec, task, progress) = running[index];
            running.RemoveAt(index);
            RowRunResult result;
            try
            {
                result = await task.ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested && request.OwnerBound)
                    CloseResult(result, request.CancellationReason?.Invoke() ?? "owner-ended", ExitCodes.OwnerEnded);
            }
            catch (OperationCanceledException)
            {
                result = ClosedRow(request, spec, cancellationToken, progress, budget);
            }

            progress.State = result.State;
            progress.ExitCode = result.ExitCode;
            progress.Line = result.Line;
            progress.Slot = result.Slot;
            progress.SlotReason = result.SlotReason;
            progress.WaitedSeconds = result.WaitedSeconds;
            progress.Seconds = result.Seconds > 0 ? result.Seconds : Elapsed(progress.StartedAt);
            progress.Repeat = result.Repeat;
            progress.Timings = result.Timings;
            progress.TerminalReason = result.TerminalReason;
            finished.Add(result);
            Publish(request);
        }

        foreach (var (spec, task, progress) in running)
        {
            RowRunResult result;
            try
            {
                result = await task.ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested && request.OwnerBound)
                    CloseResult(result, request.CancellationReason?.Invoke() ?? "owner-ended", ExitCodes.OwnerEnded);
            }
            catch (OperationCanceledException)
            {
                result = ClosedRow(request, spec, cancellationToken, progress, budget);
            }
            progress.State = result.State;
            progress.ExitCode = result.ExitCode;
            progress.Slot = result.Slot;
            progress.SlotReason = result.SlotReason;
            progress.WaitedSeconds = result.WaitedSeconds;
            progress.Repeat = result.Repeat;
            progress.Timings = result.Timings;
            progress.TerminalReason = result.TerminalReason;
            finished.Add(result);
            Publish(request);
        }

        try
        {
            if (buildTask is not null)
                await buildTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        var codes = finished.Select(row => row.ExitCode).ToArray();
        var exit = cancellationToken.IsCancellationRequested
            ? request.OwnerBound ? ExitCodes.OwnerEnded : ExitCodes.Canceled
            : codes.Contains(ExitCodes.OwnerEnded) ? ExitCodes.OwnerEnded
            : budget.Reason is not null && !codes.Contains(ExitCodes.Invalid) && !codes.Contains(ExitCodes.RestoreFailed) ? ExitCodes.Timeout
            : ExitCodes.FromRowStates(codes);
        request.State.Budget = budget.Snapshot(TimeSpan.FromMinutes(request.Manifest.Build.SlotWaitMinutes));
        request.State.Reason = cancellationToken.IsCancellationRequested
            ? request.OwnerBound ? request.CancellationReason?.Invoke() ?? "owner-ended" : "caller-requested"
            : budget.Reason ?? finished.FirstOrDefault(row => row.TerminalReason is not null)?.TerminalReason;
        request.State.ExitCode = exit;
        Publish(request);
        return new SchedulerResult { ExitCode = exit, Rows = finished, State = request.State };
        }
        catch (Exception failure)
        {
            total.Cancel();
            foreach (var (spec, task, progress) in running)
            {
                try
                {
                    var drained = await task.ConfigureAwait(false);
                    if (failure is StatePublicationException && !finished.Any(row => row.Id == spec.Id))
                    {
                        finished.Add(drained);
                        progress.State = drained.State;
                        progress.ExitCode = drained.ExitCode;
                        progress.Line = drained.Line;
                        progress.Slot = drained.Slot;
                        progress.SlotReason = drained.SlotReason;
                        progress.WaitedSeconds = drained.WaitedSeconds;
                        progress.Seconds = drained.Seconds;
                        progress.Repeat = drained.Repeat;
                        progress.Timings = drained.Timings;
                    }
                }
                catch { /* preserve the original scheduler failure after draining */ }
            }
            if (buildTask is not null)
            {
                try { await buildTask.ConfigureAwait(false); }
                catch { /* preserve the original scheduler failure after draining */ }
            }
            if (failure is StatePublicationException publication)
                publication.PartialResult = new SchedulerResult
                {
                    ExitCode = ExitCodes.Invalid, Rows = finished, State = request.State,
                };
            throw;
        }
    }

    // A row's deadline: its explicit timeout, else the estimate-derived one, else the manifest default;
    // --row-timeout replaces whichever applies.
    internal static int RowDeadlineMinutes(CheckpointSpec spec, CheckpointManifest manifest, TimeSpan? rowTimeoutOverride)
    {
        if (rowTimeoutOverride is TimeSpan over)
            return Math.Max(1, (int)Math.Ceiling(over.TotalMinutes));
        return spec.TimeoutMinutes ?? RowTimeout.DeriveRowMinutes(spec.EstimatedMinutes, null, manifest.Timeouts.RowMinutes);
    }

    // CARD-1356 D-6: a scheduled build runs under the largest deadline of the rows that need it, at least 15 minutes
    // and at least the manifest row default; --row-timeout replaces it exactly as it replaces a row's deadline.
    internal static int BuildDeadlineMinutes(CheckpointManifest manifest, IEnumerable<CheckpointSpec> rows, string buildId, TimeSpan? rowTimeoutOverride)
    {
        if (rowTimeoutOverride is TimeSpan over)
            return Math.Max(1, (int)Math.Ceiling(over.TotalMinutes));
        var minutes = Math.Max(15, manifest.Timeouts.RowMinutes);
        foreach (var row in rows)
            if (!row.IsCommand && string.Equals(row.Build, buildId, StringComparison.Ordinal))
                minutes = Math.Max(minutes, RowDeadlineMinutes(row, manifest, null));
        return minutes;
    }

    internal static bool IsPtyProject(string? project)
    {
        if (string.IsNullOrWhiteSpace(project))
            return false;
        var normalized = project.Replace('\\', '/');
        return normalized.Contains("tests/Antiphon.Agents.Pty.Tests", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("tests/Antiphon.PtyHost.Tests", StringComparison.OrdinalIgnoreCase);
    }

    private async Task BuildOneAsync(
        SchedulerRequest request,
        BuildSpec build,
        BuildProgress progress,
        SlotSession session,
        RunBudgetClock budget,
        CancellationToken cancellationToken)
    {
        using var activity = budget.Work();
        var wallStarted = _buildTimeProvider.GetTimestamp();
        long? admittedAt = null;
        var before = OutputCleanup.MatchingIsolatedDirectories(request.WorkingDirectory, build.Id);
        try
        {
        if (!OutputCleanup.IsIsolatedOutputId(build.Id))
        {
            progress.AdmissionExitCode = ExitCodes.Invalid;
            progress.SlotReason = "unsafe-build-id";
            progress.State = "failed";
            return;
        }

        progress.State = "building";
        progress.StartedAt = DateTimeOffset.UtcNow;
        var deadlineMinutes = BuildDeadlineMinutes(request.Manifest, request.Rows, build.Id, request.RowTimeoutOverride);
        progress.DeadlineMinutes = deadlineMinutes;
        request.State.MaxConcurrentBuilds = Math.Max(
            request.State.MaxConcurrentBuilds,
            request.State.Builds.Count(item => item.State == "building"));
        Publish(request);
        var repeat = request.Rows.First(row => row.Build == build.Id).Repeat;
        var properties = BuildStep.PropertyArguments(request.Manifest.Build.Properties, _platform.IsWindows, repeat,
            repeat > 1 ? RepeatRequest.CanonicalProject(request.WorkingDirectory, build.Project) : null).ToList();
        if (!string.IsNullOrWhiteSpace(build.Project) && !string.IsNullOrWhiteSpace(build.OutputPath))
            BuildStep.AppendSharedLayout(properties, request.WorkingDirectory, build.Project, build.OutputPath);
        var log = Path.Combine(request.RunDirectory, "builds", build.Id, "build.log");
        // CARD-1241: files outside the write roots are stamped before the slot is taken, re-checked once
        // it is granted (AdmitLedger) and verified after release (VerifyLedger).
        BuildConfinement.Ledger? ledger = null;
        if (_driver is IConfinedDriver confined)
        {
            var captured = confined.CaptureLedger(new DriverRequest("dotnet",
                BuildStep.BuildArguments(build.Project, build.OutputPath, properties, 0), request.WorkingDirectory, log));
            if (captured.Refusal is not null)
            {
                progress.AdmissionExitCode = ExitCodes.Invalid;
                progress.SlotReason = "refused: " + captured.Refusal;
                progress.State = "failed";
                Publish(request);
                return;
            }

            ledger = captured.Value;
        }

        CheckpointBuildBinding? built = null;
        DriverResult? result = null;
        var launched = false;
        var sourceOk = true;
        var started = DateTimeOffset.UtcNow;
        try
        {
            progress.Slot = "acquiring";
            Publish(request);
            await using var lease = await budget.AcquireAsync(request.Slots, session, "build:" + build.Id, activity,
                TimeSpan.FromMinutes(request.Manifest.Build.SlotWaitMinutes), value => progress.WaitedSeconds = Math.Max(progress.WaitedSeconds, value), cancellationToken).ConfigureAwait(false);
            progress.Slot = lease.State;
            progress.SlotReason = lease.SlotReason;
            progress.WaitedSeconds = Math.Max(progress.WaitedSeconds, lease.WaitedSeconds);
            Publish(request);
            if (lease.ExitCode != 0)
            {
                progress.AdmissionExitCode = lease.ExitCode;
                progress.TerminalReason = lease.ExitCode == ExitCodes.SlotTimeout ? "slot-admission-deadline-exceeded" : null;
                progress.State = "failed";
                Publish(request);
                return;
            }

            admittedAt = _buildTimeProvider.GetTimestamp();
            cancellationToken.ThrowIfCancellationRequested();
            var binding = CheckpointBuildBinding.Expected(request.WorkingDirectory, build.Project, build.OutputPath,
                properties, request.State.Source.Start);
            if (request.SourceGuard?.Observe() == false)
            {
                progress.AdmissionExitCode = ExitCodes.Invalid;
                progress.SlotReason = request.SourceGuard.Reason;
                progress.State = "failed";
                progress.Source = request.SourceGuard.Copy("unknown");
                return;
            }
            var writeRefusal = ProjectWriteRoots.Preflight(request.WorkingDirectory, build.Project, build.OutputPath);
            if (writeRefusal is not null)
            {
                progress.AdmissionExitCode = ExitCodes.Invalid;
                progress.SlotReason = "refused: " + writeRefusal;
                progress.State = "failed";
                Publish(request);
                return;
            }
            var invalidated = binding.Invalidate() ?? SafeWrite.CreateFreshDirectory(binding.OutputPath, SafeWrite.Under(binding.OutputPath));
            if (invalidated is not null)
            {
                progress.AdmissionExitCode = ExitCodes.Invalid;
                progress.SlotReason = "refused: " + invalidated;
                progress.State = "failed";
                Publish(request);
                return;
            }
            var cpu = lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4;
            var args = BuildStep.BuildArguments(build.Project, build.OutputPath, properties, cpu);
            started = DateTimeOffset.UtcNow;
            if (request.BeforeLaunch is not null)
                await request.BeforeLaunch(cancellationToken).ConfigureAwait(false);
            var admitted = ledger is not null ? BuildConfinement.AdmitLedger(ledger, request.Manifest.Build.AdmissionHash == "strict").Settlement : null;
            if (admitted?.Kind is not null)
            {
                ProcessDriver.AppendLog(log, admitted.Detail);
                progress.AdmissionExitCode = ExitCodes.Invalid;
                progress.SlotReason = BuildConfinement.ReportedRefusal(admitted.Detail);
                progress.State = "failed";
                Publish(request);
                return;
            }
            launched = true;
            built = binding;
            result = await RowTimeout.RunWithDeadlineAsync(
                _driver,
                new DriverRequest("dotnet", args, request.WorkingDirectory, log) { Ledger = ledger },
                TimeSpan.FromMinutes(deadlineMinutes),
                _buildTimeProvider,
                cancellationToken).ConfigureAwait(false);
            sourceOk = request.SourceGuard?.Observe() != false;
        }
        catch (OperationCanceledException)
        {
            progress.State = "failed";
            progress.TerminalReason = budget.Reason;
            progress.AdmissionExitCode = ExitCodes.Timeout;
        }

        var outside = launched && ledger is not null ? BuildConfinement.VerifyLedger(ledger) : null;
        if (outside?.Kind is not null)
            ProcessDriver.AppendLog(log, outside.Detail);
        if (result is not null)
        {
            progress.Seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
            progress.State = result.ExitCode == 0 && !result.TimedOut && sourceOk && outside?.Kind is null ? "ok" : "failed";
            var refusal = BuildConfinement.ReportedRefusal(result.Stderr) ?? BuildConfinement.ReportedRefusal(outside?.Detail);
            if (progress.State == "ok")
            {
                var written = built!.Write(result.Exited);
                if (written is not null)
                {
                    progress.State = "failed";
                    progress.AdmissionExitCode = ExitCodes.Invalid;
                    progress.SlotReason = "refused: " + written;
                }
            }
            else if (refusal is not null)
            {
                progress.AdmissionExitCode = ExitCodes.Invalid;
                progress.SlotReason = refusal;
            }
            else if (!sourceOk) { progress.AdmissionExitCode = ExitCodes.Invalid; progress.SlotReason = request.SourceGuard?.Reason; }
            else if (result.TimedOut)
            {
                progress.AdmissionExitCode = ExitCodes.Timeout;
                progress.TerminalReason = "build-deadline-exceeded";
            }
            progress.Source = request.SourceGuard?.Copy(progress.State == "ok" ? "verified" : "unknown") ?? new SourceEvidence();
        }
        else if (outside?.Kind is not null)
        {
            progress.State = "failed";
            progress.AdmissionExitCode = ExitCodes.Invalid;
            progress.SlotReason = BuildConfinement.ReportedRefusal(outside.Detail);
        }

        Publish(request);
        }
        finally
        {
            progress.WallSeconds = _buildTimeProvider.GetElapsedTime(wallStarted).TotalSeconds;
            progress.ExecutedSeconds = admittedAt is long at ? _buildTimeProvider.GetElapsedTime(at).TotalSeconds : 0;
            OutputCleanup.RecordCreatedOutputs(request.State, request.WorkingDirectory, build.Id, before);
        }
    }

    private async Task<RowRunResult> RunRowAsync(
        SchedulerRequest request,
        CheckpointSpec spec,
        SlotSession session,
        RowProgress progress,
        string buildState,
        RunBudgetClock budget,
        CancellationToken cancellationToken)
    {
        using var activity = budget.Work();
        var wallStarted = _buildTimeProvider.GetTimestamp();
        long? admittedAt = null;
        try
        {
        var started = DateTimeOffset.UtcNow;
        progress.Slot = "acquiring";
        Publish(request);
        await using var lease = await budget.AcquireAsync(request.Slots, session, spec.Id + "@" + (spec.Build ?? "command"), activity,
            TimeSpan.FromMinutes(request.Manifest.Build.SlotWaitMinutes), value => progress.WaitedSeconds = Math.Max(progress.WaitedSeconds, value), cancellationToken).ConfigureAwait(false);
        progress.Slot = lease.State;
        progress.SlotReason = lease.SlotReason;
        Publish(request);
        if (lease.ExitCode != 0)
        {
            return new RowRunResult
            {
                Id = spec.Id,
                ExitCode = lease.ExitCode,
                State = lease.State == "timeout" ? "slot-timeout" : "slot-refused",
                BuildState = "failed",
                Slot = lease.State,
                SlotReason = lease.SlotReason,
                WaitedSeconds = progress.WaitedSeconds,
                TerminalReason = lease.ExitCode == ExitCodes.SlotTimeout ? "slot-admission-deadline-exceeded" : null,
                ExecutedSeconds = 0,
                WallSeconds = _buildTimeProvider.GetElapsedTime(wallStarted).TotalSeconds,
                Line = CheckpointLine.Format(new CheckpointLineModel
                {
                    Name = spec.Id,
                    Commit = request.Commit,
                    Build = "failed",
                    Filter = spec.Filter ?? spec.Command ?? "",
                    Slot = lease.State,
                    SlotReason = lease.SlotReason,
                    WaitedSeconds = progress.WaitedSeconds,
                    Command = spec.IsCommand,
                    Executed = "0", Passed = "0", Failed = "0", Skipped = "0",
                }),
            };
        }

        admittedAt = _buildTimeProvider.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var build = spec.IsCommand ? null : request.Manifest.Builds.First(item => item.Id == spec.Build);
        var rowSource = request.SourceGuard?.Copy(spec.IsCommand ? "notApplicable" : "unknown") ?? new SourceEvidence();
        bool Boundary()
        {
            var ok = request.SourceGuard?.Observe() != false;
            if (request.SourceGuard is not null)
            {
                var latest = request.SourceGuard.Copy(rowSource.BuildSource);
                rowSource.End = latest.End;
                rowSource.State = latest.State;
            }
            return ok;
        }
        var minutes = RowDeadlineMinutes(spec, request.Manifest, request.RowTimeoutOverride);
        var result = await _rows.RunAsync(new RowRequest
        {
            Name = spec.Id,
            Project = build?.Project,
            OutputPath = build?.OutputPath,
            Filter = spec.Filter,
            Command = spec.Command,
            ResultsDirectory = Path.Combine(request.RunDirectory, "rows", spec.Id),
            WorkingDirectory = request.WorkingDirectory,
            NoBuild = true,
            BuildStateOverride = buildState,
            MinExecuted = spec.MinExecuted ?? 1,
            Repeat = spec.Repeat,
            Expect = spec.Expect,
            Properties = request.Manifest.Build.Properties.ToList(),
            KnownFlaky = spec.Repeat > 1 ? [] : request.KnownFlaky,
            Commit = request.Commit,
            Source = rowSource,
            SourceBoundary = Boundary,
            ExpectedSourceSha = request.ExpectedSourceSha,
            Slot = lease.State,
            SlotReason = lease.SlotReason,
            WaitedSeconds = progress.WaitedSeconds,
            MaxCpuCount = lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4,
            Deadline = TimeSpan.FromMinutes(minutes),
            BeforeLaunch = request.BeforeLaunch,
            Environment = spec.Environment,
            TimingRoot = request.TimingRoot,
            TimingRunId = Path.GetFileName(request.RunDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            TimingProducer = "tool",
            ProducingBuildId = spec.Build,
        }, TextWriter.Null, cancellationToken).ConfigureAwait(false);
        progress.LastOutputAt = DateTimeOffset.UtcNow;
        result.Id = spec.Id;
        result.Slot = lease.State;
        result.SlotReason = lease.SlotReason;
        result.WaitedSeconds = Math.Max(progress.WaitedSeconds, lease.WaitedSeconds);
        result.Seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
        result.Source = rowSource;
        result.ExecutedSeconds = _buildTimeProvider.GetElapsedTime(admittedAt.Value).TotalSeconds;
        result.WallSeconds = _buildTimeProvider.GetElapsedTime(wallStarted).TotalSeconds;
        result.TerminalReason = result.TimedOut ? "row-deadline-exceeded" : null;
        return result;
        }
        finally
        {
            progress.WallSeconds = _buildTimeProvider.GetElapsedTime(wallStarted).TotalSeconds;
            progress.ExecutedSeconds = admittedAt is long at ? _buildTimeProvider.GetElapsedTime(at).TotalSeconds : 0;
        }
    }

    private static void CloseResult(RowRunResult result, string reason, int exit)
    {
        result.State = reason;
        result.TerminalReason = reason;
        result.ExitCode = exit;
    }

    private static RowRunResult ClosedRow(SchedulerRequest request, CheckpointSpec spec, CancellationToken cancellationToken,
        RowProgress progress, RunBudgetClock budget)
    {
        var block = request.AdmissionBlock?.Invoke();
        var ownerEnded = cancellationToken.IsCancellationRequested && request.OwnerBound;
        var reason = block ?? (ownerEnded ? request.CancellationReason?.Invoke() ?? "owner-ended"
            : cancellationToken.IsCancellationRequested ? "caller-requested" : budget.Reason ?? "timeout");
        var result = Placeholder(spec, reason, ownerEnded || block is not null ? ExitCodes.OwnerEnded
            : cancellationToken.IsCancellationRequested ? ExitCodes.Canceled : ExitCodes.Timeout);
        result.TerminalReason = reason;
        result.WaitedSeconds = progress.WaitedSeconds;
        result.Slot = progress.Slot;
        result.ExecutedSeconds = progress.ExecutedSeconds;
        result.WallSeconds = progress.WallSeconds;
        return result;
    }

    private static bool IsExclusive(CheckpointSpec row, SchedulerRequest request)
    {
        var project = request.Manifest.Builds.FirstOrDefault(build => build.Id == row.Build)?.Project;
        return request.SerialAll || row.Serial || IsPtyProject(project);
    }

    private static CheckpointSpec? Pick(
        List<CheckpointSpec> pending,
        int running,
        SchedulerRequest request,
        Dictionary<string, BuildProgress> builds,
        bool exclusiveRunning,
        bool buildInFlight)
    {
        if (exclusiveRunning)
            return null;
        foreach (var row in pending.ToList())
        {
            if (!row.IsCommand)
            {
                if (!builds.TryGetValue(row.Build ?? "", out var build))
                    continue;
                if (build.State is "pending" or "building" or "failed")
                    continue;
            }

            if (IsExclusive(row, request))
                return running == 0 && !buildInFlight ? row : null;
            return running < Math.Max(1, request.Width) ? row : null;
        }

        return null;
    }

    private static bool BuildFailed(CheckpointSpec row, Dictionary<string, BuildProgress> builds) =>
        !row.IsCommand && builds.TryGetValue(row.Build ?? "", out var build) && build.State == "failed";

    private static void SkipPendingBuilds(SchedulerRequest request, Dictionary<string, BuildProgress> builds)
    {
        foreach (var build in builds.Values.Where(build => build.State == "pending"))
            build.State = "skipped";
        Publish(request);
    }

    private static void Mark(SchedulerRequest request, string id, string state, int exitCode)
    {
        var progress = request.State.Rows.First(row => row.Id == id);
        progress.State = state;
        progress.ExitCode = exitCode;
    }

    private static RowRunResult Placeholder(CheckpointSpec spec, string state, int exitCode) =>
        new()
        {
            Id = spec.Id,
            ExitCode = exitCode,
            State = state,
            BuildState = state == "build-failed" ? "failed" : "n/a",
        };

    private static double Elapsed(DateTimeOffset? started) =>
        started is DateTimeOffset at ? Math.Max(0, (DateTimeOffset.UtcNow - at).TotalSeconds) : 0;

    private static void Publish(SchedulerRequest request)
    {
        request.State.Budget = request.ActiveBudget?.Snapshot(TimeSpan.FromMinutes(request.Manifest.Build.SlotWaitMinutes));
        request.Publish?.Invoke();
    }
}
