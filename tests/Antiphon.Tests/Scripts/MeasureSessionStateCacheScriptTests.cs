using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class MeasureSessionStateCacheScriptTests
{
    [Test]
    public async Task Matched_true_and_false_settings_compare()
    {
        foreach (var reset in new[] { true, false })
        {
            var before = Evidence(false, reset);
            var after = Evidence(true, reset);
            var result = await CompareAsync(before, after);
            result.ExitCode.ShouldBe(0, $"matched noResetOnClose={reset}: {result.Output}");
            result.Summary!["queryAndWorkloadGates"]!.GetValue<bool>().ShouldBeTrue();
            result.Summary["accepted"]!.GetValue<bool>().ShouldBeFalse();
        }
    }

    private static JsonObject Evidence(bool after, bool reset = true)
    {
        var windows = new JsonArray();
        foreach (var workload in new[] { "idle", "activity" })
        {
            var start = Snapshot(0, after, reset, workload);
            var end = Snapshot(180, after, reset, workload);
            windows.Add(new JsonObject
            {
                ["workload"] = workload, ["start"] = start, ["end"] = end,
                ["samples"] = new JsonArray(start.DeepClone(), Snapshot(90, after, reset, workload), end.DeepClone())
            });
        }
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["card"] = "CARD-0701", ["phase"] = after ? "After" : "Before",
            ["round"] = after ? "R2" : "R1", ["canonicalSha"] = new string(after ? 'b' : 'a', 40),
            ["context"] = Context(), ["windows"] = windows
        };
    }

    private static JsonObject Context() => new()
    {
        ["openClients"] = 1, ["workloadKey"] = "natural-observed",
        ["siblingShas"] = new JsonObject
        {
            ["CARD-0696"] = new string('c', 40), ["CARD-0698"] = new string('c', 40),
            ["CARD-0699"] = new string('c', 40), ["CARD-0700"] = new string('c', 40)
        }
    };

    private static JsonObject Snapshot(int seconds, bool after, bool reset, string workload)
    {
        var progress = seconds / 180.0;
        var statements = new JsonArray();
        foreach (var shape in new[] { "working", "binding", "transcript-insert" })
        {
            var count = shape == "transcript-insert" ? (workload == "activity" ? 10 : 0) : (after ? 1 : 100);
            statements.Add(new JsonObject
            {
                ["dbid"] = 1, ["userid"] = 1, ["queryid"] = shape, ["toplevel"] = true,
                ["calls"] = 100 + (long)(count * progress), ["rows"] = 100 + (long)(count * progress),
                ["total_exec_time"] = 100 + count * progress, ["shape"] = shape, ["fingerprint"] = shape,
                ["transcript_select"] = shape == "working"
            });
        }
        return new JsonObject
        {
            ["utc"] = DateTimeOffset.Parse("2026-10-04T00:00:00Z").AddSeconds(seconds).ToString("O"),
            ["version"] = new JsonObject { ["version"] = new string(after ? 'b' : 'a', 40) },
            ["observation"] = Context(),
            ["runtime"] = new JsonObject
            {
                ["processId"] = 123, ["processStartedAt"] = "2026-10-03T00:00:00Z",
                ["cpuSeconds"] = 100 + seconds * (after ? 0.1 : 0.5), ["liveSessions"] = 12, ["unknownSessions"] = 0,
                ["driverVersion"] = "9.0.4", ["providerVersion"] = "9.0.4",
                ["pool"] = new JsonObject { ["pooling"] = true, ["noResetOnClose"] = reset, ["maxAutoPrepare"] = 0, ["multiplexing"] = false }
            },
            ["postgres"] = new JsonObject
            {
                ["containerIdentity"] = "container-one|2026-10-03", ["containerCpuSeconds"] = 100 + seconds * (after ? 0.1 : 0.5),
                ["statistics"] = new JsonObject
                {
                    ["postmasterStartedAt"] = "2026-10-03T00:00:00Z", ["databaseOid"] = 1,
                    ["info"] = new JsonObject { ["stats_reset"] = "2026-10-03", ["dealloc"] = 0 }, ["statements"] = statements
                }
            }
        };
    }

    private static async Task<(int ExitCode, string Output, JsonObject? Summary)> CompareAsync(JsonObject before, JsonObject after)
    {
        var root = Path.Combine(Path.GetTempPath(), "c701-collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var baseline = Path.Combine(root, "baseline.json");
            var afterPath = Path.Combine(root, "after.json");
            var beforeText = before.ToJsonString();
            var afterText = after.ToJsonString();
            await File.WriteAllTextAsync(baseline, beforeText);
            await File.WriteAllTextAsync(afterPath, afterText);
            var result = await RunAsync("-File", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "measure-session-state-cache.ps1"),
                "-Mode", "Compare", "-Round", "R3", "-EvidenceRoot", root, "-BaselinePath", baseline, "-AfterPath", afterPath);
            (await File.ReadAllTextAsync(baseline)).ShouldBe(beforeText, "comparison must preserve baseline bytes");
            (await File.ReadAllTextAsync(afterPath)).ShouldBe(afterText, "comparison must preserve after bytes");
            var summary = Directory.GetFiles(root, "compare-*.json").SingleOrDefault();
            return (result.ExitCode, result.Output, summary is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(summary))!.AsObject());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = DelegateScriptRunner.RepoRoot };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        // Offline Compare must not accidentally use inherited production credentials.
        info.Environment.Remove("ANTIPHON_TASK_TOKEN");
        info.Environment["ANTIPHON_API"] = "http://127.0.0.1:1";
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Offline collector exceeded 30 seconds.");
        }
        return (process.ExitCode, await stdout + await stderr);
    }
}
