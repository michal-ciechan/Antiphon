using Antiphon.HostCleanup.Checkpoints;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public static class EvidenceFolder
{
    public static void Write(string runDirectory, ReportModel model, bool removeToolCopy, string? imageDirectory = null,
        CheckpointCleanupContext? context = null, IReadOnlyDictionary<string, string>? replayProperties = null,
        IReadOnlySet<string>? warmBuilds = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var write = new CheckpointWrite(context);
        var ownership = write.Under(runDirectory);
        SafeWrite.Require(write.CreateDirectory(runDirectory, ownership));
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
                    var rowCommands = ReplayCommands(model, row, failure.Name, replayProperties);
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
                    rowFailures.AppendLine(rowCommands);
                    rerun.AppendLine(rowCommands);
                    // The checkpoint row validates and holds the warm partition for the duration of replay.
                    if (row.Build is null || warmBuilds?.Contains(row.Build) != true)
                    {
                        var rawCommand = RawCommand(model, row, failure.Name);
                        rowFailures.AppendLine(rawCommand);
                        rerun.AppendLine(rawCommand);
                    }
                    rowFailures.AppendLine();
                }

                var rowDir = Path.Combine(runDirectory, "rows", row.Id);
                SafeWrite.Require(write.CreateDirectory(rowDir, ownership));
                SafeWrite.Require(write.Text(Path.Combine(rowDir, "failures.md"), rowFailures.ToString(), ownership));
                SafeWrite.Require(write.Text(Path.Combine(rowDir, "rerun.txt"), rerun.ToString(), ownership));
                failures.Append(rowFailures);
            }

            SafeWrite.Require(write.Text(Path.Combine(runDirectory, "failures.md"), failures.ToString(), ownership));
        }
        else
        {
            var stale = Path.Combine(runDirectory, "failures.md");
            if (context.Io.FileExists(stale))
                new CheckpointDelete(context).DeleteCreatedFile(stale);
        }

        if (removeToolCopy)
            TryRemoveToolCopy(runDirectory, imageDirectory, context);
    }

    public static int SweepFinishedToolCopies(string resultsRoot, IProcessLiveness liveness, RunStateStore? store = null, CheckpointCleanupContext? context = null)
    {
        return string.IsNullOrWhiteSpace(resultsRoot) ? 0 : new ToolCopyCleanup(context: context).Sweep(resultsRoot);
    }

    public static void TryRemoveToolCopy(string runDirectory, string? imageDirectory = null, CheckpointCleanupContext? context = null)
    {
        if (IsExecutorImage(runDirectory, imageDirectory, context))
            return;
        new ToolCopyCleanup(context: context).Remove(runDirectory);
    }

    public static bool IsExecutorImage(string runDirectory, string? imageDirectory = null, CheckpointCleanupContext? context = null) =>
        ExecutorImageGuard.IsExecutorImage(context ?? CheckpointCleanupDefaults.Context, runDirectory, imageDirectory);

    public static string RowCommand(ReportModel model, ReportRow row, string failureName,
        IReadOnlyDictionary<string, string>? replayProperties = null) =>
        RowCommand(model, row, failureName, replayProperties, powerShell: OperatingSystem.IsWindows());

    private static string ReplayCommands(ReportModel model, ReportRow row, string failureName,
        IReadOnlyDictionary<string, string>? replayProperties)
    {
        var powerShell = "# PowerShell 7.3+" + Environment.NewLine
            + RowCommand(model, row, failureName, replayProperties, powerShell: true);
        return OperatingSystem.IsWindows() ? powerShell
            : "# POSIX sh" + Environment.NewLine
                + RowCommand(model, row, failureName, replayProperties, powerShell: false)
                + Environment.NewLine + powerShell;
    }

    private static string RowCommand(ReportModel model, ReportRow row, string failureName,
        IReadOnlyDictionary<string, string>? replayProperties, bool powerShell)
    {
        string Quote(string value) => ShellArgument(value, powerShell);
        var command = new StringBuilder();
        if (powerShell)
            command.Append("& { $PSNativeCommandArgumentPassing = 'Standard'; ");
        command.Append("dotnet run --project tools/Antiphon.Checkpoints -- row --name ")
            .Append(Quote(row.Id)).Append(" --project ").Append(Quote(ProjectOf(model, row)))
            .Append(" --output-path ").Append(Quote(OutputOf(model, row)))
            .Append(" --filter ").Append(Quote(FilterOf(failureName))).Append(" --no-build");
        if (row.Source.Start is { CaptureStatus: "known", DirtyFiles: 0, Commit: { } commit })
            command.Append(" --expected-source-sha ").Append(Quote(commit));
        if (row.Repeat is { Requested: > 1 } repeat)
            command.Append(" --repeat ").Append(repeat.Requested.ToString(CultureInfo.InvariantCulture));
        if (replayProperties is not null)
        {
            foreach (var (name, value) in replayProperties)
                command.Append(" --msbuild-property ").Append(Quote(name + "=" + value));
        }
        if (powerShell)
            command.Append(" }");
        return command.ToString();
    }

    private static string ShellArgument(string value, bool powerShell)
    {
        if (value.Length != 0 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '/' or '.' or '='))
            return value;
        var escaped = powerShell
            ? Regex.Replace(value, "['\u2018-\u201B]", "$&$&")
            : value.Replace("'", "'\"'\"'", StringComparison.Ordinal);
        return "'" + escaped + "'";
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
