using System.Security.Cryptography;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class CheckpointApp
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task<int> ExecuteAsync(string runDirectory, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<RunRequest>(File.ReadAllText(Path.Combine(runDirectory, "request.json")), Json)
            ?? throw new ManifestValidationException("request", "request.json is empty");
        var repo = request.RepoRoot;
        var manifest = ManifestLoader.LoadFile(Path.Combine(runDirectory, "manifest.resolved.yaml"), repo);
        var selected = manifest.Checkpoints.Where(row => request.Rows.Count == 0 || request.Rows.Contains(row.Id)).ToList();
        File.WriteAllText(Path.Combine(runDirectory, "host.txt"), HostSnapshot.Capture(BuildSlotClient.DefaultEndpoint(OperatingSystem.IsWindows())));
        File.WriteAllText(Path.Combine(runDirectory, "git.txt"), GitSnapshot.Capture(repo));
        var log = Path.Combine(runDirectory, "executor.log");
        void Note(string line) => File.AppendAllText(log, DateTimeOffset.UtcNow.ToString("o") + " " + line + "\n");

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
            catch (IOException) { /* the next transition retries */ }
        }

        Publish();
        IBuildSlotClient slots = request.Slots == "off"
            ? new FixedSlotClient("off")
            : new BuildSlotClient(new HttpClientHandler(), BuildSlotClient.DefaultEndpoint(OperatingSystem.IsWindows()), log: Note);
        var platform = new RuntimePlatform();
        var width = request.Parallel is > 0 ? request.Parallel.Value : manifest.EffectiveMaxRows(platform.IsWindows);
        if (request.Serial)
            width = 1;
        var totalMinutes = request.TotalTimeoutMinutes
            ?? RowTimeout.DeriveTotalMinutes(selected.Select(row => row.EstimatedMinutes ?? 0), null);
        state.TotalTimeoutAt = state.StartedAt.AddMinutes(totalMinutes);
        Note($"execute rows={selected.Count} width={width} total={totalMinutes}m");
        var scheduler = new RunScheduler(new ProcessDriver(), platform);
        SchedulerResult result;
        try
        {
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
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Note("executor crashed: " + ex);
            state.Phase = "crashed";
            state.ExitCode = ExitCodes.ExecutorCrashed;
            Publish();
            return ExitCodes.ExecutorCrashed;
        }

        var model = BuildReport(runDirectory, repo, request, manifest, state, result);
        if (!string.IsNullOrWhiteSpace(request.Baseline))
        {
            var comparer = new BaselineComparer(new ProcessDriver());
            await comparer.CompareAsync(repo, runDirectory, request.Baseline, model.Rows.Where(row => row.Failures.Count > 0).ToList(), manifest.Builds, cancellationToken)
                .ConfigureAwait(false);
        }

        model.Evidence = Path.Combine(runDirectory, "report.md");
        ReportWriter.WriteFiles(runDirectory, model);
        var green = model.ExitCode == 0;
        EvidenceFolder.Write(runDirectory, model, removeToolCopy: green && !request.KeepOutputs);
        if (!request.KeepOutputs)
            OutputCleanup.CleanOwnedOutputs(repo, manifest.Builds.Select(build => build.Id).ToList(), model.ExitCode, request.CleanOnRed, dryRun: false);
        state.Phase = "done";
        state.EndedAt = DateTimeOffset.UtcNow;
        state.ExitCode = model.ExitCode;
        Publish();
        Note("done exit=" + model.ExitCode);
        return model.ExitCode;
    }

    public static int Start(CheckpointManifest manifest, RunRequest request, string repo, TextWriter output)
    {
        ManifestValidator.Validate(manifest, repo);
        var selected = manifest.Checkpoints.Where(row => request.Rows.Count == 0 || request.Rows.Contains(row.Id)).ToList();
        if (selected.Count == 0)
            throw new ManifestValidationException("rows", "no checkpoints selected");
        var resultsRoot = Path.GetFullPath(Path.IsPathRooted(manifest.ResultsRoot) ? manifest.ResultsRoot : Path.Combine(repo, manifest.ResultsRoot));
        Directory.CreateDirectory(resultsRoot);
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
        ShadowCopy.CopyToolOutput(AppContext.BaseDirectory, Path.Combine(runDirectory, "tool"));
        var dll = Path.Combine(runDirectory, "tool", "Antiphon.Checkpoints.dll");
        var pid = new DetachedLauncher(new RuntimePlatform()).Start(new LaunchRequest(
            "dotnet",
            [dll, "execute", "--run", runDirectory],
            repo));
        File.WriteAllText(Path.Combine(resultsRoot, "latest"), runId);
        var state = new RunState { RunId = runId, Phase = "starting", ExecutorPid = pid, StartedAt = DateTimeOffset.UtcNow };
        new RunStateStore().Write(Path.Combine(runDirectory, "state.json"), state);
        output.WriteLine($"RUN {runId} started rows={selected.Count} executor={pid}");
        return ExitCodes.Green;
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
