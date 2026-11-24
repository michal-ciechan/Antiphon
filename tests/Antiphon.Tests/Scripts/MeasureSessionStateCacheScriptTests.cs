using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0701: the session-state cache measurement (the measure-session-state-cache command, Antiphon.Nightly's
/// SessionStateCacheMeasure), in process since unit 33 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md (it ran
/// scripts/measure-session-state-cache.ps1 under pwsh). Compare runs offline on the evidence files; capture runs on a fake
/// API, docker and git (the host's launcher) and a fake clock, never the real server, PostgreSQL or session runtime.
/// </summary>
[Category("Integration")]
[Category("Slow")]
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
        // Old servers supply runtime observations in the context file. The production snapshot builder reads it again at
        // every sample (the pre-feature 404 falls back to it), so startup facts cannot hide a dip.
        var path = Path.Combine(Path.GetTempPath(), "c701-context-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var context = Context();
            context["runtime"] = new JsonObject { ["processId"] = 123, ["liveSessions"] = 9 };
            await File.WriteAllTextAsync(path, context.ToJsonString());
            var world = new CaptureWorld();
            world.Answers["/api/diagnostics/session-state"] = new NightlyReportHttpResponse(404, "");
            var snapshot = SessionStateCacheMeasure.Snapshot(world.Options(path, phase: "Before"), "Before", world.Host(), new System.IO.Abstractions.FileSystem(), world.Seams());
            snapshot["runtime"]!["liveSessions"]!.GetValue<int>().ShouldBe(9,
                "each pre-feature observation must reload current context instead of retaining startup population");
            snapshot["runtime"]!["processStartedAt"]!.GetValue<string>().ShouldBe("2026-10-03T00:00:00.0000000Z", "the live process refreshes the context runtime");
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
            var world = new CaptureWorld();
            var exit = SessionStateCacheMeasure.Run(new SessionStateCacheOptions { Mode = "Acceptance", EvidenceRoot = root }, world.Host(),
                new System.IO.Abstractions.FileSystem(), world.Seams());
            exit.ShouldNotBe(0);
            world.Output.ToString().ShouldContain("requires a previously saved baseline");
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
            world.Requests.ShouldBeEmpty("no evidence means no observation");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Baseline_files_are_immutable()
    {
        var path = Path.Combine(Path.GetTempPath(), "c701-immutable-" + Guid.NewGuid().ToString("N") + ".json");
        var files = new System.IO.Abstractions.FileSystem();
        try
        {
            SessionStateCacheMeasure.SaveImmutable(files, path, new JsonObject { ["original"] = 701 });
            var original = File.ReadAllBytes(path);
            Should.Throw<IOException>(() => SessionStateCacheMeasure.SaveImmutable(files, path, new JsonObject { ["replacement"] = 999 }),
                "CreateNew must reject overwriting immutable evidence");
            File.ReadAllBytes(path).ShouldBe(original);
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
        await using var command = new Npgsql.NpgsqlCommand(inputs + SessionStateCacheMeasure.StatisticsSql, connection);
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

    // A Before capture on fakes: the warm-up and both windows wait on the host's clock, every sample is written once, the
    // baseline is the capture, the statistics read is the production SQL through docker exec, and the token stays a header.
    [Test]
    public void Before_capture_records_two_sampled_windows_and_the_baseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "c701-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, Context().ToJsonString());
            var world = new CaptureWorld();
            var evidence = Path.Combine(root, "evidence");
            var exit = SessionStateCacheMeasure.Run(world.Options(contextPath, phase: "Before", evidenceRoot: evidence), world.Host(), new System.IO.Abstractions.FileSystem(), world.Seams());
            exit.ShouldBe(0, world.Output.ToString());
            var destination = Directory.GetDirectories(evidence, "before-*").ShouldHaveSingleItem();
            Directory.GetFiles(destination, "window-1-*.json").Length.ShouldBe(7, "a start and six 30 s samples");
            Directory.GetFiles(destination, "window-2-*.json").Length.ShouldBe(7);
            var capture = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "capture.json")))!;
            JsonNode.Parse(File.ReadAllText(Path.Combine(evidence, "baseline.json")))!.ToJsonString().ShouldBe(capture.ToJsonString());
            capture["phase"]!.GetValue<string>().ShouldBe("Before");
            capture["canonicalSha"]!.GetValue<string>().ShouldBe(CaptureWorld.Head);
            capture["windows"]!.AsArray().Count.ShouldBe(2);
            capture["windows"]![0]!["samples"]!.AsArray().Count.ShouldBe(7);
            var output = world.Output.ToString();
            output.ShouldContain("Warm-up - 60 seconds remaining (read-only; use natural workload).");
            output.ShouldContain("Observation window 2 - 30 seconds remaining");
            output.ShouldContain("Capture saved: " + destination);
            world.Clock.GetUtcNow().ShouldBe(CaptureWorld.Start.AddSeconds(60 + 180 + 180));
            var psql = world.Launcher.Launches.Where(l => l.FileName == "docker" && l.Arguments.Contains("psql")).ToList();
            psql.Count.ShouldBe(14);
            psql.ShouldAllBe(l => l.Arguments.Last() == SessionStateCacheMeasure.StatisticsSql && l.CaptureOutput && l.TimeoutMilliseconds == 30000);
            world.Requests.ShouldAllBe(r => r.Token == CaptureWorld.Token);
            output.ShouldNotContain(CaptureWorld.Token);
            Directory.GetFiles(evidence, "*", SearchOption.AllDirectories).ShouldAllBe(f => !File.ReadAllText(f).Contains(CaptureWorld.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Capture_refusals_observe_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "c701-refusal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, Context().ToJsonString());
            (string, Action<CaptureWorld>, SessionStateCacheOptions)[] cases =
            [
                ("requires the canonical desktop lane", w => w.IsWindows = false, new CaptureWorld().Options(contextPath, evidenceRoot: Path.Combine(root, "a"))),
                ("requires a sanitized context JSON", _ => { }, new CaptureWorld().Options(Path.Combine(root, "none.json"), evidenceRoot: Path.Combine(root, "b"))),
                ("does not equal canonical checkout HEAD", w => w.Version = new string('c', 40), new CaptureWorld().Options(contextPath, phase: "After", evidenceRoot: Path.Combine(root, "c"))),
            ];
            foreach (var (message, arrange, options) in cases)
            {
                var world = new CaptureWorld();
                arrange(world);
                SessionStateCacheMeasure.Run(options, world.Host(), new System.IO.Abstractions.FileSystem(), world.Seams()).ShouldBe(1, message);
                world.Output.ToString().ShouldContain(message);
                Directory.GetFiles(options.EvidenceRoot, "*", SearchOption.AllDirectories).ShouldBeEmpty(message);
                world.Launcher.Launches.ShouldNotContain(l => l.FileName == "docker", message);
                world.Clock.GetUtcNow().ShouldBe(CaptureWorld.Start, "no warm-up for " + message);
            }
            Directory.CreateDirectory(Path.Combine(root, "d"));
            File.WriteAllText(Path.Combine(root, "d", "baseline.json"), "{}");
            var again = new CaptureWorld();
            SessionStateCacheMeasure.Run(again.Options(contextPath, phase: "Before", evidenceRoot: Path.Combine(root, "d")), again.Host(),
                new System.IO.Abstractions.FileSystem(), again.Seams()).ShouldBe(1);
            again.Output.ToString().ShouldContain("Baseline already exists");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Usage_errors_are_the_command_line_only()
    {
        Should.Throw<NightlyUsageException>(() => SessionStateCacheOptions.Parse([], null, null, null)).Message.ShouldContain("--evidence-root");
        Should.Throw<NightlyUsageException>(() => SessionStateCacheOptions.Parse(["--evidence-root", "e", "--mode", "Replay"], null, null, null));
        Should.Throw<NightlyUsageException>(() => SessionStateCacheOptions.Parse(["--evidence-root", "e", "--warm-seconds", "59"], null, null, null));
        Should.Throw<NightlyUsageException>(() => SessionStateCacheOptions.Parse(["--evidence-root", "e", "--window-seconds", "3601"], null, null, null));
        var parsed = SessionStateCacheOptions.Parse(["--evidence-root", "e", "--mode", "compare", "--round", "r1"], "http://api.test/", "t", "repo");
        (parsed.Mode, parsed.Round, parsed.Phase, parsed.Api, parsed.Baseline).ShouldBe(("Compare", "R1", "After", "http://api.test", Path.Combine("e", "baseline.json")));
    }

    // ---- fakes ---------------------------------------------------------------------------------------------------------------

    /// <summary>A fake desktop: the API, docker and git answers, a process sample, and a host whose waits advance its clock.</summary>
    private sealed class CaptureWorld
    {
        public const string Token = "c701-task-token";
        public static readonly string Head = new('b', 40);
        public static readonly DateTimeOffset Start = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

        public FakeTimeProvider Clock { get; } = new(Start);
        public StringWriter Output { get; } = new();
        public RecordingLauncher Launcher { get; }
        public List<(string Uri, string? Token)> Requests { get; } = [];
        public Dictionary<string, NightlyReportHttpResponse> Answers { get; } = new(StringComparer.Ordinal);
        public bool IsWindows { get; set; } = true;
        public string Version { get; set; } = Head;

        public CaptureWorld()
        {
            Launcher = new RecordingLauncher(launch =>
            {
                var args = string.Join(" ", launch.Arguments);
                var stdout = launch.FileName == "git"
                    ? (args.Contains("worktree list") ? "worktree C:/canonical\nHEAD " + Head + "\nbranch refs/heads/master\n" : Head + "\n")
                    : args.Contains("psql") ? Statistics()
                    : args.Contains("inspect") ? "container-one|2026-10-03\n"
                    : "usage_usec " + (100_000_000 + (long)(Clock.GetUtcNow() - Start).TotalSeconds * 1000) + "\n";
                return new NightlyLaunchResult { ExitCode = 0, StandardOutputText = stdout };
            });
        }

        public NightlyHost Host() => new()
        {
            Clock = Clock,
            Git = new FakeGit().Run,
            Launcher = Launcher,
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = Output,
            Error = Output,
            Trace = TextWriter.Null,
            Sleep = Clock.Advance,
        };

        public SessionStateCacheSeams Seams() => new()
        {
            Http = (method, uri, headers, body) =>
            {
                Requests.Add((uri, headers.TryGetValue("X-Antiphon-Task-Token", out var token) ? token : null));
                var path = new Uri(uri).AbsolutePath;
                if (Answers.TryGetValue(path, out var answer)) return answer;
                return path switch
                {
                    "/api/version" => new(200, new JsonObject { ["version"] = Version }.ToJsonString()),
                    "/api/diagnostics/session-state" => new(200, Runtime().ToJsonString()),
                    _ => new(404, ""),
                };
            },
            Process = pid => new SessionStateProcessSample(42, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), 1024),
            IsWindows = IsWindows,
        };

        public SessionStateCacheOptions Options(string contextPath, string phase = "Before", string? evidenceRoot = null) => new()
        {
            Mode = "Capture",
            Phase = phase,
            Round = "R1",
            EvidenceRoot = evidenceRoot ?? Path.Combine(Path.GetTempPath(), "c701-unused-" + Guid.NewGuid().ToString("N")),
            ContextPath = contextPath,
            Api = "http://api.test",
            TaskToken = Token,
            PostgresContainer = "fixture-postgres",
            RepoRoot = "C:/checkout",
        };

        private JsonObject Runtime() => new()
        {
            ["processId"] = 123, ["processStartedAt"] = "2026-10-03T00:00:00Z", ["cpuSeconds"] = 100 + (Clock.GetUtcNow() - Start).TotalSeconds * 0.5,
            ["liveSessions"] = 12, ["unknownSessions"] = 0, ["driverVersion"] = "9.0.4", ["providerVersion"] = "9.0.4",
            ["pool"] = new JsonObject { ["pooling"] = true, ["noResetOnClose"] = true, ["maxAutoPrepare"] = 0, ["multiplexing"] = false },
        };

        private static string Statistics() => new JsonObject
        {
            ["postmasterStartedAt"] = "2026-10-03T00:00:00Z", ["databaseOid"] = 1,
            ["info"] = new JsonObject { ["stats_reset"] = "2026-10-03", ["dealloc"] = 0 },
            ["statements"] = new JsonArray(new JsonObject
            {
                ["dbid"] = 1, ["userid"] = 1, ["queryid"] = "working", ["toplevel"] = true, ["calls"] = 100, ["rows"] = 100, ["total_exec_time"] = 100.0,
                ["fingerprint"] = "working", ["shape"] = "working", ["transcript_select"] = true,
            }),
        }.ToJsonString();
    }

    // ---- evidence (the PowerShell tests' builders, unchanged) -----------------------------------------------------------------

    private static JsonNode End(JsonObject evidence) => evidence["windows"]![0]!["end"]!;
    private static JsonNode Middle(JsonObject evidence) => evidence["windows"]![0]!["samples"]![3]!;
    private static IEnumerable<JsonNode> AllPoints(JsonObject evidence) => evidence["windows"]!.AsArray()
        .SelectMany(w => new[] { w!["start"]!, w["end"]! }.Concat(w["samples"]!.AsArray().Select(p => p!)));

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

    // Compare offline, in process: the baseline and after files are read, never changed, and a compare-*.json summary is
    // written only when the evidence could be compared. The seams answer nothing: Compare observes nothing.
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
            var world = new CaptureWorld();
            var seams = new SessionStateCacheSeams
            {
                Http = (_, uri, _, _) => throw new InvalidOperationException("Compare made a request: " + uri),
                Process = pid => throw new InvalidOperationException("Compare read a process"),
                IsWindows = false,
            };
            var exit = SessionStateCacheMeasure.Run(new SessionStateCacheOptions { Mode = "Compare", Round = "R3", EvidenceRoot = root, BaselinePath = baseline, AfterPath = afterPath },
                world.Host(), new System.IO.Abstractions.FileSystem(), seams);
            (await File.ReadAllTextAsync(baseline)).ShouldBe(beforeText, "comparison must preserve baseline bytes");
            (await File.ReadAllTextAsync(afterPath)).ShouldBe(afterText, "comparison must preserve after bytes");
            world.Launcher.Launches.ShouldBeEmpty("Compare runs no tool");
            var summary = Directory.GetFiles(root, "compare-*.json").SingleOrDefault();
            return (exit, world.Output.ToString(), summary is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(summary))!.AsObject());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
