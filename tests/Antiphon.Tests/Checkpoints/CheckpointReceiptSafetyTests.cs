using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[NotInParallel]
public sealed class CheckpointReceiptSafetyTests : CheckpointTestBase
{
    [Test]
    public async Task linux_fifo_receipts_refuse_within_four_seconds()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux FIFO qualification requires Linux");
        await FileTypes("fifo");
    }

    [Test]
    public async Task symlink_and_directory_receipts_refuse_within_four_seconds()
    {
        await FileTypes("symlink");
        await FileTypes("directory");
    }

    private async Task FileTypes(string kind)
    {
        var failures = new List<string>();
        foreach (var verb in new[] { "report", "report-json", "stop" })
        foreach (var name in new[] { "state.json", "report.json", "report.md" })
        {
            var (root, run) = Fixture();
            var path = Path.Combine(run, name);
            var original = File.ReadAllBytes(path);
            File.Delete(path);
            if (kind == "fifo") MkFifo(path, 0x180).ShouldBe(0);
            if (kind == "directory") Directory.CreateDirectory(path);
            if (kind == "symlink")
            {
                File.WriteAllBytes(path + ".target", original);
                File.CreateSymbolicLink(path, path + ".target");
            }
            try
            {
                var result = await Child(root, run, verb);
                if (result.Exit != 2 || !result.Output.Contains("refused:", StringComparison.Ordinal))
                    failures.Add($"{kind}/{name}/{verb}: exit={result.Exit} {result.Output}");
                if (kind == "symlink") File.ReadAllBytes(path + ".target").ShouldBe(original);
            }
            finally
            {
                if (kind == "directory") Directory.Delete(path);
                else File.Delete(path);
            }
        }
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    [Test]
    public async Task malformed_collection_and_string_shapes_return_named_refusals()
    {
        var failures = new List<string>();
        var fields = new Dictionary<string, string[]>
        {
            ["report.json"] = ["outputPaths", "builds", "rows", "unlisted", "cleanupRefusals", "outputNames",
                "runId", "manifestHash", "commit", "branch", "worktree", "verdict", "markdownSha256",
                "host.os", "host.loadAvg", "source.state", "source.buildSource"],
            ["state.json"] = ["builds", "rows", "createdOutputPaths", "runId", "toolOwner", "phase", "marker",
                "source.state", "source.buildSource"],
        };
        foreach (var pair in fields)
        foreach (var field in pair.Value)
        foreach (var shape in new[] { "null", "wrong-type", "missing" })
        foreach (var verb in new[] { "report", "report-json", "stop", "status" })
        {
            var (root, run) = Fixture();
            var path = Path.Combine(run, pair.Key);
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var parts = field.Split('.');
            var parent = parts.Length == 1 ? json : json[parts[0]]!.AsObject();
            if (shape == "missing") parent.Remove(parts[^1]);
            else parent[parts[^1]] = shape == "null" ? null : JsonValue.Create(42);
            File.WriteAllText(path, json.ToJsonString());
            var before = File.ReadAllBytes(Path.Combine(run, "state.json"));
            var output = new StringWriter();
            try
            {
                var code = await Antiphon.Checkpoints.Program.RunAsync(Args(root, run, verb), new CheckpointApp.Runtime { Output = output });
                if (code != 2 || !output.ToString().Contains("refused:", StringComparison.Ordinal))
                    failures.Add($"{pair.Key}/{field}/{shape}/{verb}: exit={code} {output}");
            }
            catch (Exception ex) { failures.Add($"{pair.Key}/{field}/{shape}/{verb}: {ex.GetType().Name}: {ex.Message}"); }
            File.ReadAllBytes(Path.Combine(run, "state.json")).ShouldBe(before, field + " state unchanged");
        }
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    private (string Root, string Run) Fixture()
    {
        var root = TempDir();
        var run = Path.Combine(root, "run");
        Directory.CreateDirectory(run);
        CheckpointFixtures.MarkRun(run, alive: false);
        var observation = new SourceObservation(new string('a', 40), 0, new string('1', 64), DateTimeOffset.UnixEpoch, "known");
        var source = new SourceEvidence { Start = observation, End = observation, State = "clean", BuildSource = "verified" };
        SafeWrite.Require(new RunStateStore().Write(Path.Combine(run, "state.json"), new RunState
        { RunId = "run", Phase = "done", ExitCode = 0, Source = source }));
        ReportWriter.WriteFiles(run, new ReportModel { RunId = "run", Commit = observation.Commit!, Source = source });
        return (root, run);
    }

    private static string[] Args(string root, string run, string verb) =>
        verb == "report-json" ? ["report", "--json", "--repo-root", root, "--run", run, "--results-root", root]
        : [verb, "--repo-root", root, "--run", run, "--results-root", root];

    private static async Task<(int Exit, string Output)> Child(string root, string run, string verb)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
        var testAssembly = typeof(CheckpointReceiptSafetyTests).Assembly.Location;
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"), typeof(Antiphon.Checkpoints.Program).Assembly.Location }.Concat(Args(root, run, verb)))
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return (-1, "HUNG: killed and awaited at four-second hard bound " + await stdout + await stderr);
        }
        return (process.ExitCode, await stdout + await stderr);
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
}
