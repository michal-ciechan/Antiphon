using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class ReportWriter
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static string Markdown(ReportModel model)
    {
        var text = new StringBuilder();
        text.AppendLine("--- checkpoint report ---");
        text.AppendLine($"run: {model.RunId}   manifest: {model.ManifestPath}");
        text.AppendLine($"commit: {model.Commit}  branch: {model.Branch}  worktree: {model.Worktree}  host: {model.Host.Os} cores={model.Host.Cores}");
        foreach (var row in model.Rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Line))
                text.AppendLine(row.Line);
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

        text.AppendLine(model.Unlisted.Count == 0
            ? "unlisted: none (the tool ran no other build or test command)"
            : "unlisted: " + string.Join("; ", model.Unlisted));
        var green = model.Rows.Count(row => row.ExitCode == ExitCodes.Green);
        var skipped = model.Rows.Count(row => row.State is "skipped");
        var red = model.Rows.Count - green - skipped;
        text.AppendLine(
            $"wall: {Format(model.WallSeconds)}  sequential-equivalent: {Format(model.SequentialEquivalentSeconds)}  builds: {model.Builds.Count}  rows: {green} green {red} red {skipped} skipped");
        if (model.ExitCode == 0)
            text.AppendLine("outputs: deleted " + string.Join(", ", model.OutputNames));
        else
            text.AppendLine($"outputs: kept {string.Join(", ", model.OutputNames)} (red run) -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run {model.RunId}");
        text.AppendLine("evidence: " + model.Evidence);
        text.AppendLine($"verdict: {model.Verdict} exit={model.ExitCode}");
        return text.ToString();
    }

    public static string JsonText(ReportModel model) => JsonSerializer.Serialize(model, Json);

    public static void WriteFiles(string runDirectory, ReportModel model)
    {
        var markdown = Markdown(model);
        File.WriteAllText(Path.Combine(runDirectory, "report.md"), markdown);
        File.WriteAllText(Path.Combine(runDirectory, "report.json"), JsonText(model));
    }

    private static string Format(double seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var whole = (int)seconds;
        return $"{whole / 60}m{whole % 60:00}s";
    }
}
