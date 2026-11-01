using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointReceiptSafetyTests : CheckpointTestBase
{
    [NativeLinuxOnly("Linux FIFO qualification requires Linux")]
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
        var (controlRoot, controlRun) = Fixture();
        var control = await Child(controlRoot, controlRun, "report-json");
        control.Exit.ShouldBe(0, "healthy native control: " + control.Output);
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
                "host.os", "host.loadAvg", "source.state", "source.buildSource", "source.start.captureStatus", "source.end.captureStatus",
                "builds.0.id", "builds.0.project", "builds.0.state", "builds.0.slot",
                "rows.0.id", "rows.0.state", "rows.0.failures", "rows.0.rerunLines", "rows.0.slowClasses"],
            ["state.json"] = ["builds", "rows", "createdOutputPaths", "runId", "toolOwner", "phase", "marker",
                "source.state", "source.buildSource", "source.start.captureStatus", "source.end.captureStatus",
                "builds.0.id", "builds.0.state", "builds.0.slot", "rows.0.id", "rows.0.state", "rows.0.slot"],
        };
        foreach (var pair in fields)
        foreach (var field in pair.Value)
        foreach (var shape in new[] { "null", "wrong-type", "missing", "empty-required-string" })
        foreach (var verb in new[] { "report", "report-json", "stop", "status" })
        {
            if (shape == "empty-required-string" && field is not ("runId" or "toolOwner" or "phase" or "verdict" or "markdownSha256" or "source.state" or "source.buildSource" or "source.start.captureStatus" or "source.end.captureStatus")) continue;
            var (root, run) = Fixture();
            var path = Path.Combine(run, pair.Key);
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var parts = field.Split('.');
            JsonNode parent = json;
            foreach (var part in parts[..^1]) parent = int.TryParse(part, out var index) ? parent[index]! : parent[part]!;
            if (shape == "missing") parent.AsObject().Remove(parts[^1]);
            else parent[parts[^1]] = shape == "null" ? null : shape == "empty-required-string" ? JsonValue.Create("") : JsonValue.Create(42);
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
            if (!File.ReadAllBytes(Path.Combine(run, "state.json")).SequenceEqual(before))
                failures.Add($"{pair.Key}/{field}/{shape}/{verb}: state changed");
        }
        foreach (var field in new[] { "outputPaths", "builds", "rows", "unlisted", "cleanupRefusals", "outputNames" })
        foreach (var variant in new[] { "null-element", "empty-element", "required-empty" })
        foreach (var verb in new[] { "report", "report-json", "stop" })
        {
            if (variant != "null-element" && field != "outputPaths") continue;
            var (root, run) = Fixture();
            var path = Path.Combine(run, "report.json");
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json[field] = variant == "null-element" ? new JsonArray((JsonNode?)null)
                : variant == "empty-element" ? new JsonArray(JsonValue.Create("")) : new JsonArray();
            if (variant == "required-empty") json["outputNames"] = new JsonArray(JsonValue.Create("owned/"));
            File.WriteAllText(path, json.ToJsonString());
            var output = new StringWriter();
            try
            {
                var code = await Antiphon.Checkpoints.Program.RunAsync(Args(root, run, verb), new CheckpointApp.Runtime { Output = output });
                if (code != 2 || !output.ToString().Contains("refused:", StringComparison.Ordinal)) failures.Add($"{field}/{variant}/{verb}: exit={code} {output}");
            }
            catch (Exception ex) { failures.Add($"{field}/{variant}/{verb}: {ex.GetType().Name}"); }
        }
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    [Test]
    [Arguments("")]
    [Arguments(" \t\r\n")]
    [Arguments(null)]
    [Arguments("CP-fixture")]
    public async Task receipt_row_and_build_identities_refuse_blank_without_writing(string? identity)
    {
        var valid = identity == "CP-fixture";
        var failures = new List<string>();
        // Each entry gets its own complete receipt pair; only the selected identity changes.
        foreach (var receipt in new[] { "state.json", "report.json" })
        foreach (var collection in new[] { "rows", "builds" })
        foreach (var verb in new[] { "report", "report-json", "report-merge", "report-merge-json", "status", "stop", "wait", "clean", "validate" })
        {
            if (verb == "validate" && receipt == "state.json") continue; // validate accepts a report, not state.
            var (root, run) = Fixture();
            var path = Path.Combine(run, receipt);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json[collection]![0]!["id"] = identity;
            File.WriteAllText(path, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var before = Directory.GetFiles(run).ToDictionary(file => file, File.ReadAllBytes);
            var result = await Child(root, run, verb);
            var expected = valid && verb != "clean" ? 0 : 2;
            if (result.Exit != expected)
                failures.Add($"{receipt}/{collection}/{verb}: expected={expected} exit={result.Exit} {result.Output}");
            if (!valid && !(result.Output.Contains("refused:", StringComparison.Ordinal)
                || verb == "validate" && result.Output.Contains("CHECKPOINT SOURCE INVALID reason=", StringComparison.Ordinal)))
                failures.Add($"{receipt}/{collection}/{verb}: missing named refusal: {result.Output}");
            if (!valid && verb == "clean" && !result.Output.Contains("receipt-", StringComparison.Ordinal))
                failures.Add($"{receipt}/{collection}/{verb}: refused only by cleanup policy, identity was accepted: {result.Output}");
            if (!valid)
            {
                if (!Directory.GetFiles(run).Order().SequenceEqual(before.Keys.Order()))
                    failures.Add($"{receipt}/{collection}/{verb}: file inventory changed");
                foreach (var file in before)
                    if (!File.Exists(file.Key) || !File.ReadAllBytes(file.Key).SequenceEqual(file.Value))
                        failures.Add($"{receipt}/{collection}/{verb}: bytes changed: {file.Key}");
            }
        }
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    [Test]
    public async Task malformed_state_shape_by_verb_table_matches_documented_exits()
    {
        var failures = new List<string>();
        foreach (var collection in new[] { "rows", "builds" })
        foreach (var shape in new[] { "null", "missing", "wrong-type", "null-element", "blank-identity" })
        foreach (var verb in new[] { "report", "report-json", "report-merge", "report-merge-json", "status", "stop", "wait", "clean", "validate" })
        {
            var (root, run) = Fixture();
            var path = Path.Combine(run, "state.json");
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (shape == "missing") json.Remove(collection);
            else if (shape == "null") json[collection] = null;
            else if (shape == "wrong-type") json[collection] = 42;
            else if (shape == "null-element") json[collection] = new JsonArray((JsonNode?)null);
            else json[collection]![0]!["id"] = " ";
            File.WriteAllText(path, json.ToJsonString());
            var before = Directory.GetFiles(run).ToDictionary(p => p, File.ReadAllBytes);
            var result = await Child(root, run, verb);
            // validate accepts report only; malformed-state wait retains the documented dead-executor exit 6.
            var expected = verb == "validate" ? 0 : verb == "wait" && shape != "blank-identity" ? 6 : 2;
            var reason = verb == "validate" ? "CHECKPOINT SOURCE VALID" : expected == 6 ? "executor died without phase=done" : "refused:";
            if (result.Exit != expected || !result.Output.Contains(reason, StringComparison.Ordinal))
                failures.Add($"{collection}/{shape}/{verb}: expected={expected} reason={reason}, exit={result.Exit} {result.Output}");
            if (expected == 2 && verb == "clean" && !result.Output.Contains("receipt-", StringComparison.Ordinal))
                failures.Add($"{collection}/{shape}/{verb}: missing receipt refusal");
            foreach (var pair in before)
                if (!File.Exists(pair.Key) || !File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) failures.Add($"{collection}/{shape}/{verb}: bytes changed");
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
        { RunId = "run", Phase = "done", ExitCode = 0, Source = source,
            Builds = [new BuildProgress { Id = "owned", State = "built", Slot = "granted" }],
            Rows = [new RowProgress { Id = "CP-fixture", State = "green", Slot = "granted" }] }));
        ReportWriter.WriteFiles(run, new ReportModel { RunId = "run", Commit = observation.Commit!, Source = source,
            Builds = [new ReportBuild { Id = "owned", Project = "fixture", State = "built", Slot = "granted", Source = source }],
            Rows = [new ReportRow { Id = "CP-fixture", State = "green", Source = source, Executed = 1, Passed = 1, Failed = 0, Skipped = 0,
                Line = "CHECKPOINT CP-fixture commit=" + observation.Commit + " source=" + observation.Commit
                    + " dirty=0 sourceState=clean buildSource=verified executed=1" }] });
        return (root, run);
    }

    private static string[] Args(string root, string run, string verb) =>
        verb == "validate" ? ["validate", "--evidence", Path.Combine(run, "report.json"), "--expected-source-sha", new string('a', 40), "--repo-root", root]
        : (verb.StartsWith("report", StringComparison.Ordinal)
            ? new[] { "report" }.Concat(verb.Contains("json", StringComparison.Ordinal) ? new[] { "--json" } : [])
                .Concat(verb.Contains("merge", StringComparison.Ordinal) ? new[] { "--merge" } : [])
            : new[] { verb }).Concat(new[] { "--repo-root", root, "--run", run, "--results-root", root }).ToArray();

    private async Task<(int Exit, string Output)> Child(string root, string run, string verb)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
        var testAssembly = typeof(CheckpointReceiptSafetyTests).Assembly.Location;
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"), typeof(Antiphon.Checkpoints.Program).Assembly.Location }.Concat(Args(root, run, verb)))
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        RegisterCheckpointChild(process);
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
