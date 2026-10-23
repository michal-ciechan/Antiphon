using System.Text;
using System.Text.Json;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

public static class ReportWriter
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static string Markdown(ReportModel model) => Markdown(CheckpointCleanupDefaults.Context, model);

    public static string Markdown(CheckpointCleanupContext context, ReportModel model)
    {
        var text = new StringBuilder();
        text.AppendLine("--- checkpoint report ---");
        text.AppendLine($"run: {model.RunId}   manifest: {model.ManifestPath}");
        text.AppendLine($"commit: {model.Commit}  branch: {model.Branch}  worktree: {model.Worktree}  host: {model.Host.Os} cores={model.Host.Cores}");
        text.AppendLine($"source: {SourceEvidence.Token(model.Source.Start)} state={model.Source.State} buildSource={model.Source.BuildSource}");
        foreach (var row in model.Rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Line))
                text.AppendLine(row.Line);
            if (row.ExecutedSeconds is double executed && row.WallSeconds is double wall)
                text.AppendLine(FormattableString.Invariant($"CHECKPOINT-TIMING {row.Id} waited={row.WaitedSeconds}s executed={executed:0.###}s wall={wall:0.###}s") + (row.TerminalReason is null ? "" : " reason=" + row.TerminalReason));
            if (row.Repeat is { } repeat)
            {
                foreach (var ordinal in repeat.Repetitions)
                    text.AppendLine($"REPETITION {ordinal.Ordinal + 1}/{repeat.Requested} executed={ordinal.Executed} passed={ordinal.Passed} failed={ordinal.Failed} skipped={ordinal.Skipped} ({row.Id})");
            }
            if (row.Timings is { } phases)
                text.AppendLine($"PHASES {row.Id} slotWait={phases.SlotWaitSeconds?.ToString() ?? "unavailable"}s build={phases.BuildSeconds?.ToString() ?? "unavailable"}s startup={phases.StartupSeconds?.ToString() ?? "unavailable"}s testsWall={phases.TestsWallSeconds?.ToString() ?? "unavailable"}s teardown={phases.TeardownSeconds?.ToString() ?? "unavailable"}s hostWall={phases.TestHostWallSeconds?.ToString() ?? "unavailable"}s" + (phases.UnavailableReason is null ? "" : " reason=" + phases.UnavailableReason));
            foreach (var rerun in row.RerunLines)
                text.AppendLine(rerun);
            foreach (var failure in row.Failures)
            {
                var baseline = string.IsNullOrWhiteSpace(failure.Baseline) ? "" : " " + failure.Baseline + ":";
                var message = string.IsNullOrWhiteSpace(failure.Message) ? "" : " " + failure.Message.Replace('\n', ' ').Trim();
                text.AppendLine($"FAILED {failure.Name} ({row.Id}){baseline}{message} -> rows/{row.Id}/failures.md");
            }

            foreach (var slow in row.SlowClasses.Where(item => item.Seconds >= 60))
                text.AppendLine($"SLOW CLASS {slow.ClassName} {(int)slow.Seconds}s tests={slow.Tests} ({row.Id})");
        }

        if (model.BudgetWallSeconds is double budgetWall)
            text.AppendLine(FormattableString.Invariant($"budget: wall={budgetWall:0.###}s admissionOnly={model.AdmissionOnlySeconds:0.###}s execution={model.ExecutionSeconds:0.###}s executionLimit={model.ExecutionLimitSeconds:0.###}s hardWallLimit={model.HardWallLimitSeconds:0.###}s"));
        text.AppendLine(model.Unlisted.Count == 0
            ? "unlisted: none (the tool ran no other build or test command)"
            : "unlisted: " + string.Join("; ", model.Unlisted));
        var green = model.Rows.Count(row => row.ExitCode == ExitCodes.Green);
        var skipped = model.Rows.Count(row => row.State is "skipped");
        var red = model.Rows.Count - green - skipped;
        text.AppendLine(
            $"wall: {Format(model.WallSeconds)}  sequential-equivalent: {Format(model.SequentialEquivalentSeconds)}  builds: {model.Builds.Count}  max-concurrent-builds: {model.MaxConcurrentBuilds}  rows: {green} green {red} red {skipped} skipped");
        if (model.CleanupRefusals.Count > 0)
        {
            foreach (var reason in model.CleanupRefusals)
                text.AppendLine("refused: " + reason);
        }

        if (model.OutputNames.Count == 0 && model.OutputPaths.Count == 0)
            text.AppendLine("outputs: none");
        else
            text.AppendLine(new CheckpointWrite(context).FormatOutputs(model.OutputNames, model.OutputPaths));
        text.AppendLine("evidence: " + model.Evidence);
        text.AppendLine($"verdict: {model.Verdict} exit={model.ExitCode}" + (model.Reason is null ? "" : " reason=" + model.Reason));
        return text.ToString();
    }

    public static string JsonText(ReportModel model) => JsonSerializer.Serialize(model, Json);

    public static void WriteFiles(string runDirectory, ReportModel model) =>
        WriteFiles(CheckpointCleanupDefaults.Context, runDirectory, model);

    public static void WriteFiles(CheckpointCleanupContext context, string runDirectory, ReportModel model)
    {
        var markdown = Markdown(context, model);
        model.MarkdownSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
        var write = new CheckpointWrite(context);
        var ownership = write.Under(runDirectory);
        var markdownRefusal = write.Text(Path.Combine(runDirectory, "report.md"), markdown, ownership);
        var jsonRefusal = write.Text(Path.Combine(runDirectory, "report.json"), JsonText(model), ownership);
        if (markdownRefusal is not null || jsonRefusal is not null)
            throw new IOException("refused: required " + (markdownRefusal is not null ? "report.md" : "report.json") + " receipt-write-failed: " + (markdownRefusal ?? jsonRefusal));
        if (ReceiptFile.ReadText(context, Path.Combine(runDirectory, "report.md")) != markdown
            || ReceiptFile.ReadText(context, Path.Combine(runDirectory, "report.json")) != JsonText(model))
            throw new IOException("refused: required report receipt readback mismatching");
    }

    public static string? RequiredReceiptRefusal(string runDirectory, RunState state) =>
        RequiredReceiptRefusal(CheckpointCleanupDefaults.Context, runDirectory, state);

    public static string? RequiredReceiptRefusal(CheckpointCleanupContext context, string runDirectory, RunState state,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (state.Source?.Start is null) return "state-receipt-source-unreadable";
            ReceiptJson.Validate(state);
            if (state.ToolOwner != SafeWrite.Marker || state.RunId != Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(runDirectory))))
                return "receipt-run-directory-mismatching";
            var markdownPath = Path.Combine(runDirectory, "report.md");
            var jsonPath = Path.Combine(runDirectory, "report.json");
            foreach (var path in new[] { markdownPath, jsonPath })
                if (!context.Io.FileExists(path)) return "receipt-missing: " + path;
            var report = ReceiptJson.Read<ReportModel>(context, jsonPath, Json, cancellationToken);
            if (report is null || state.ExitCode is null) return "receipt-unreadable-or-terminal-exit-missing";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ReceiptFile.ReadBytes(context, markdownPath, cancellationToken)));
            if (report.MarkdownSha256 != hash) return "receipt-mismatching: report.md";
            if (report.RunId != state.RunId || report.ExitCode != state.ExitCode
                || report.Commit != (state.Source.Start.Commit ?? "")
                || JsonSerializer.Serialize(report.Source, Json) != JsonSerializer.Serialize(state.Source, Json))
                return "receipt-stale-or-source-mismatching: report.json";
            if (report.ExitCode == 0 && !report.CleanedOutputs)
            {
                foreach (var output in report.OutputPaths)
                {
                    var path = Path.Combine(output, "checkpoint-build-source.json");
                    if (!context.Io.FileExists(path)) return "receipt-missing: " + path;
                    var binding = ReceiptJson.Read<CheckpointBuildBinding>(context, path, CheckpointApp.Json, cancellationToken);
                    if (binding is null || binding.Commit != report.Commit || binding.Fingerprint != report.Source.Start.Fingerprint)
                        return "receipt-source-mismatching: " + path;
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return "receipt-unreadable: " + ex.GetType().Name + ": " + ex.Message; }
    }

    private static string Format(double seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var whole = (int)seconds;
        return $"{whole / 60}m{whole % 60:00}s";
    }
}
