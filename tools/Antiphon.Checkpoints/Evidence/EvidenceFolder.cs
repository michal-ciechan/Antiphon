using System.Text;

namespace Antiphon.Checkpoints;

public static class EvidenceFolder
{
    public static void Write(string runDirectory, ReportModel model, bool removeToolCopy, string? imageDirectory = null)
    {
        Directory.CreateDirectory(runDirectory);
        if (model.Rows.Any(row => row.Failures.Count > 0))
        {
            var failures = new StringBuilder();
            foreach (var row in model.Rows)
            {
                if (row.Failures.Count == 0)
                    continue;
                var rowFailures = new StringBuilder();
                var rerun = new StringBuilder();
                foreach (var failure in row.Failures)
                {
                    var rowCommand = RowCommand(model, row, failure.Name);
                    var rawCommand = RawCommand(model, row, failure.Name);
                    rowFailures.AppendLine("## " + failure.Name);
                    rowFailures.AppendLine("outcome: Failed");
                    rowFailures.AppendLine("durationSeconds: " + failure.DurationSeconds);
                    rowFailures.AppendLine("baseline: " + (failure.Baseline ?? "n/a"));
                    rowFailures.AppendLine("message:");
                    rowFailures.AppendLine(Cap(failure.Message));
                    rowFailures.AppendLine("stack:");
                    rowFailures.AppendLine(Cap(failure.StackTrace));
                    rowFailures.AppendLine("stdout:");
                    rowFailures.AppendLine(Cap(failure.StdOut));
                    rowFailures.AppendLine("rerun:");
                    rowFailures.AppendLine(rowCommand);
                    rowFailures.AppendLine(rawCommand);
                    rowFailures.AppendLine();
                    rerun.AppendLine(rowCommand);
                    rerun.AppendLine(rawCommand);
                }

                var rowDir = Path.Combine(runDirectory, "rows", row.Id);
                Directory.CreateDirectory(rowDir);
                File.WriteAllText(Path.Combine(rowDir, "failures.md"), rowFailures.ToString());
                File.WriteAllText(Path.Combine(rowDir, "rerun.txt"), rerun.ToString());
                failures.Append(rowFailures);
            }

            File.WriteAllText(Path.Combine(runDirectory, "failures.md"), failures.ToString());
        }
        else
        {
            var stale = Path.Combine(runDirectory, "failures.md");
            if (File.Exists(stale))
                File.Delete(stale);
        }

        if (removeToolCopy)
            TryRemoveToolCopy(runDirectory, imageDirectory);
    }

    public static void TryRemoveToolCopy(string runDirectory, string? imageDirectory = null)
    {
        if (IsExecutorImage(runDirectory, imageDirectory))
            return;
        var tool = Path.Combine(runDirectory, "tool");
        if (!Directory.Exists(tool))
            return;
        try
        {
            Directory.Delete(tool, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static bool IsExecutorImage(string runDirectory, string? imageDirectory = null)
    {
        var tool = Path.GetFullPath(Path.Combine(runDirectory, "tool"));
        var image = Path.GetFullPath(imageDirectory ?? AppContext.BaseDirectory);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = tool.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!image.EndsWith(Path.DirectorySeparatorChar) && !image.EndsWith(Path.AltDirectorySeparatorChar))
            image += Path.DirectorySeparatorChar;
        return image.StartsWith(prefix, comparison);
    }

    public static string RowCommand(ReportModel model, ReportRow row, string failureName)
    {
        var filter = FilterOf(failureName);
        return "dotnet run --project tools/Antiphon.Checkpoints -- row --name " + row.Id
            + " --project " + ProjectOf(model, row)
            + " --output-path " + OutputOf(model, row)
            + " --filter \"" + filter + "\" --no-build";
    }

    public static string RawCommand(ReportModel model, ReportRow row, string failureName)
    {
        var filter = FilterOf(failureName);
        return "dotnet run --project " + ProjectOf(model, row)
            + " --no-build --property:OutputPath=" + OutputOf(model, row)
            + " -- --treenode-filter \"" + filter + "\"";
    }

    private static string FilterOf(string failureName)
    {
        var filters = RerunPolicy.MethodFilters([failureName]);
        return filters.Count == 0 ? "" : filters[0];
    }

    private static string ProjectOf(ReportModel model, ReportRow row) =>
        model.Builds.FirstOrDefault(build => build.Id == row.Build)?.Project ?? "tests/Antiphon.Tests";

    private static string OutputOf(ReportModel model, ReportRow row)
    {
        var id = row.Build ?? "bin-out";
        return id.EndsWith('/') ? id : id + "/";
    }

    private static string Cap(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Take(200));
    }
}
