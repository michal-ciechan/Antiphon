using Antiphon.HostCleanup.Checkpoints;
using System.Security.Cryptography;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class CheckpointApp
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public sealed class Runtime
    {
        public CheckpointCleanupContext CleanupContext { get; init; } = CheckpointCleanupDefaults.Context;
        public CancellationToken CancellationToken { get; init; }
        public string? RepoRoot { get; init; }
        public string? SourceBranch { get; init; }
        public Func<string, string?> EnvironmentLookup { get; init; } = Environment.GetEnvironmentVariable;
        public HttpMessageHandler? OwnerHandler { get; init; }
        public Func<TimeSpan, CancellationToken, Task>? Delay { get; init; }
        public Func<TimeSpan, CancellationToken, CancellationTokenSource>? OwnerDeadline { get; init; }
        public Func<DateTimeOffset>? OwnerClock { get; init; }
        public TimeSpan? OwnerUncertaintyBudget { get; init; }
        public IDriver? Driver { get; init; }
        public IBuildSlotClient? Slots { get; init; }
        public HttpMessageHandler? SlotHandler { get; init; }
        public Func<DateTimeOffset>? SlotClock { get; init; }
        public Func<TimeSpan, CancellationToken, Task>? SlotDelay { get; init; }
        public Func<Process, DateTime>? SlotStartTimeReader { get; init; }
        public IPlatform? Platform { get; init; }
        public TimeProvider? TimeProvider { get; init; }
        public Func<LaunchRequest, int>? Launch { get; init; }
        public Func<LaunchRequest, LaunchOutcome>? LaunchWithOutcome { get; init; }
        public string? ToolDirectory { get; init; }
        public Action<string, string>? CopyToolOutput { get; init; }
        public Action<string, RunOwnership>? OwnershipWrite { get; init; }
        public Action<string, ProcessIdentity>? ExecutorAckWrite { get; init; }
        public Action<string, string>? LatestWrite { get; init; }
        public ProcessIdentityProbe? ProcessProbe { get; init; }
        public IProcessControl? ProcessControl { get; init; }
        public Func<string, IExecutorLogSink>? LogSinkFactory { get; init; }
        public Func<string, Task<int>>? Wait { get; init; }
        public TextWriter? Output { get; init; }
        public string? GitExecutable { get; init; }
        public Func<string, SourceObservation>? SourceCapture { get; init; }
    }

    public static string ToolSource(Runtime? runtime = null) => runtime?.ToolDirectory ?? (runtime?.CleanupContext ?? CheckpointCleanupDefaults.Context).Io.ImageDirectory;

    public static string ReadLatest(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return "";
        if (lines[0].StartsWith(SafeWrite.Marker + " ", StringComparison.Ordinal))
            return lines.Length > 1 ? lines[^1] : "";
        return lines[0];
    }

    private static void WriteRunText(CheckpointCleanupContext context, string runDirectory, string name, string content)
    {
        var reason = new CheckpointWrite(context).Text(Path.Combine(runDirectory, name), content, SafeWrite.Under(runDirectory));
        if (reason is not null)
            throw new IOException("refused: " + reason);
    }

    public static async Task<int> ExecuteAsync(string runDirectory, CancellationToken cancellationToken, Runtime? runtime = null)
    {
        runtime ??= new Runtime();
        var context = runtime.CleanupContext;
        var write = new CheckpointWrite(context);
        var refusal = write.Preflight(runDirectory, [Path.GetFullPath(runDirectory)], runtime.GitExecutable);
        if (refusal is not null)
        {
            (runtime.Output ?? Console.Error).WriteLine("refused: " + refusal);
            return ExitCodes.Invalid;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteCoreAsync(runDirectory, cancellationToken, runtime).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception ex)
        {
            // The scheduler may return a timeout/owner result after joining canceled
            // drivers. Only the invoking caller token selects the interrupt outcome.
            var canceled = cancellationToken.IsCancellationRequested;
            var exit = canceled ? ExitCodes.Canceled : ex is StatePublicationException ? ExitCodes.Invalid : ExitCodes.ExecutorCrashed;
            var reason = canceled ? "caller-requested"
                : "executor.log or executor failure: " + ex.GetType().Name + ": " + ex.Message
                + (ex.InnerException is null ? "" : " <- " + ex.InnerException.ToString().Replace("\n", " | "));
            var output = runtime.Output ?? Console.Error;
            var statePath = Path.Combine(runDirectory, "state.json");
            var store = new RunStateStore(context);
            var state = store.TryRead(statePath) ?? new RunState { RunId = Path.GetFileName(runDirectory) };
            state.Phase = "done";
            state.ExitCode = exit;
            state.Reason = reason;
            state.EndedAt = context.Time.GetUtcNow();
            var model = new ReportModel
            {
                RunId = state.RunId,
                Commit = state.Source.Start.Commit ?? "",
                StartedAt = state.StartedAt,
                EndedAt = state.EndedAt.Value,
                ExitCode = exit,
                Reason = reason,
                Verdict = "RED",
                Budget = state.Budget,
                Rows = state.Rows.Select(row => new ReportRow { Id = row.Id, State = row.State, ExitCode = row.ExitCode,
                    Slot = row.Slot, SlotReason = row.SlotReason, WaitedSeconds = row.WaitedSeconds,
                    ExecutedSeconds = row.ExecutedSeconds, WallSeconds = row.WallSeconds,
                    TerminalReason = row.TerminalReason, Line = row.Line }).ToList(),
                Source = state.Source,
                Evidence = Path.Combine(runDirectory, "report.md"),
            };
            try
            {
                ReportWriter.WriteFiles(context, runDirectory, model);
                var persisted = store.Write(statePath, state);
                if (persisted is not null)
                    output.WriteLine("refused: " + persisted);
            }
            catch (Exception persistence)
            {
                output.WriteLine("checkpoint terminal persistence failed: " + persistence.Message);
            }
            output.WriteLine(canceled ? "canceled: caller-requested" : "checkpoint executor failed: " + ex.Message);
            return exit;
        }
    }

    private static async Task<int> ExecuteCoreAsync(string runDirectory, CancellationToken cancellationToken, Runtime? runtime)
    {
        runtime ??= new Runtime();
        var context = runtime.CleanupContext;
        var write = new CheckpointWrite(context);
        // Checked entry custody precedes request, log, owner, and row I/O.
        (runtime.ExecutorAckWrite ?? ((run, identity) => ExecutorOwnershipStore.Write(run, identity, context)))(runDirectory, (runtime.ProcessProbe ?? new ProcessIdentityProbe(context)).Current());
        var request = ReceiptJson.Read<RunRequest>(context, Path.Combine(runDirectory, "request.json"), Json, cancellationToken)
            ?? throw new ManifestValidationException("request", "request.json is empty");
        var repo = request.RepoRoot;
        var manifest = ManifestLoader.LoadFile(Path.Combine(runDirectory, "manifest.resolved.yaml"), repo, context: context, cancellationToken: cancellationToken);
        var source = request.Source;
        var sourceGuard = new SourceRunGuard(repo, source, runtime.SourceCapture);
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
            WriteRunText(context, runDirectory, "host.txt", HostSnapshot.Capture(BuildSlotClient.DefaultEndpoint(OperatingSystem.IsWindows())));
            WriteRunText(context, runDirectory, "git.txt", GitSnapshot.Capture(repo));
        }
        var store = new RunStateStore(context);
        var state = new RunState
        {
            RunId = Path.GetFileName(runDirectory),
            Phase = "running",
            ExecutorPid = context.Io.CurrentProcessId,
            StartedAt = context.Time.GetUtcNow(),
            Source = source,
        };
        var statePath = Path.Combine(runDirectory, "state.json");
        void Publish()
        {
            try
            {
                var refusal = store.Write(statePath, state);
                if (refusal is not null)
                    throw new IOException("refused: " + refusal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new StatePublicationException(ex); }
        }

        Publish();
        IBuildSlotClient slots = runtime.Slots ?? (request.Slots == "off"
            ? new FixedSlotClient("off")
            : new BuildSlotClient(runtime.SlotHandler ?? new HttpClientHandler(), BuildSlotClient.DefaultEndpoint(OperatingSystem.IsWindows()),
                wait: TimeSpan.FromMinutes(manifest.Build.SlotWaitMinutes), clock: runtime.SlotClock, delay: runtime.SlotDelay, log: Note,
                holders: new ProcessLeaseHolderSource(runtime.SlotStartTimeReader),
                sensitiveToken: runtime.EnvironmentLookup("ANTIPHON_TASK_TOKEN"), timeProvider: runtime.TimeProvider));
        var platform = runtime.Platform ?? new RuntimePlatform();
        var width = request.Parallel is > 0 ? request.Parallel.Value : manifest.EffectiveMaxRows(platform.IsWindows);
        if (request.Serial)
            width = 1;
        var totalMinutes = request.TotalTimeoutMinutes
            ?? RowTimeout.DeriveTotalMinutes(selected.Select(row => row.EstimatedMinutes ?? 0), null);
        state.TotalTimeoutAt = state.StartedAt.AddMinutes(totalMinutes);
        Note($"execute rows={selected.Count} width={width} total={totalMinutes}m");
        var driver = runtime.Driver ?? new ProcessDriver();
        var executionLimit = TimeSpan.FromMinutes(totalMinutes);
        var wallLimit = request.HardWallTimeoutMinutes ?? manifest.Timeouts.HardWallMinutes;
        var admissionLimit = TimeSpan.FromMinutes(manifest.Build.SlotWaitMinutes);
        await using var budget = new RunBudgetClock(executionLimit,
            wallLimit is int wall ? TimeSpan.FromMinutes(wall) : executionLimit + admissionLimit, runtime.TimeProvider);
        state.HardWallTimeoutAt = budget.StartedAt + budget.HardWallLimit;
        var scheduler = new RunScheduler(driver, platform, runtime.TimeProvider ?? TimeProvider.System);
        SchedulerResult result;
        StatePublicationException? publicationFailure = null;
        using var watchCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var workCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Settled);
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
            var admitted = entryAdmitted && await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false)
                && sourceGuard.Observe();
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
                Budget = budget,
                Publish = Publish,
                BeforeLaunch = BeforeLaunch,
                CancellationReason = () => owner.Reason ?? "owner-ended",
                AdmissionBlock = () => owner.Reason == "owner-unverified" ? owner.Reason : null,
                OwnerBound = owner.Bound,
                SourceGuard = sourceGuard,
                ExpectedSourceSha = request.ExpectedSourceSha,
                TimingRoot = Environment.GetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT"),
            }, workCancel.Token).ConfigureAwait(false);
            }
            else if (sourceGuard.Reason is not null)
            {
                state.Reason = sourceGuard.Reason;
                state.Rows = selected.Select(row => new RowProgress { Id = row.Id, State = state.Reason, ExitCode = ExitCodes.Invalid }).ToList();
                result = new SchedulerResult
                {
                    ExitCode = ExitCodes.Invalid,
                    Rows = selected.Select(row => new RowRunResult { Id = row.Id, State = state.Reason, ExitCode = ExitCodes.Invalid, Source = sourceGuard.Copy("unknown") }).ToList(),
                    State = state,
                };
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
        catch (StatePublicationException ex)
        {
            publicationFailure = ex;
            state.Reason = ex.Message;
            result = ex.PartialResult ?? new SchedulerResult { ExitCode = ExitCodes.Invalid, State = state };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("scheduler crashed", ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (owner.Ended.IsCancellationRequested)
        {
            state.Reason = owner.Reason;
            result = new SchedulerResult { ExitCode = ExitCodes.OwnerEnded, Rows = result.Rows, State = state };
        }

        sourceGuard.Observe();
        if (sourceGuard.Reason is not null && result.ExitCode != ExitCodes.OwnerEnded)
        {
            state.Reason = sourceGuard.Reason;
            result = new SchedulerResult { ExitCode = ExitCodes.Invalid, Rows = result.Rows, State = state };
        }
        // The report heading describes the build binding shared by its rows. Command rows
        // have no build; test rows must all carry a verified binding to certify this run.
        var testRows = result.Rows.Where(row =>
            manifest.Checkpoints.FirstOrDefault(spec => spec.Id == row.Id)?.IsCommand != true).ToList();
        source.BuildSource = testRows.Count == 0 ? "notApplicable"
            : testRows.All(row => row.Source.BuildSource == "verified") ? "verified" : "unknown";
        var model = BuildReport(runDirectory, repo, request, manifest, state, result, context, cancellationToken);
        async Task<int> RetainPublicationFailure(StatePublicationException failure)
        {
            state.ExitCode = model.ExitCode = ExitCodes.Invalid;
            state.Reason = model.Reason = failure.Message;
            model.Verdict = "RED";
            model.CleanedOutputs = false;
            model.OutputPaths = state.CreatedOutputPaths.ToList();
            state.Budget = model.Budget = budget.Snapshot(admissionLimit);
            model.Evidence = Path.Combine(runDirectory, "report.md");
            var output = runtime.Output ?? Console.Error;
            foreach (var line in model.Rows.SelectMany(row => new[] { row.Line }.Concat(row.RerunLines))
                         .OfType<string>().Append(failure.Message))
            {
                output.WriteLine(line);
                try { Note(line); }
                catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException) { }
                catch (ObjectDisposedException) { } // Terminal publication follows log settlement.
            }
            try { ReportWriter.WriteFiles(context, runDirectory, model); }
            catch (Exception persistence) when (persistence is IOException or UnauthorizedAccessException)
            { output.WriteLine("checkpoint terminal persistence failed: " + persistence.Message); }
            try { await logWriter.FlushAsync().ConfigureAwait(false); }
            catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
            { output.WriteLine("checkpoint log persistence failed: " + logFailure.Message); }
            catch (ObjectDisposedException) { }
            try { await logWriter.DisposeAsync().ConfigureAwait(false); }
            catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
            { output.WriteLine("checkpoint log disposal failed: " + logFailure.Message); }
            return ExitCodes.Invalid;
        }
        if (publicationFailure is not null)
            return await RetainPublicationFailure(publicationFailure).ConfigureAwait(false);
        // Keep a nonterminal receipt until Git evidence, cleanup and owner settlement
        // determine the final exit. A failed terminal write must not expose an earlier green.
        state.Phase = "finishing";
        try { Publish(); }
        catch (StatePublicationException ex)
        { return await RetainPublicationFailure(ex).ConfigureAwait(false); }
        if (!owner.Ended.IsCancellationRequested && !string.IsNullOrWhiteSpace(request.Baseline))
        {
            var remaining = budget.RemainingExecution;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            var comparer = new BaselineComparer(driver, slots, remaining, BeforeLaunch, budget, admissionLimit);
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

        var finalizationTime = runtime.TimeProvider ?? TimeProvider.System;
        var finalizationStarted = finalizationTime.GetTimestamp();
        var finalizationPhaseStarted = finalizationStarted;
        void FinalizationPhase(string phase)
        {
            var ended = finalizationTime.GetTimestamp();
            Note(FormattableString.Invariant($"FINALIZATION phase={phase} seconds={finalizationTime.GetElapsedTime(finalizationPhaseStarted, ended).TotalSeconds:F3}"));
            finalizationPhaseStarted = ended;
        }
        state.Budget = model.Budget = budget.Snapshot(admissionLimit);
        model.Evidence = Path.Combine(runDirectory, "report.md");
        var gitAppend = write.Append(Path.Combine(runDirectory, "git.txt"),
            $"\nsource={SourceEvidence.Token(source.Start)} sourceState={source.State} buildSource={source.BuildSource}\n",
            SafeWrite.Under(runDirectory));
        if (gitAppend is not null)
            throw new IOException("refused: " + gitAppend);
        cancellationToken.ThrowIfCancellationRequested();
        ReportWriter.WriteFiles(context, runDirectory, model);
        try
        {
            EvidenceFolder.Write(runDirectory, model, removeToolCopy: false, context: context);
        }
        catch (Exception ex)
        {
            Note("evidence failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        FinalizationPhase("evidence");

        bool OwnedOutputRemains(IReadOnlyCollection<string>? created)
        {
            if (created is null)
                return false;
            foreach (var path in created)
            {
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!OutputCleanup.IsIsolatedOutputId(Path.GetFileName(trimmed)))
                    continue;
                if (context.Io.DirectoryExists(path) || context.Io.FileExists(path))
                    return true;
            }

            return false;
        }

        var selectedBuildIds = selected.Where(row => !row.IsCommand)
            .Select(row => row.Build).ToHashSet(StringComparer.Ordinal);
        model.OutputPaths = manifest.Builds.Where(build => selectedBuildIds.Contains(build.Id))
            .SelectMany(build => new CheckpointOutputCleanup(context).MatchingIsolatedDirectories(repo, build.Id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        FinalizationPhase("output-discovery");
        try
        {
            if (owner.Bound && !await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false))
                throw new OperationCanceledException(owner.Reason);
            if (!request.KeepOutputs)
            {
                var clean = new CheckpointOutputCleanup(context).CleanOwnedOutputs(repo, manifest.Builds.Select(build => build.Id).ToList(), model.ExitCode,
                    request.CleanOnRed, dryRun: false, beforeDelete: owner.Bound ? () => owner.Ended.ThrowIfCancellationRequested() : null,
                    createdPaths: state.CreatedOutputPaths);
                if (clean.KeptBecauseRed)
                    model.CleanedOutputs = false;
                else if (clean.Refusals.Count > 0)
                {
                    model.CleanedOutputs = false;
                    model.CleanupRefusals = clean.Refusals;
                    if (model.ExitCode == 0)
                    {
                        model.ExitCode = ExitCodes.Invalid;
                        model.Verdict = "RED";
                        model.Reason = "refused: " + clean.Refusals[0];
                    }
                }
                else if (OwnedOutputRemains(state.CreatedOutputPaths))
                {
                    model.CleanedOutputs = false;
                    model.CleanupRefusals.Add("still-present");
                    if (model.ExitCode == 0)
                    {
                        model.ExitCode = ExitCodes.Invalid;
                        model.Verdict = "RED";
                        model.Reason = "refused: still-present";
                    }
                }
                else
                    model.CleanedOutputs = model.ExitCode == 0 || request.CleanOnRed;
            }
        }
        catch (OperationCanceledException) when (owner.Ended.IsCancellationRequested)
        {
            state.Reason = owner.Reason;
            model.ExitCode = ExitCodes.OwnerEnded;
            model.Verdict = "RED";
            model.Reason = owner.Reason;
            ReportWriter.WriteFiles(context, runDirectory, model);
        }
        catch (Exception ex)
        {
            Note("cleanup failed: " + ex.GetType().Name + ": " + ex.Message);
            model.CleanedOutputs = false;
            if (model.CleanupRefusals.Count == 0)
                model.CleanupRefusals.Add("cleanup-failed");
            if (model.ExitCode == 0)
            {
                model.ExitCode = ExitCodes.Invalid;
                model.Verdict = "RED";
                model.Reason = "refused: " + model.CleanupRefusals[0];
            }
        }
        if (owner.Bound && !owner.Ended.IsCancellationRequested)
            await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
        if (!request.KeepOutputs)
        {
            var remaining = model.OutputPaths.Where(path => context.Io.DirectoryExists(path) || context.Io.FileExists(path)).ToList();
            if (remaining.Count > 0)
            {
                model.CleanedOutputs = false;
                if (model.CleanupRefusals.Count == 0)
                    model.CleanupRefusals.Add("still-present");
                if (model.ExitCode == 0)
                {
                    model.ExitCode = ExitCodes.Invalid;
                    model.Verdict = "RED";
                    model.Reason = "refused: " + model.CleanupRefusals[0];
                }
            }
        }
        if (owner.Ended.IsCancellationRequested && model.ExitCode != ExitCodes.OwnerEnded)
        {
            state.Reason = owner.Reason;
            model.ExitCode = ExitCodes.OwnerEnded;
            model.Verdict = "RED";
            model.Reason = owner.Reason;
            ReportWriter.WriteFiles(context, runDirectory, model);
        }
        FinalizationPhase("output-cleanup");
        state.Budget = model.Budget = budget.Snapshot(admissionLimit);
        model.Budget.FinalizationSeconds = finalizationTime.GetElapsedTime(finalizationStarted).TotalSeconds;
        model.EndedAt = context.Time.GetUtcNow();
        model.WallSeconds = (model.EndedAt - model.StartedAt).TotalSeconds;
        if (budget.Reason is not null && model.ExitCode == ExitCodes.Green)
        {
            model.ExitCode = ExitCodes.Timeout;
            model.Verdict = "RED";
            model.Reason = budget.Reason;
        }
        ReportWriter.WriteFiles(context, runDirectory, model);
        FinalizationPhase("report-publication");
        watchCancel.Cancel();
        await watcher.ConfigureAwait(false);
        if (owner.Bound && !owner.Ended.IsCancellationRequested)
            await owner.EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
        if (owner.Ended.IsCancellationRequested && model.ExitCode != ExitCodes.OwnerEnded)
        {
            state.Reason = owner.Reason;
            model.ExitCode = ExitCodes.OwnerEnded;
            model.Verdict = "RED";
            model.Reason = owner.Reason;
            ReportWriter.WriteFiles(context, runDirectory, model);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Note("done exit=" + model.ExitCode);
        await logWriter.FlushAsync().ConfigureAwait(false);
        await logWriter.DisposeAsync().ConfigureAwait(false);
        state.Reason = model.Reason;
        try { Finish(state, model.ExitCode, Publish, () => { }, _ => { }, context); }
        catch (StatePublicationException ex)
        { return await RetainPublicationFailure(ex).ConfigureAwait(false); }
        return model.ExitCode;
        }
        finally
        {
            watchCancel.Cancel();
            await watcher.ConfigureAwait(false);
        }
    }

    public readonly record struct StartResult(int ExitCode, string RunDirectory);

    public static string CreateRun(CheckpointManifest manifest, RunRequest request, string repo, Runtime? runtime = null)
    {
        runtime ??= new Runtime();
        var context = runtime.CleanupContext;
        var write = new CheckpointWrite(context);
        ManifestValidator.Validate(manifest, repo);
        var selected = manifest.Checkpoints.Where(row => request.Rows.Count == 0 || request.Rows.Contains(row.Id)).ToList();
        if (selected.Count == 0)
            throw new ManifestValidationException("rows", "no checkpoints selected");
        var resultsRoot = Path.GetFullPath(Path.IsPathRooted(manifest.ResultsRoot) ? manifest.ResultsRoot : Path.Combine(repo, manifest.ResultsRoot));
        var resultsOwnership = SafeWrite.Under(resultsRoot, gitExecutable: runtime.GitExecutable);
        SafeWrite.Require(write.CreateDirectory(resultsRoot, resultsOwnership));
        // Runs do not reclaim any older repository paths.
        var runId = RepoPaths.RunId(context);
        var runDirectory = Path.Combine(resultsRoot, runId);
        SafeWrite.Require(write.CreateFreshDirectory(runDirectory, resultsOwnership));
        try
        {
            (runtime.OwnershipWrite ?? ((run, record) => RunOwnershipStore.Write(run, record, context)))(runDirectory, new RunOwnership
            {
                RunId = runId,
                RunDirectory = Path.GetFullPath(runDirectory),
                Starter = (runtime.ProcessProbe ?? new ProcessIdentityProbe(context)).Current(),
            });
            TestRootGuard.RegisterRun(runDirectory, context);
            WriteRunText(context, runDirectory, "manifest.resolved.yaml", ManifestLoader.ToYaml(manifest));
            request.RepoRoot = repo;
            request.Rows = selected.Select(row => row.Id).ToList();
            var capture = runtime.SourceCapture ?? new SourceSnapshot().Capture;
            request.Source = new SourceEvidence { Start = capture(repo) };
            if (request.ExpectedSourceSha is not null)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(request.ExpectedSourceSha, "^(?:[0-9a-f]{40}|[0-9a-f]{64})$"))
                    throw new ManifestValidationException("expected-source-sha", "expected source SHA must be a normalized full object ID");
                if (request.Source.Start.CaptureStatus != "known" || request.Source.Start.DirtyFiles != 0 ||
                    request.Source.Start.Commit != request.ExpectedSourceSha)
                    throw new ManifestValidationException("source", "source_mismatch or source_dirty");
            }
            if (string.IsNullOrWhiteSpace(request.Commit))
                request.Commit = request.Source.Start.Commit ?? GitSnapshot.Run(repo, "rev-parse", "HEAD");
            if (string.IsNullOrWhiteSpace(request.Branch))
                request.Branch = runtime.SourceBranch ?? GitSnapshot.Run(repo, "rev-parse", "--abbrev-ref", "HEAD");
            WriteRunText(context, runDirectory, "request.json", JsonSerializer.Serialize(request, Json));
            return runDirectory;
        }
        catch
        {
            if (ContainedCleanup.SafeAncestors(runDirectory, context) && ContainedCleanup.SafeTree(runDirectory, 10000, context))
                _ = runDirectory; // retain partial run evidence
            throw;
        }
    }

    public static StartResult Start(CheckpointManifest manifest, RunRequest request, string repo, TextWriter output) =>
        StartAsync(manifest, request, repo, output).GetAwaiter().GetResult();

    public static async Task<StartResult> StartAsync(CheckpointManifest manifest, RunRequest request, string repo, TextWriter output, Runtime? runtime = null)
    {
        runtime ??= new Runtime();
        var context = runtime.CleanupContext;
        var write = new CheckpointWrite(context);
        using var owner = new TaskOwnerGuard(runtime.EnvironmentLookup, runtime.OwnerHandler, runtime.Delay,
            deadline: runtime.OwnerDeadline, now: runtime.OwnerClock,
            uncertaintyBudget: runtime.OwnerUncertaintyBudget);
        if (!await owner.EnsureLiveAsync(runtime.CancellationToken).ConfigureAwait(false))
        {
            output.WriteLine("CHECKPOINT owner " + owner.Reason);
            return new StartResult(ExitCodes.OwnerEnded, "");
        }

        var resultsPreview = Path.GetFullPath(Path.IsPathRooted(manifest.ResultsRoot) ? manifest.ResultsRoot : Path.Combine(repo, manifest.ResultsRoot));
        var writeRoots = new List<string> { resultsPreview };
        foreach (var build in manifest.Builds)
        {
            var closure = ProjectWriteRoots.TryCollect(repo, build.Project, build.OutputPath ?? "", out var directories);
            if (closure is not null)
            {
                output.WriteLine("refused: " + closure);
                return new StartResult(ExitCodes.Invalid, "");
            }

            writeRoots.AddRange(directories);
        }

        var preflight = write.Preflight(repo, writeRoots, runtime.GitExecutable);
        if (preflight is not null)
        {
            output.WriteLine("refused: " + preflight);
            return new StartResult(ExitCodes.Invalid, "");
        }

        request.OwnerTaskId = owner.TaskId;
        request.OwnerSessionId = owner.SessionId;
        var runDirectory = CreateRun(manifest, request, repo, runtime);
        var resultsRoot = Path.GetDirectoryName(runDirectory)!;
        var runId = Path.GetFileName(runDirectory);
        var ownership = RunOwnershipStore.Read(runDirectory, context, runtime.CancellationToken)!;
        try
        {
            var stateRefusal = new RunStateStore(context).Write(Path.Combine(runDirectory, "state.json"),
                new RunState { RunId = runId, Phase = "starting", StartedAt = context.Time.GetUtcNow() });
            if (stateRefusal is not null)
                throw new InvalidOperationException("refused: " + stateRefusal);
            (runtime.CopyToolOutput ?? ((source, destination) => ShadowCopy.CopyToolOutput(source, destination, context)))(ToolSource(runtime), Path.Combine(runDirectory, "tool"));
            ownership.ToolCopy = ToolCopyAuthority.Seal(runDirectory, ownership, context);
        }
        catch
        {
            // This starter has not published launch intent; its partial run evidence remains in place.
            if (ContainedCleanup.SafeTree(runDirectory, 10000, context)) _ = runDirectory; // retain partial run evidence
            throw;
        }
        var dll = Path.Combine(runDirectory, "tool", "Antiphon.Checkpoints.dll");
        var launch = new LaunchRequest("dotnet", [dll, "execute", "--run", runDirectory], repo);
        ownership.Phase = "launch-attempted";
        try { (runtime.OwnershipWrite ?? ((run, record) => RunOwnershipStore.Write(run, record, context)))(runDirectory, ownership); }
        catch
        {
            // Launch has not been invoked. This starter's partial run evidence remains in place.
            if (ContainedCleanup.SafeAncestors(runDirectory, context) && ContainedCleanup.SafeTree(runDirectory, 10000, context))
                _ = runDirectory; // retain partial run evidence
            throw;
        }
        LaunchOutcome outcome;
        try
        {
            outcome = runtime.LaunchWithOutcome?.Invoke(launch)
                ?? new LaunchOutcome(LaunchKind.Started,
                    runtime.Launch?.Invoke(launch) ?? new DetachedLauncher(new RuntimePlatform()).Start(launch));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // The native API refused to create a process; no executor can use this image.
            RecordNotStarted();
            if (ContainedCleanup.SafeAncestors(runDirectory, context) && ContainedCleanup.SafeTree(runDirectory, 10000, context))
                _ = runDirectory; // retain partial run evidence
            throw;
        }
        if (outcome.Kind == LaunchKind.NotStarted)
        {
            RecordNotStarted();
            if (ContainedCleanup.SafeAncestors(runDirectory, context) && ContainedCleanup.SafeTree(runDirectory, 10000, context))
                _ = runDirectory; // retain partial run evidence
            output.WriteLine("CHECKPOINT native launch refused run=" + runId);
            return new StartResult(ExitCodes.Invalid, runDirectory);
        }
        if (outcome.Kind == LaunchKind.Unknown)
        {
            output.WriteLine("CHECKPOINT launch-outcome-unknown run=" + runId + " path=" + runDirectory);
            return new StartResult(ExitCodes.Invalid, runDirectory);
        }
        var pid = outcome.Pid;
        ProcessIdentity? launched = outcome.Identity;
        if (launched is null)
        {
            try { launched = (runtime.ProcessProbe ?? new ProcessIdentityProbe(context)).Capture(pid); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }
        if (launched is not null)
        {
            ownership.Launched = launched;
            ownership.Phase = "launched";
            try { (runtime.OwnershipWrite ?? ((run, record) => RunOwnershipStore.Write(run, record, context)))(runDirectory, ownership); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.WriteLine("CHECKPOINT launch custody write failed run=" + runId + " reason=" + ex.GetType().Name);
                return new StartResult(ExitCodes.Invalid, runDirectory);
            }
        }
        // No shared latest file is written; the run id identifies fresh evidence.
        // An executor can already be running or done; the starter does not publish progress.
        output.WriteLine($"RUN {runId} started rows={request.Rows.Count} executor={pid}");
        return new StartResult(ExitCodes.Green, runDirectory);

        void RecordNotStarted()
        {
            ownership.Phase = "not-started";
            ownership.Launched = null;
            try { (runtime.OwnershipWrite ?? ((run, record) => RunOwnershipStore.Write(run, record, context)))(runDirectory, ownership); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Failed publication keeps the old ambiguous journal and therefore retains the copy.
                output.WriteLine("CHECKPOINT no-launch custody write failed run=" + runId + " reason=" + ex.GetType().Name);
            }
        }
    }

    public static void Finish(RunState state, int exitCode, Action publish, Action cleanup, Action<string> note, CheckpointCleanupContext? context = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        state.Phase = "done";
        state.EndedAt = context.Time.GetUtcNow();
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
        SchedulerResult result, CheckpointCleanupContext? context = null, CancellationToken cancellationToken = default)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var rows = new List<ReportRow>();
        foreach (var row in result.Rows)
        {
            var spec = manifest.Checkpoints.FirstOrDefault(item => item.Id == (row.Id.Length > 0 ? row.Id : IdFromLine(row.Line)));
            rows.Add(ToReportRow(row, spec));
        }

        var chargedBuilds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.Where(item => item.Timings is not null && item.Build is not null))
        {
            var build = state.Builds.FirstOrDefault(item => item.Id == row.Build);
            var first = chargedBuilds.Add(row.Build!);
            row.Timings!.ProducingBuild = row.Build;
            row.Timings.BuildSeconds = first ? build?.Seconds : 0;
            row.Timings.BuildSlotWaitSeconds = first ? build?.WaitedSeconds : 0;
            row.Timings.RowSlotWaitSeconds = row.WaitedSeconds;
            row.Timings.SlotWaitSeconds = row.Timings.BuildSlotWaitSeconds + row.Timings.RowSlotWaitSeconds;
        }

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Line))
                continue;
            row.Line = CheckpointLine.Format(new CheckpointLineModel
            {
                Name = row.Id,
                Commit = request.Commit,
                Source = row.Source,
                Build = row.State == "build-failed" ? "failed" : "n/a",
                Filter = row.Filter ?? row.Command ?? "",
                Executed = row.State is "slot-refused" or "slot-timeout" ? "0" : "n/a",
                Passed = row.State is "slot-refused" or "slot-timeout" ? "0" : "n/a",
                Failed = row.State is "slot-refused" or "slot-timeout" ? "0" : "n/a",
                Skipped = row.State is "slot-refused" or "slot-timeout" ? "0" : "n/a",
                Trx = "n/a",
                Slot = row.Slot ?? "skipped",
                SlotReason = row.SlotReason,
                WaitedSeconds = row.WaitedSeconds,
                Command = row.Command is not null,
                Timeout = row.State == "timeout" ? "total" : null,
            });
        }

        var ended = context.Time.GetUtcNow();
        var model = new ReportModel
        {
            SchemaVersion = rows.Any(row => row.Repeat is not null) ? 3 : 2,
            RunId = state.RunId,
            ManifestPath = Path.Combine(runDirectory, "manifest.resolved.yaml"),
            ManifestHash = Hash(ReceiptFile.ReadText(context, Path.Combine(runDirectory, "manifest.resolved.yaml"), cancellationToken)),
            Commit = request.Commit,
            Source = state.Source,
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
            Budget = state.Budget,
            Verdict = result.ExitCode == 0 ? "GREEN" : "RED",
            Builds = state.Builds.Select(build => new ReportBuild
            {
                Id = build.Id,
                Project = manifest.Builds.FirstOrDefault(item => item.Id == build.Id)?.Project ?? "",
                State = build.State,
                Seconds = build.Seconds,
                Slot = build.Slot,
                SlotReason = build.SlotReason,
                WaitedSeconds = build.WaitedSeconds,
                DeadlineMinutes = build.DeadlineMinutes,
                ExecutedSeconds = build.ExecutedSeconds,
                WallSeconds = build.WallSeconds,
                TerminalReason = build.TerminalReason,
                Source = build.Source,
            }).ToList(),
            Rows = rows,
            Unlisted = [],
            // Configured builds can time out before creating any output. Describe the
            // actual inventory; Builds/Rows retain the nonexecuted admission result.
            OutputPaths = state.CreatedOutputPaths.ToList(),
            OutputNames = manifest.Builds
                .Where(build => state.CreatedOutputPaths.Any(path =>
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) == build.Id))
                .Select(build => build.Id + "/").ToList(),
            MaxConcurrentRows = state.MaxConcurrentRows,
            MaxConcurrentBuilds = state.MaxConcurrentBuilds,
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
            Source = row.Source,
            ExitCode = row.ExitCode,
            Slot = row.Slot.Length == 0 ? null : row.Slot,
            SlotReason = row.SlotReason,
            WaitedSeconds = row.WaitedSeconds,
            Executed = row.Trx?.Executed ?? (row.State is "slot-refused" or "slot-timeout" ? 0 : null),
            Passed = row.Trx?.Passed ?? (row.State is "slot-refused" or "slot-timeout" ? 0 : null),
            Failed = row.Trx?.Failed ?? (row.State is "slot-refused" or "slot-timeout" ? 0 : null),
            Skipped = row.Trx?.Skipped ?? (row.State is "slot-refused" or "slot-timeout" ? 0 : null),
            Reruns = row.Reruns,
            Repeat = row.Repeat,
            Timings = row.Timings,
            Trx = row.TrxPath,
            Seconds = row.Seconds,
            ExecutedSeconds = row.ExecutedSeconds,
            WallSeconds = row.WallSeconds,
            TerminalReason = row.TerminalReason,
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
