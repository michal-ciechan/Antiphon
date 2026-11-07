using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

internal static class TerminalReceipt
{
    // Called only after the scheduler/drivers (or the stopped executor) are joined.
    // Reconstruct from durable inputs instead of inventing a successful/empty run.
    public static void Publish(string runDirectory, int exit, string reason, CheckpointApp.Runtime runtime,
        DateTimeOffset startedAt)
    {
        var context = runtime.CleanupContext;
        var request = ReceiptJson.Read<RunRequest>(context, Path.Combine(runDirectory, "request.json"), CheckpointApp.Json);
        var manifest = ManifestLoader.LoadFile(Path.Combine(runDirectory, "manifest.resolved.yaml"), request.RepoRoot, context: context);
        var store = new RunStateStore(context);
        var statePath = Path.Combine(runDirectory, "state.json");
        var state = store.TryRead(statePath, out var refusal);
        if (state is null && context.Io.FileExists(statePath))
            throw new IOException("refused: " + refusal);
        state ??= new RunState
        {
            RunId = Path.GetFileName(runDirectory), ToolOwner = SafeWrite.Marker,
            StartedAt = startedAt, Source = request.Source,
        };
        if (state.ToolOwner != SafeWrite.Marker || state.RunId != Path.GetFileName(runDirectory)
            || state.Source.Start.Commit != request.Source.Start.Commit
            || state.Source.Start.Fingerprint != request.Source.Start.Fingerprint)
            throw new IOException("refused: terminal receipt identity/source mismatching");

        var selected = manifest.Checkpoints.Where(row => request.Rows.Count == 0 || request.Rows.Contains(row.Id)).ToList();
        var source = new SourceRunGuard(request.RepoRoot, state.Source, runtime.SourceCapture);
        try { source.Observe(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed observation cannot suppress the failure receipt or certify
            // an earlier end observation as the terminal source state.
            state.Source.End = null;
            state.Source.State = "unknown";
        }
        state.Source.BuildSource = selected.All(row => row.IsCommand) ? "notApplicable"
            : selected.Where(row => !row.IsCommand).All(row => state.Builds.Any(build =>
                build.Id == row.Build && build.Source.BuildSource == "verified")) ? "verified" : "unknown";
        var terminal = exit == ExitCodes.Canceled ? "canceled" : exit == ExitCodes.Timeout ? "timeout" : "executor-failed";
        foreach (var build in state.Builds.Where(build => build.State is "building" or "pending"))
        {
            build.State = build.State == "pending" ? "not-run" : terminal;
            build.TerminalReason = reason;
        }
        var rows = new List<RowRunResult>();
        foreach (var spec in selected)
        {
            var progress = state.Rows.FirstOrDefault(row => row.Id == spec.Id);
            if (progress is null)
            {
                progress = new RowProgress { Id = spec.Id };
                state.Rows.Add(progress);
            }
            if (progress.State is "queued" or "running")
            {
                progress.State = terminal;
                progress.ExitCode = exit;
                progress.TerminalReason = reason;
            }
            var path = Path.Combine(runDirectory, "rows", spec.Id, "run.trx");
            var parsed = spec.IsCommand ? null : TrxReport.Parse(path);
            // An absent or incomplete XML document is unknown, never zero tests.
            var trx = parsed?.Ok == true ? parsed : null;
            rows.Add(new RowRunResult
            {
                Id = spec.Id, State = progress.State, ExitCode = progress.ExitCode,
                Source = source.Copy(spec.IsCommand ? "notApplicable"
                    : state.Builds.FirstOrDefault(build => build.Id == spec.Build)?.Source.BuildSource ?? "unknown"),
                Slot = progress.Slot, SlotReason = progress.SlotReason, WaitedSeconds = progress.WaitedSeconds,
                Seconds = progress.Seconds, ExecutedSeconds = progress.ExecutedSeconds, WallSeconds = progress.WallSeconds,
                TerminalReason = progress.TerminalReason, Line = progress.Line ?? "",
                Repeat = progress.Repeat, Timings = progress.Timings,
                Trx = trx, TrxPath = trx is null ? null : path,
            });
        }
        state.Reason = reason;
        var model = CheckpointApp.BuildReport(runDirectory, request.RepoRoot, request, manifest, state,
            new SchedulerResult { ExitCode = exit, Rows = rows, State = state }, context);
        model.Evidence = Path.Combine(runDirectory, "report.md");
        state.Phase = "done";
        state.ExitCode = exit;
        state.EndedAt = model.EndedAt;
        ReportWriter.WriteFiles(context, runDirectory, model);
        var persisted = store.Write(statePath, state);
        if (persisted is not null) throw new IOException("refused: " + persisted);
    }
}

internal static class TerminalPublicationGate
{
    public static bool TryEnter(CheckpointCleanupContext context, string runDirectory, out Stream? gate)
    {
        gate = null;
        try
        {
            var write = new CheckpointWrite(context);
            var refusal = write.OpenGate(Path.Combine(runDirectory, ".terminal-publication.lock"), write.Under(runDirectory), out gate);
            if (refusal is not null) throw new IOException("refused: " + refusal);
            return true;
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
        {
            // A live stopper owns publication. Its death also releases this OS lock.
            return false;
        }
    }
}
