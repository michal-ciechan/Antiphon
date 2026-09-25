using System.Text;

namespace Antiphon.Checkpoints;

public static class EvidenceFolder
{
    public static void Write(string runDirectory, ReportModel model, bool removeToolCopy)
    {
        Directory.CreateDirectory(runDirectory);
        if (model.Rows.Any(row => row.Failures.Count > 0))
        {
            var failures = new StringBuilder();
            var rerun = new StringBuilder();
            foreach (var row in model.Rows)
            {
                foreach (var failure in row.Failures)
                {
                    failures.AppendLine("## " + failure.Name);
                    failures.AppendLine("outcome: Failed");
                    failures.AppendLine("durationSeconds: " + failure.DurationSeconds);
                    failures.AppendLine("baseline: " + (failure.Baseline ?? "n/a"));
                    failures.AppendLine("message:");
                    failures.AppendLine(Cap(failure.Message));
                    failures.AppendLine("stack:");
                    failures.AppendLine(Cap(failure.StackTrace));
                    failures.AppendLine("stdout:");
                    failures.AppendLine(Cap(failure.StdOut));
                    var rowCommand = RowCommand(model, row, failure.Name);
                    var rawCommand = RawCommand(row, failure.Name);
                    failures.AppendLine("rerun:");
                    failures.AppendLine(rowCommand);
                    failures.AppendLine(rawCommand);
                    failures.AppendLine();
                    rerun.AppendLine(rowCommand);
                    rerun.AppendLine(rawCommand);
                }

                var rowDir = Path.Combine(runDirectory, "rows", row.Id);
                if (row.Failures.Count > 0 && Directory.Exists(rowDir))
                    File.WriteAllText(Path.Combine(rowDir, "rerun.txt"), rerun.ToString());
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
        {
            var tool = Path.Combine(runDirectory, "tool");
            if (Directory.Exists(tool))
                Directory.Delete(tool, recursive: true);
        }
    }

    public static string RowCommand(ReportModel model, ReportRow row, string failureName)
    {
        var filter = RerunPolicy.MethodFilter([failureName]);
        return "dotnet run --project tools/Antiphon.Checkpoints -- row --name " + row.Id
            + " --project " + ProjectOf(model, row)
            + " --output-path " + OutputOf(model, row)
            + " --filter '" + filter + "' --no-build";
    }

    public static string RawCommand(ReportRow row, string failureName)
    {
        var filter = RerunPolicy.MethodFilter([failureName]);
        return "dotnet run --project " + (row.Build ?? "tests/Antiphon.Tests")
            + " --no-build -- --treenode-filter '" + filter + "'";
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
