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
        request.State.Builds = buildStates.Values.ToList();
        request.State.Rows = request.Rows.Select(row => new RowProgress { Id = row.Id, State = "queued" }).ToList();
        Publish(request);

        var buildTasks = request.Manifest.Builds
            .Where(build => request.Rows.Any(row => row.Build == build.Id && !row.IsCommand))
            .Select(build => BuildOneAsync(request, build, buildStates[build.Id], session, total.Token))
            .ToList();

        var pending = request.Rows.ToList();
        var reportedBuilds = new HashSet<string>(StringComparer.Ordinal);
        var running = new List<(CheckpointSpec Spec, Task<RowRunResult> Task, RowProgress Progress)>();
        var finished = new List<RowRunResult>();

        while (pending.Count > 0 || running.Count > 0)
        {
            var totalTimedOut = total.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            if (totalTimedOut)
            {
                foreach (var row in pending.ToList())
                {
                    pending.Remove(row);
                    Mark(request, row.Id, "skipped", ExitCodes.Timeout);
                    finished.Add(Placeholder(row, "skipped", ExitCodes.Timeout));
                }

                _driver.Kill(entireProcessTree: true);
                Publish(request);
                break;
            }

            foreach (var row in pending.Where(row => BuildFailed(row, buildStates)).ToList())
            {
                pending.Remove(row);
                Mark(request, row.Id, "build-failed", ExitCodes.Invalid);
                finished.Add(Placeholder(row, "build-failed", ExitCodes.Invalid));
            }

            var exclusiveRunning = running.Any(item => IsExclusive(item.Spec, request));
            while (Pick(pending, running.Count, request, buildStates, exclusiveRunning) is CheckpointSpec next)
            {
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

            if (running.Count == 0)
            {
                var waiting = pending.Any(row => !row.IsCommand && buildStates.TryGetValue(row.Build ?? "", out var build) && build.State is "pending" or "building");
                var incomplete = buildTasks.Where(task => !task.IsCompleted).ToList();
                if (!waiting || incomplete.Count == 0)
                    break;
                await Task.WhenAny(incomplete).ConfigureAwait(false);
                continue;
            }

            var completed = await Task.WhenAny(running.Select(item => item.Task)).ConfigureAwait(false);
            var index = running.FindIndex(item => item.Task == completed);
            var (spec, task, progress) = running[index];
            running.RemoveAt(index);
            RowRunResult result;
            try
            {
                result = await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = Placeholder(spec, "timeout", ExitCodes.Timeout);
            }

            progress.State = result.State;
            progress.ExitCode = result.ExitCode;
            progress.Line = result.Line;
            progress.Seconds = result.Seconds > 0 ? result.Seconds : Elapsed(progress.StartedAt);
            finished.Add(result);
            Publish(request);
        }

        foreach (var (spec, task, progress) in running)
        {
            progress.State = "timeout";
            progress.ExitCode = ExitCodes.Timeout;
            finished.Add(Placeholder(spec, "timeout", ExitCodes.Timeout));
            _ = task.ContinueWith(static _ => { }, TaskScheduler.Default);
        }

        try
        {
            await Task.WhenAll(buildTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        var exit = ExitCodes.FromRowStates(finished.Select(row => row.ExitCode));
        request.State.ExitCode = exit;
        Publish(request);
        return new SchedulerResult { ExitCode = exit, Rows = finished, State = request.State };
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
        Publish(request);
        try
        {
            await using var lease = await request.Slots.AcquireAsync(session, "build:" + build.Id, cancellationToken).ConfigureAwait(false);
            progress.Slot = lease.State;
            progress.WaitedSeconds = lease.WaitedSeconds;
            if (lease.ExitCode == ExitCodes.SlotTimeout)
            {
                progress.State = "failed";
                Publish(request);
                return;
            }

            var properties = BuildStep.PropertyArguments(request.Manifest.Build.Properties, _platform.IsWindows);
            var cpu = lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4;
            var args = BuildStep.BuildArguments(build.Project, build.OutputPath, properties, cpu);
            var log = Path.Combine(request.RunDirectory, "builds", build.Id, "build.log");
            var started = DateTimeOffset.UtcNow;
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
        if (lease.ExitCode == ExitCodes.SlotTimeout)
        {
            return new RowRunResult
            {
                Id = spec.Id,
                ExitCode = ExitCodes.SlotTimeout,
                State = "slot-timeout",
                BuildState = "failed",
                Line = CheckpointLine.Format(new CheckpointLineModel
                {
                    Name = spec.Id,
                    Commit = request.Commit,
                    Build = "failed",
                    Filter = spec.Filter ?? spec.Command ?? "",
                    Slot = "timeout",
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
            WaitedSeconds = lease.WaitedSeconds,
            MaxCpuCount = lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4,
            Deadline = TimeSpan.FromMinutes(minutes),
        }, TextWriter.Null, cancellationToken).ConfigureAwait(false);
        progress.LastOutputAt = DateTimeOffset.UtcNow;
        result.Id = spec.Id;
        result.Seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
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
        bool exclusiveRunning)
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
                return running == 0 ? row : null;
            return running < Math.Max(1, request.Width) ? row : null;
        }

        return null;
    }

    private static bool BuildFailed(CheckpointSpec row, Dictionary<string, BuildProgress> builds) =>
        !row.IsCommand && builds.TryGetValue(row.Build ?? "", out var build) && build.State == "failed";

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
