using System.Security.Cryptography;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class CheckpointApp
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public sealed class Runtime
    {
        public Func<string, string?> EnvironmentLookup { get; init; } = Environment.GetEnvironmentVariable;
        public HttpMessageHandler? OwnerHandler { get; init; }
        public Func<TimeSpan, CancellationToken, Task>? Delay { get; init; }
        public Func<TimeSpan, CancellationToken, CancellationTokenSource>? OwnerDeadline { get; init; }
        public Func<DateTimeOffset>? OwnerClock { get; init; }
        public TimeSpan? OwnerUncertaintyBudget { get; init; }
        public IDriver? Driver { get; init; }
        public IBuildSlotClient? Slots { get; init; }
        public IPlatform? Platform { get; init; }
        public Func<LaunchRequest, int>? Launch { get; init; }
        public Func<string, IExecutorLogSink>? LogSinkFactory { get; init; }
        public Func<string, Task<int>>? Wait { get; init; }
    }

    public static async Task<int> ExecuteAsync(string runDirectory, CancellationToken cancellationToken, Runtime? runtime = null)
    {
        try
        {
            return await ExecuteCoreAsync(runDirectory, cancellationToken, runtime).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var reason = "executor.log or executor failure: " + ex.GetType().Name + ": " + ex.Message;
            var statePath = Path.Combine(runDirectory, "state.json");
            var store = new RunStateStore();
            var state = store.TryRead(statePath) ?? new RunState { RunId = Path.GetFileName(runDirectory) };
            state.Phase = "done";
            state.ExitCode = ExitCodes.ExecutorCrashed;
            state.Reason = reason;
            state.EndedAt = DateTimeOffset.UtcNow;
            var model = new ReportModel
            {
                RunId = state.RunId,
                StartedAt = state.StartedAt,
                EndedAt = state.EndedAt.Value,
                ExitCode = ExitCodes.ExecutorCrashed,
                Reason = reason,
                Verdict = "RED",
                Rows = state.Rows.Select(row => new ReportRow { Id = row.Id, State = row.State, ExitCode = row.ExitCode }).ToList(),
                Evidence = Path.Combine(runDirectory, "report.md"),
            };
            try
            {
                ReportWriter.WriteFiles(runDirectory, model);
                store.Write(statePath, state);
            }
            catch (Exception persistence)
            {
                Console.Error.WriteLine("checkpoint terminal persistence failed: " + persistence.GetType().Name);
            }
            Console.Error.WriteLine("checkpoint executor failed: " + ex.GetType().Name);
            return ExitCodes.ExecutorCrashed;
        }
    }

    private static async Task<int> ExecuteCoreAsync(string runDirectory, CancellationToken cancellationToken, Runtime? runtime)
    {
        runtime ??= new Runtime();
        var request = JsonSerializer.Deserialize<RunRequest>(File.ReadAllText(Path.Combine(runDirectory, "request.json")), Json)
            ?? throw new ManifestValidationException("request", "request.json is empty");
        var repo = request.RepoRoot;
        var manifest = ManifestLoader.LoadFile(Path.Combine(runDirectory, "manifest.resolved.yaml"), repo);
        var selected = manifest.Checkpoints.Where(row => request.Rows.Count == 0 || request.Rows.Contains(row.Id)).ToList();
        var log = Path.Combine(runDirectory, "executor.log");
        await using var logWriter = new ExecutorLogWriter(runtime.LogSinkFactory?.Invoke(log) ?? new FileExecutorLogSink(log));
        void Note(string line) => logWriter.Note(line);
        using var owner = new TaskOwnerGuard(runtime.EnvironmentLookup, runtime.OwnerHandler, runtime.Delay,
            request.OwnerTaskId, request.OwnerSessionId, runtime.OwnerDeadline,
            runtime.OwnerClock, runtime.OwnerUncertaintyBudget, Note);
        var entryAdmitted = await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
        if (entryAdmitted)
        {
            File.WriteAllText(Path.Combine(runDirectory, "host.txt"), HostSnapshot.Capture(BuildSlotClient.DefaultEndpoint(OperatingSystem.IsWindows())));
            File.WriteAllText(Path.Combine(runDirectory, "git.txt"), GitSnapshot.Capture(repo));
        }
        var store = new RunStateStore();
        var state = new RunState
        {
            RunId = Path.GetFileName(runDirectory),
            Phase = "running",
            ExecutorPid = Environment.ProcessId,
            StartedAt = DateTimeOffset.UtcNow,
        };
        var statePath = Path.Combine(runDirectory, "state.json");
        void Publish()
        {
            try { store.Write(statePath, state); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        Publish();
        IBuildSlotClient slots = runtime.Slots ?? (request.Slots == "off"
            ? new FixedSlotClient("off")
            : new BuildSlotClient(new HttpClientHandler(), BuildSlotClient.DefaultEndpoint(OperatingSystem.IsWindows()), log: Note, holders: new ProcessLeaseHolderSource()));
        var platform = runtime.Platform ?? new RuntimePlatform();
        var width = request.Parallel is > 0 ? request.Parallel.Value : manifest.EffectiveMaxRows(platform.IsWindows);
        if (request.Serial)
            width = 1;
        var totalMinutes = request.TotalTimeoutMinutes
            ?? RowTimeout.DeriveTotalMinutes(selected.Select(row => row.EstimatedMinutes ?? 0), null);
        state.TotalTimeoutAt = state.StartedAt.AddMinutes(totalMinutes);
        Note($"execute rows={selected.Count} width={width} total={totalMinutes}m");
        var driver = runtime.Driver ?? new ProcessDriver();
        var scheduler = new RunScheduler(driver, platform);
        SchedulerResult result;
        using var watchCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var workCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Ended);
        async Task BeforeLaunch(CancellationToken token)
        {
            if (!await owner.EnsureLiveAsync(token).ConfigureAwait(false))
                throw new OperationCanceledException(owner.Reason, token);
            token.ThrowIfCancellationRequested();
        }
        Task watcher = Task.CompletedTask;
        try
        {
        try
        {
            var admitted = entryAdmitted && await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
            if (admitted)
            {
                watcher = owner.WatchAsync(watchCancel.Token);
                result = await scheduler.RunAsync(new SchedulerRequest
            {
                Manifest = manifest,
                Rows = selected,
                RunDirectory = runDirectory,
                WorkingDirectory = repo,
                State = state,
                Slots = slots,
                Commit = request.Commit,
                KnownFlaky = request.KnownFlaky.Count > 0 ? request.KnownFlaky : manifest.Rerun.KnownFlaky,
                Width = width,
                SerialAll = request.Serial,
                RowTimeoutOverride = request.RowTimeoutMinutes is int row ? TimeSpan.FromMinutes(row) : null,
                TotalTimeout = TimeSpan.FromMinutes(totalMinutes),
                Publish = Publish,
                BeforeLaunch = BeforeLaunch,
                CancellationReason = () => owner.Reason ?? "owner-ended",
                OwnerBound = owner.Bound,
            }, workCancel.Token).ConfigureAwait(false);
            }
            else
            {
                state.Rows = selected.Select(row => new RowProgress { Id = row.Id, State = owner.Reason ?? "owner-unverified", ExitCode = ExitCodes.OwnerEnded }).ToList();
                result = new SchedulerResult
                {
                    ExitCode = ExitCodes.OwnerEnded,
                    Rows = selected.Select(row => new RowRunResult { Id = row.Id, State = owner.Reason ?? "owner-unverified", ExitCode = ExitCodes.OwnerEnded }).ToList(),
                    State = state,
                };
            }
        }
        catch (OperationCanceledException) when (owner.Ended.IsCancellationRequested)
        {
            state.Rows = selected.Select(row => new RowProgress { Id = row.Id, State = owner.Reason ?? "owner-ended", ExitCode = ExitCodes.OwnerEnded }).ToList();
            result = new SchedulerResult
            {
                ExitCode = ExitCodes.OwnerEnded,
                Rows = selected.Select(row => new RowRunResult { Id = row.Id, State = owner.Reason ?? "owner-ended", ExitCode = ExitCodes.OwnerEnded }).ToList(),
                State = state,
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("scheduler crashed", ex);
        }

        if (owner.Ended.IsCancellationRequested)
        {
            state.Reason = owner.Reason;
            result = new SchedulerResult { ExitCode = ExitCodes.OwnerEnded, Rows = result.Rows, State = state };
        }

        var model = BuildReport(runDirectory, repo, request, manifest, state, result);
        if (!owner.Ended.IsCancellationRequested && !string.IsNullOrWhiteSpace(request.Baseline))
        {
            var remaining = state.TotalTimeoutAt is DateTimeOffset deadline
                ? deadline - DateTimeOffset.UtcNow
                : TimeSpan.FromMinutes(15);
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            var comparer = new BaselineComparer(driver, slots, remaining, BeforeLaunch);
            try
            {
                await comparer.CompareAsync(
                    repo,
                    runDirectory,
                    request.Baseline,
                    model.Rows.Where(row => row.Failures.Count > 0).ToList(),
                    manifest.Builds,
                    workCancel.Token).ConfigureAwait(false);
                model.Unlisted.AddRange(comparer.ToolRuns);
            }
            catch (Exception ex)
            {
                Note("baseline failed: " + ex.GetType().Name + ": " + ex.Message);
                model.Unlisted.Add("tool-run: baseline failed");
            }
        }

        if (owner.Bound && !owner.Ended.IsCancellationRequested)
            await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
        if (owner.Ended.IsCancellationRequested)
        {
            state.Reason = owner.Reason;
            model.ExitCode = ExitCodes.OwnerEnded;
            model.Verdict = "RED";
            model.Reason = owner.Reason;
        }

        model.Evidence = Path.Combine(runDirectory, "report.md");
        ReportWriter.WriteFiles(runDirectory, model);
        var green = model.ExitCode == 0;
        try
        {
            EvidenceFolder.Write(runDirectory, model, removeToolCopy: false);
        }
        catch (Exception ex)
        {
            Note("evidence failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            if (owner.Bound && !await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false))
                throw new OperationCanceledException(owner.Reason);
            if (!request.KeepOutputs)
            {
                if (green)
                    EvidenceFolder.TryRemoveToolCopy(runDirectory);
                OutputCleanup.CleanOwnedOutputs(repo, manifest.Builds.Select(build => build.Id).ToList(), model.ExitCode,
                    request.CleanOnRed, dryRun: false, beforeDelete: owner.Bound ? () => owner.Ended.ThrowIfCancellationRequested() : null);
            }
        }
        catch (OperationCanceledException) when (owner.Ended.IsCancellationRequested)
        {
            state.Reason = owner.Reason;
            model.ExitCode = ExitCodes.OwnerEnded;
            model.Verdict = "RED";
            model.Reason = owner.Reason;
            ReportWriter.WriteFiles(runDirectory, model);
        }
        catch (Exception ex)
        {
            Note("cleanup failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        if (owner.Bound && !owner.Ended.IsCancellationRequested)
            await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
        if (owner.Ended.IsCancellationRequested && model.ExitCode != ExitCodes.OwnerEnded)
        {
            state.Reason = owner.Reason;
            model.ExitCode = ExitCodes.OwnerEnded;
            model.Verdict = "RED";
            model.Reason = owner.Reason;
            ReportWriter.WriteFiles(runDirectory, model);
        }
        Note("done exit=" + model.ExitCode);
        await logWriter.FlushAsync().ConfigureAwait(false);
        await logWriter.DisposeAsync().ConfigureAwait(false);
        if (owner.Bound && !owner.Ended.IsCancellationRequested)
            await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
        if (owner.Ended.IsCancellationRequested && model.ExitCode != ExitCodes.OwnerEnded)
        {
            state.Reason = owner.Reason;
            model.ExitCode = ExitCodes.OwnerEnded;
            model.Verdict = "RED";
            model.Reason = owner.Reason;
            ReportWriter.WriteFiles(runDirectory, model);
        }
        Finish(state, model.ExitCode, Publish, () => { }, _ => { });
        return model.ExitCode;
        }
        finally
        {
            watchCancel.Cancel();
            await watcher.ConfigureAwait(false);
        }
    }

    public readonly record struct StartResult(int ExitCode, string RunDirectory);

    public static string CreateRun(CheckpointManifest manifest, RunRequest request, string repo)
    {
        ManifestValidator.Validate(manifest, repo);
        var selected = manifest.Checkpoints.Where(row => request.Rows.Count == 0 || request.Rows.Contains(row.Id)).ToList();
        if (selected.Count == 0)
            throw new ManifestValidationException("rows", "no checkpoints selected");
        var resultsRoot = Path.GetFullPath(Path.IsPathRooted(manifest.ResultsRoot) ? manifest.ResultsRoot : Path.Combine(repo, manifest.ResultsRoot));
        Directory.CreateDirectory(resultsRoot);
        EvidenceFolder.SweepFinishedToolCopies(resultsRoot, new ProcessLiveness());
        var runId = RepoPaths.RunId();
        var runDirectory = Path.Combine(resultsRoot, runId);
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "manifest.resolved.yaml"), ManifestLoader.ToYaml(manifest));
        request.RepoRoot = repo;
        request.Rows = selected.Select(row => row.Id).ToList();
        if (string.IsNullOrWhiteSpace(request.Commit))
            request.Commit = GitSnapshot.Run(repo, "rev-parse", "HEAD");
        if (string.IsNullOrWhiteSpace(request.Branch))
            request.Branch = GitSnapshot.Run(repo, "rev-parse", "--abbrev-ref", "HEAD");
        File.WriteAllText(Path.Combine(runDirectory, "request.json"), JsonSerializer.Serialize(request, Json));
        return runDirectory;
    }

    public static StartResult Start(CheckpointManifest manifest, RunRequest request, string repo, TextWriter output) =>
        StartAsync(manifest, request, repo, output).GetAwaiter().GetResult();

    public static async Task<StartResult> StartAsync(CheckpointManifest manifest, RunRequest request, string repo, TextWriter output, Runtime? runtime = null)
    {
        runtime ??= new Runtime();
        using var owner = new TaskOwnerGuard(runtime.EnvironmentLookup, runtime.OwnerHandler, runtime.Delay,
            deadline: runtime.OwnerDeadline, now: runtime.OwnerClock,
            uncertaintyBudget: runtime.OwnerUncertaintyBudget);
        if (!await owner.EnsureLiveAsync(CancellationToken.None).ConfigureAwait(false))
        {
            output.WriteLine("CHECKPOINT owner " + owner.Reason);
            return new StartResult(ExitCodes.OwnerEnded, "");
        }
        request.OwnerTaskId = owner.TaskId;
        request.OwnerSessionId = owner.SessionId;
        var runDirectory = CreateRun(manifest, request, repo);
        var resultsRoot = Path.GetDirectoryName(runDirectory)!;
        var runId = Path.GetFileName(runDirectory);
        ShadowCopy.CopyToolOutput(AppContext.BaseDirectory, Path.Combine(runDirectory, "tool"));
        var dll = Path.Combine(runDirectory, "tool", "Antiphon.Checkpoints.dll");
        var launch = new LaunchRequest(
            "dotnet",
            [dll, "execute", "--run", runDirectory],
            repo);
        var pid = runtime.Launch?.Invoke(launch) ?? new DetachedLauncher(new RuntimePlatform()).Start(launch);
        File.WriteAllText(Path.Combine(resultsRoot, "latest"), runId);
        var state = new RunState { RunId = runId, Phase = "starting", ExecutorPid = pid, StartedAt = DateTimeOffset.UtcNow };
        new RunStateStore().Write(Path.Combine(runDirectory, "state.json"), state);
        output.WriteLine($"RUN {runId} started rows={request.Rows.Count} executor={pid}");
        return new StartResult(ExitCodes.Green, runDirectory);
    }

    public static void Finish(RunState state, int exitCode, Action publish, Action cleanup, Action<string> note)
    {
        state.Phase = "done";
        state.EndedAt = DateTimeOffset.UtcNow;
        state.ExitCode = exitCode;
        publish();
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            note(ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static ReportModel BuildReport(
        string runDirectory,
        string repo,
        RunRequest request,
        CheckpointManifest manifest,
        RunState state,
        SchedulerResult result)
    {
        var rows = new List<ReportRow>();
        foreach (var row in result.Rows)
        {
            var spec = manifest.Checkpoints.FirstOrDefault(item => item.Id == (row.Id.Length > 0 ? row.Id : IdFromLine(row.Line)));
            rows.Add(ToReportRow(row, spec));
        }

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Line))
                continue;
            row.Line = CheckpointLine.Format(new CheckpointLineModel
            {
                Name = row.Id,
                Commit = request.Commit,
                Build = row.State == "build-failed" ? "failed" : "n/a",
                Filter = row.Filter ?? row.Command ?? "",
                Executed = "n/a",
                Passed = "n/a",
                Failed = "n/a",
                Skipped = "n/a",
                Trx = "n/a",
                Slot = "skipped",
                Command = row.Command is not null,
                Timeout = row.State == "timeout" ? "total" : null,
            });
        }

        var ended = DateTimeOffset.UtcNow;
        var model = new ReportModel
        {
            RunId = state.RunId,
            ManifestPath = Path.Combine(runDirectory, "manifest.resolved.yaml"),
            ManifestHash = Hash(File.ReadAllText(Path.Combine(runDirectory, "manifest.resolved.yaml"))),
            Commit = request.Commit,
            Branch = request.Branch,
            Worktree = repo,
            Host = new ReportHost
            {
                Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim(),
                Cores = Environment.ProcessorCount,
                MemAvailableMb = HostSnapshot.ReadMemAvailableMb(),
                LoadAvg = HostSnapshot.ReadLoad(),
            },
            StartedAt = state.StartedAt,
            EndedAt = ended,
            WallSeconds = (ended - state.StartedAt).TotalSeconds,
            SequentialEquivalentSeconds = rows.Sum(row => row.Seconds),
            ExitCode = result.ExitCode,
            Reason = state.Reason,
            Verdict = result.ExitCode == 0 ? "GREEN" : "RED",
            Builds = state.Builds.Select(build => new ReportBuild
            {
                Id = build.Id,
                Project = manifest.Builds.FirstOrDefault(item => item.Id == build.Id)?.Project ?? "",
                State = build.State,
                Seconds = build.Seconds,
                Slot = build.Slot,
                WaitedSeconds = build.WaitedSeconds,
            }).ToList(),
            Rows = rows,
            Unlisted = [],
            OutputNames = manifest.Builds.Select(build => build.Id + "/").ToList(),
            MaxConcurrentRows = state.MaxConcurrentRows,
        };
        return model;
    }

    private static ReportRow ToReportRow(RowRunResult row, CheckpointSpec? spec)
    {
        var report = new ReportRow
        {
            Id = spec?.Id ?? "",
            Group = spec?.Group,
            Filter = spec?.Filter,
            Command = spec?.Command,
            Build = spec?.Build,
            State = row.State,
            ExitCode = row.ExitCode,
            Executed = row.Trx?.Executed,
            Passed = row.Trx?.Passed,
            Failed = row.Trx?.Failed,
            Skipped = row.Trx?.Skipped,
            Reruns = row.Reruns,
            Trx = row.TrxPath,
            Seconds = row.Seconds,
            Line = row.Line,
            RerunLines = row.RerunLines,
            SlowClasses = row.Trx?.SlowClasses ?? [],
            Failures = row.Trx?.Failures.Select(failure => new ReportFailure
            {
                Name = failure.Name,
                Message = failure.Message,
                StackTrace = failure.StackTrace,
                StdOut = failure.StdOut,
                DurationSeconds = failure.DurationSeconds,
            }).ToList() ?? [],
        };
        if (report.Id.Length == 0)
            report.Id = row.Id.Length > 0 ? row.Id : IdFromLine(row.Line);
        return report;
    }

    private static string IdFromLine(string line)
    {
        if (line.StartsWith("CHECKPOINT ", StringComparison.Ordinal))
        {
            var rest = line["CHECKPOINT ".Length..];
            var space = rest.IndexOf(' ');
            return space > 0 ? rest[..space] : rest;
        }

        return "";
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
