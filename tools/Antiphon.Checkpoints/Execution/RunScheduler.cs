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
    public Action? Publish { get; init; }
    public Func<CancellationToken, Task>? BeforeLaunch { get; init; }
    public Func<string>? CancellationReason { get; init; }
    public Func<string?>? AdmissionBlock { get; init; }
    public bool OwnerBound { get; init; }
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

    public RunScheduler(IDriver driver, IPlatform platform)
    {
        _driver = driver;
        _platform = platform;
        _rows = new RowRunner(driver, platform);
    }

    public async Task<SchedulerResult> RunAsync(SchedulerRequest request, CancellationToken cancellationToken)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.TotalTimeout is TimeSpan limit && limit > TimeSpan.Zero)
            total.CancelAfter(limit);

        var session = request.Session ?? await request.Slots.ProbeAsync(total.Token).ConfigureAwait(false);
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
            if (admissionBlock is not null)
            {
                foreach (var row in pending.ToList())
                {
                    pending.Remove(row);
                    Mark(request, row.Id, admissionBlock, ExitCodes.OwnerEnded);
                    finished.Add(Placeholder(row, admissionBlock, ExitCodes.OwnerEnded));
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
                    var state = admission ? build.Slot == "timeout" ? "slot-timeout" : "slot-refused" : "build-failed";
                    var code = admission ? build.AdmissionExitCode : ExitCodes.Invalid;
                    var receipt = Placeholder(row, state, code);
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
                    finished.Add(receipt);
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
                        buildTask = BuildOneAsync(request, buildSpec, buildStates[buildSpec.Id], session, total.Token);
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
                    running.Add((next, RunRowAsync(request, next, session, rowProgress, buildState, total.Token), rowProgress));
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
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
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
                    result = Placeholder(spec, request.CancellationReason?.Invoke() ?? "owner-ended", ExitCodes.OwnerEnded);
            }
            catch (OperationCanceledException)
            {
                result = ClosedRow(request, spec, cancellationToken);
            }

            progress.State = result.State;
            progress.ExitCode = result.ExitCode;
            progress.Line = result.Line;
            progress.Slot = result.Slot;
            progress.SlotReason = result.SlotReason;
            progress.WaitedSeconds = result.WaitedSeconds;
            progress.Seconds = result.Seconds > 0 ? result.Seconds : Elapsed(progress.StartedAt);
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
                    result = Placeholder(spec, request.CancellationReason?.Invoke() ?? "owner-ended", ExitCodes.OwnerEnded);
            }
            catch (OperationCanceledException)
            {
                result = ClosedRow(request, spec, cancellationToken);
            }
            progress.State = result.State;
            progress.ExitCode = result.ExitCode;
            progress.Slot = result.Slot;
            progress.SlotReason = result.SlotReason;
            progress.WaitedSeconds = result.WaitedSeconds;
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
            ? request.OwnerBound ? ExitCodes.OwnerEnded : ExitCodes.Timeout
            : codes.Contains(ExitCodes.OwnerEnded) ? ExitCodes.OwnerEnded
            : ExitCodes.FromRowStates(codes);
        request.State.ExitCode = exit;
        Publish(request);
        return new SchedulerResult { ExitCode = exit, Rows = finished, State = request.State };
        }
        catch
        {
            total.Cancel();
            foreach (var (_, task, _) in running)
            {
                try { await task.ConfigureAwait(false); }
                catch { /* preserve the original scheduler failure after draining */ }
            }
            if (buildTask is not null)
            {
                try { await buildTask.ConfigureAwait(false); }
                catch { /* preserve the original scheduler failure after draining */ }
            }
            throw;
        }
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
        CancellationToken cancellationToken)
    {
        progress.State = "building";
        progress.StartedAt = DateTimeOffset.UtcNow;
        request.State.MaxConcurrentBuilds = Math.Max(
            request.State.MaxConcurrentBuilds,
            request.State.Builds.Count(item => item.State == "building"));
        Publish(request);
        try
        {
            await using var lease = await request.Slots.AcquireAsync(session, "build:" + build.Id, cancellationToken).ConfigureAwait(false);
            progress.Slot = lease.State;
            progress.SlotReason = lease.SlotReason;
            progress.WaitedSeconds = lease.WaitedSeconds;
            if (lease.ExitCode != 0)
            {
                progress.AdmissionExitCode = lease.ExitCode;
                progress.State = "failed";
                Publish(request);
                return;
            }

            var properties = BuildStep.PropertyArguments(request.Manifest.Build.Properties, _platform.IsWindows);
            var cpu = lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4;
            var args = BuildStep.BuildArguments(build.Project, build.OutputPath, properties, cpu);
            var log = Path.Combine(request.RunDirectory, "builds", build.Id, "build.log");
            var started = DateTimeOffset.UtcNow;
            if (request.BeforeLaunch is not null)
                await request.BeforeLaunch(cancellationToken).ConfigureAwait(false);
            var result = await RowTimeout.RunWithDeadlineAsync(
                _driver,
                new DriverRequest("dotnet", args, request.WorkingDirectory, log),
                TimeSpan.FromMinutes(Math.Max(15, request.Manifest.Timeouts.RowMinutes)),
                cancellationToken).ConfigureAwait(false);
            progress.Seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
            progress.State = result.ExitCode == 0 && !result.TimedOut ? "ok" : "failed";
        }
        catch (OperationCanceledException)
        {
            progress.State = "failed";
        }

        Publish(request);
    }

    private async Task<RowRunResult> RunRowAsync(
        SchedulerRequest request,
        CheckpointSpec spec,
        SlotSession session,
        RowProgress progress,
        string buildState,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        await using var lease = await request.Slots.AcquireAsync(session, spec.Id + "@" + (spec.Build ?? "command"), cancellationToken).ConfigureAwait(false);
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
                WaitedSeconds = lease.WaitedSeconds,
                Line = CheckpointLine.Format(new CheckpointLineModel
                {
                    Name = spec.Id,
                    Commit = request.Commit,
                    Build = "failed",
                    Filter = spec.Filter ?? spec.Command ?? "",
                    Slot = lease.State,
                    SlotReason = lease.SlotReason,
                    WaitedSeconds = lease.WaitedSeconds,
                    Command = spec.IsCommand,
                }),
            };
        }

        var build = spec.IsCommand ? null : request.Manifest.Builds.First(item => item.Id == spec.Build);
        var minutes = spec.TimeoutMinutes ?? RowTimeout.DeriveRowMinutes(spec.EstimatedMinutes, null, request.Manifest.Timeouts.RowMinutes);
        if (request.RowTimeoutOverride is TimeSpan over)
            minutes = Math.Max(1, (int)Math.Ceiling(over.TotalMinutes));
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
            Expect = spec.Expect,
            Properties = request.Manifest.Build.Properties.ToList(),
            KnownFlaky = request.KnownFlaky,
            Commit = request.Commit,
            Slot = lease.State,
            SlotReason = lease.SlotReason,
            WaitedSeconds = lease.WaitedSeconds,
            MaxCpuCount = lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4,
            Deadline = TimeSpan.FromMinutes(minutes),
            BeforeLaunch = request.BeforeLaunch,
            Environment = spec.Environment,
        }, TextWriter.Null, cancellationToken).ConfigureAwait(false);
        progress.LastOutputAt = DateTimeOffset.UtcNow;
        result.Id = spec.Id;
        result.Slot = lease.State;
        result.SlotReason = lease.SlotReason;
        result.WaitedSeconds = lease.WaitedSeconds;
        result.Seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
        return result;
    }

    private static RowRunResult ClosedRow(SchedulerRequest request, CheckpointSpec spec, CancellationToken cancellationToken)
    {
        var block = request.AdmissionBlock?.Invoke();
        if (block is not null && !(cancellationToken.IsCancellationRequested && request.OwnerBound))
            return Placeholder(spec, block, ExitCodes.OwnerEnded);
        var ownerEnded = cancellationToken.IsCancellationRequested && request.OwnerBound;
        return Placeholder(spec, ownerEnded ? request.CancellationReason?.Invoke() ?? "owner-ended" : "timeout",
            ownerEnded ? ExitCodes.OwnerEnded : ExitCodes.Timeout);
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

    private static void Publish(SchedulerRequest request) => request.Publish?.Invoke();
}
