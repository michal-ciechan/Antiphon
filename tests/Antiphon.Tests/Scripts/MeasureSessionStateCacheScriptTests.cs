using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
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
            result.Summary!["attributionValid"]!.GetValue<bool>().ShouldBeTrue();
            result.Summary["queryAndWorkloadGates"]!.GetValue<bool>().ShouldBeTrue();
            result.Summary["accepted"]!.GetValue<bool>().ShouldBeFalse();
        }
    }

    [Test]
    public async Task Schema1_compatible_and_insufficient_pairs_are_distinguished()
    {
        foreach (var schema in new[] { 1, 2 })
        {
            var before = Evidence(false); var after = Evidence(true);
            before["schemaVersion"] = schema; after["schemaVersion"] = schema;
            (await CompareAsync(before, after)).ExitCode.ShouldBe(0, $"schema {schema} with sufficient provenance");
            foreach (var window in before["windows"]!.AsArray()) window!.AsObject().Remove("samples");
            await InconclusiveAsync(before, after, "intermediate_observations_missing");
        }
        // Old servers supply runtime observations in ContextPath. Exercise the production
        // snapshot builder with external reads substituted, so startup facts cannot hide a dip.
        var path = Path.Combine(Path.GetTempPath(), "c701-context-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var context = Context();
            context["runtime"] = new JsonObject { ["liveSessions"] = 9 };
            await File.WriteAllTextAsync(path, context.ToJsonString());
            var result = await RunFunctionsAsync($$"""
                $ContextPath = '{{PsQuote(path)}}'
                function Read-Api { param([string]$Path) return @{ version = 'observed' } }
                function Read-Runtime { param($Context) return $Context.runtime }
                function Read-Statistics { return @{ statistics = @{} } }
                $startup = @{ runtime = @{ liveSessions = 12 } }
                New-Snapshot $startup | ConvertTo-Json -Depth 10 -Compress
                """);
            result.ExitCode.ShouldBe(0, result.Output);
            JsonNode.Parse(result.Output)!["runtime"]!["liveSessions"]!.GetValue<int>().ShouldBe(9,
                "each pre-feature observation must reload current context instead of retaining startup population");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task Missing_settings_are_inconclusive()
    {
        foreach (var reset in new[] { true, false })
        foreach (var field in new[] { "pooling", "noResetOnClose", "maxAutoPrepare", "multiplexing", "driverVersion", "providerVersion" })
        {
            var after = Evidence(true, reset);
            var runtime = Middle(after)["runtime"]!.AsObject();
            (field.EndsWith("Version") ? runtime : runtime["pool"]!.AsObject()).Remove(field);
            await InconclusiveAsync(Evidence(false, reset), after, "effective_configuration_missing_or_changed");
        }
    }

    [Test]
    public async Task Changed_settings_are_inconclusive()
    {
        foreach (var field in new[] { "pooling", "noResetOnClose", "maxAutoPrepare", "multiplexing" })
        {
            var after = Evidence(true);
            var pool = Middle(after)["runtime"]!["pool"]!;
            pool[field] = field == "maxAutoPrepare" ? JsonValue.Create(5) : JsonValue.Create(!pool[field]!.GetValue<bool>());
            await InconclusiveAsync(Evidence(false), after, "effective_configuration_missing_or_changed");
        }
    }

    [Test]
    public async Task Changed_versions_are_inconclusive()
    {
        foreach (var field in new[] { "driverVersion", "providerVersion" })
        {
            var after = Evidence(true);
            Middle(after)["runtime"]![field] = "10.0.0";
            await InconclusiveAsync(Evidence(false), after, "effective_configuration_missing_or_changed");
        }
    }

    [Test]
    public async Task Wrong_baseline_identity_is_refused()
    {
        foreach (var field in new[] { "phase", "round", "card", "canonicalSha" })
        {
            var before = Evidence(false);
            before[field] = field switch { "phase" => "After", "round" => "R2", "card" => "CARD-0700", _ => "unknown" };
            await RefusedAsync(before, Evidence(true), "baseline identity");
        }
        var relabeled = Evidence(true);
        relabeled["round"] = "R2";
        await RefusedAsync(relabeled, Evidence(true), "baseline identity");
        var root = Path.Combine(Path.GetTempPath(), "c701-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await RunAsync("-File", ScriptPath, "-Mode", "Acceptance", "-EvidenceRoot", root);
            result.ExitCode.ShouldNotBe(0);
            result.Output.ShouldContain("requires a previously saved baseline");
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Baseline_files_are_immutable()
    {
        var path = Path.Combine(Path.GetTempPath(), "c701-immutable-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var create = await RunFunctionsAsync($"Save-Immutable '{PsQuote(path)}' @{{ original = 701 }}");
            create.ExitCode.ShouldBe(0, create.Output);
            var original = await File.ReadAllBytesAsync(path);
            var overwrite = await RunFunctionsAsync($"Save-Immutable '{PsQuote(path)}' @{{ replacement = 999 }}");
            (await File.ReadAllBytesAsync(path)).ShouldBe(original);
            overwrite.ExitCode.ShouldNotBe(0, "CreateNew must reject overwriting immutable evidence");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task Server_restart_invalidates_window()
    {
        foreach (var intermediate in new[] { false, true })
        {
            var after = Evidence(true);
            (intermediate ? Middle(after) : End(after))["runtime"]!["processStartedAt"] = "2026-10-04T00:01:00Z";
            await RefusedAsync(Evidence(false), after, "Server restarted");
        }
    }

    [Test]
    public async Task Postgres_restart_invalidates_window()
    {
        foreach (var field in new[] { "containerIdentity", "postmasterStartedAt" })
        {
            var after = Evidence(true);
            var pg = End(after)["postgres"]!;
            (field == "containerIdentity" ? pg : pg["statistics"]!)[field] = "replacement-instance";
            await RefusedAsync(Evidence(false), after, "PostgreSQL restarted");
        }
    }

    [Test]
    public async Task Statistics_reset_invalidates_window()
    {
        foreach (var field in new[] { "stats_reset", "dealloc", "databaseOid" })
        {
            var after = Evidence(true);
            var stats = End(after)["postgres"]!["statistics"]!;
            if (field == "databaseOid") stats[field] = 2;
            else stats["info"]![field] = field == "dealloc" ? JsonValue.Create(1) : JsonValue.Create("2026-10-04");
            await RefusedAsync(Evidence(false), after, "Statistics reset, eviction, or database identity change");
        }
    }

    [Test]
    public async Task Invalid_deltas_are_refused()
    {
        foreach (var field in new[] { "calls", "rows", "total_exec_time", "missing", "serverCpu", "postgresCpu" })
        {
            var after = Evidence(true);
            var end = End(after);
            var rows = end["postgres"]!["statistics"]!["statements"]!.AsArray();
            if (field == "missing") rows.RemoveAt(0);
            else if (field == "serverCpu") end["runtime"]!["cpuSeconds"] = 99;
            else if (field == "postgresCpu") end["postgres"]!["containerCpuSeconds"] = 99;
            else rows[0]![field] = 99;
            await RefusedAsync(Evidence(false), after, field == "missing" ? "statement disappeared" : "Negative");
        }
    }

    [Test]
    public async Task Workload_mismatch_is_inconclusive()
    {
        foreach (var scenario in new[] { "key", "clients", "middle-key", "label", "sibling" })
        {
            var after = Evidence(true);
            switch (scenario)
            {
                case "key": after["context"]!["workloadKey"] = "different"; break;
                case "clients": after["context"]!["openClients"] = 2; break;
                case "middle-key": Middle(after)["observation"]!["workloadKey"] = "changed"; break;
                case "label": after["windows"]![0]!["workload"] = "activity"; break;
                case "sibling": Middle(after)["observation"]!["siblingShas"]!["CARD-0699"] = new string('d', 40); break;
            }
            var result = await CompareAsync(Evidence(false), after);
            result.ExitCode.ShouldBe(1, scenario + result.Output);
            result.Summary!["queryAndWorkloadGates"]!.GetValue<bool>().ShouldBeFalse(scenario);
            result.Summary["attributionValid"]!.GetValue<bool>().ShouldBeFalse(scenario);
        }
    }

    [Test]
    public async Task Population_dip_between_endpoints_is_inconclusive()
    {
        var after = Evidence(true);
        Middle(after)["runtime"]!["liveSessions"] = 9;
        await InconclusiveAsync(Evidence(false), after, "population_below_ten");
        after = Evidence(true);
        after["windows"]![0]!["samples"]!.AsArray().RemoveAt(3);
        after["windows"]![0]!["samples"]!.AsArray().RemoveAt(3);
        await InconclusiveAsync(Evidence(false), after, "sample_gap_over_60_seconds");
    }

    [Test]
    public async Task Tagged_binding_and_unknown_work_are_attributed()
    {
        // Execute the collector's exact production CASE, with only the input views shadowed.
        var sqlResult = await RunFunctionsAsync("Get-StatisticsSql");
        sqlResult.ExitCode.ShouldBe(0, sqlResult.Output);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var connection = new Npgsql.NpgsqlConnection(schema.ConnectionString);
        await connection.OpenAsync();
        const string inputs = """
            WITH pg_stat_statements AS (
              SELECT (SELECT oid FROM pg_database WHERE datname=current_database()) dbid,
                1::oid userid, n::bigint queryid, true toplevel, 1::bigint calls, 1::bigint rows,
                1::double precision total_exec_time, query
              FROM (VALUES
                (1, E'-- session-state.binding\nSELECT a."RunnerId", a."RunnerStoreId", a."RunnerCwd" FROM "AgentSessions" a'),
                (2, 'SELECT "Content" FROM "TranscriptEntries"')) q(n, query)
            ), pg_stat_statements_info AS (SELECT '2026-10-03'::text stats_reset, 0::bigint dealloc)
            """;
        await using var command = new Npgsql.NpgsqlCommand(inputs + sqlResult.Output, connection);
        var stats = JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!;
        var rows = stats["statements"]!.AsArray();
        rows.Count.ShouldBe(2);
        rows.Where(r => r!["shape"]!.GetValue<string>() == "binding").Sum(r => r!["calls"]!.GetValue<int>()).ShouldBe(1);
        rows.Single(r => r!["shape"]!.GetValue<string>() == "binding")!["calls"]!.GetValue<int>().ShouldBe(1);
        rows.Where(r => r!["shape"]!.GetValue<string>() == "transcript-unknown").Sum(r => r!["calls"]!.GetValue<int>()).ShouldBe(1);
        var unknown = rows.Single(r => r!["shape"]!.GetValue<string>() == "transcript-unknown")!;
        unknown["calls"]!.GetValue<int>().ShouldBe(1);
        unknown["transcript_select"]!.GetValue<bool>().ShouldBeTrue();
        var after = Evidence(true);
        foreach (var point in AllPoints(after))
        {
            var row = unknown.DeepClone();
            row["calls"] = point["utc"]!.GetValue<string>().Contains("00:03:00") ? 1 : 0;
            row["rows"] = row["calls"]!.DeepClone(); row["total_exec_time"] = row["calls"]!.DeepClone();
            point["postgres"]!["statistics"]!["statements"]!.AsArray().Add(row);
        }
        var result = await InconclusiveAsync(Evidence(false), after, "unclassified_transcript_selects");
        result["comparisons"]![0]!["after"]!["unknownTranscriptSelectCalls"]!.GetValue<int>().ShouldBe(1);
        result["comparisons"]![0]!["after"]!["classificationComplete"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Test]
    public async Task Cpu_and_query_gates_remain_separate()
    {
        var after = Evidence(true);
        foreach (var point in AllPoints(after))
        {
            var seconds = DateTimeOffset.Parse(point["utc"]!.GetValue<string>()).TimeOfDay.TotalSeconds;
            point["runtime"]!["cpuSeconds"] = 100 + seconds;
        }
        var result = await CompareAsync(Evidence(false), after);
        result.ExitCode.ShouldBe(1, result.Output);
        result.Summary.ShouldNotBeNull(result.Output);
        result.Summary!["queryAndWorkloadGates"]!.GetValue<bool>().ShouldBeTrue();
        result.Summary["cpuGate"]!.GetValue<bool>().ShouldBeFalse();
        result.Summary["accepted"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Test]
    public async Task Zero_baseline_is_not_percentage_evidence()
    {
        var before = Evidence(false); var after = Evidence(true);
        foreach (var point in AllPoints(before).Concat(AllPoints(after)))
            foreach (var row in point["postgres"]!["statistics"]!["statements"]!.AsArray())
                if (row!["shape"]!.GetValue<string>() is "working" or "binding")
                    foreach (var counter in new[] { "calls", "rows", "total_exec_time" }) row[counter] = 100;
        var result = await CompareAsync(before, after);
        result.ExitCode.ShouldBe(1, result.Output);
        result.Summary!["attributionValid"]!.GetValue<bool>().ShouldBeTrue();
        result.Summary["queryAndWorkloadGates"]!.GetValue<bool>().ShouldBeFalse();
        foreach (var comparison in result.Summary["comparisons"]!.AsArray())
        {
            comparison!["workingPercentageGate"]!.GetValue<bool>().ShouldBeFalse();
            comparison["bindingPercentageGate"]!.GetValue<bool>().ShouldBeFalse();
            comparison["workingAfter"]!.GetValue<double>().ShouldBe(0);
            comparison["bindingAfter"]!.GetValue<double>().ShouldBe(0);
        }
    }

    private static JsonNode End(JsonObject evidence) => evidence["windows"]![0]!["end"]!;
    private static JsonNode Middle(JsonObject evidence) => evidence["windows"]![0]!["samples"]![3]!;
    private static IEnumerable<JsonNode> AllPoints(JsonObject evidence) => evidence["windows"]!.AsArray()
        .SelectMany(w => new[] { w!["start"]!, w["end"]! }.Concat(w["samples"]!.AsArray().Select(p => p!)));
    private static string ScriptPath => Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "measure-session-state-cache.ps1");
    private static string PsQuote(string value) => value.Replace("'", "''");

    private static Task<(int ExitCode, string Output)> RunFunctionsAsync(string body) => RunAsync("-Command", $$"""
        $ErrorActionPreference = 'Stop'
        Set-StrictMode -Version Latest
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{PsQuote(ScriptPath)}}', [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw 'Collector parse failed' }
        foreach ($definition in $ast.EndBlock.Statements) {
            if ($definition -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }
        }
        {{body}}
        """);

    private static async Task<JsonObject> InconclusiveAsync(JsonObject before, JsonObject after, string reason)
    {
        var result = await CompareAsync(before, after);
        result.ExitCode.ShouldBe(1, result.Output);
        result.Summary.ShouldNotBeNull(result.Output);
        result.Summary["attributionValid"]!.GetValue<bool>().ShouldBeFalse(reason);
        result.Summary["attributionReasons"]!.AsArray().Select(x => x!.GetValue<string>()).ShouldContain(reason);
        result.Summary["queryAndWorkloadGates"]!.GetValue<bool>().ShouldBeFalse();
        return result.Summary;
    }

    private static async Task RefusedAsync(JsonObject before, JsonObject after, string diagnostic)
    {
        var result = await CompareAsync(before, after);
        result.ExitCode.ShouldNotBe(0, result.Output);
        result.Output.ShouldContain(diagnostic);
        result.Summary.ShouldBeNull("invalid raw evidence must not yield a successful comparison artifact");
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
                ["samples"] = new JsonArray(Enumerable.Range(0, 7).Select(n => (JsonNode)Snapshot(n * 30, after, reset, workload)).ToArray())
            });
        }
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["card"] = "CARD-0701", ["phase"] = after ? "After" : "Before",
            ["round"] = after ? "R3" : "R1", ["canonicalSha"] = new string(after ? 'b' : 'a', 40),
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
