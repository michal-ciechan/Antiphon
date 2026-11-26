using System.IO.Abstractions;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class OperatorStatusTests
{
    private const string Project = "acacacac-1111-2222-3333-444444444444";
    private const string Code = "12345678-1111-2222-3333-444444444444";
    private const string Merge = "87654321-1111-2222-3333-444444444444";
    private const string Pin = "0123456789abcdef0123456789abcdef01234567";
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Exact_eta_matches_finished_chunks_and_lists_failures_and_unknowns()
    {
        using var world = new Fixture();
        // Only the shared finished chunk calibrates the ratio: 30 / 20. The 40-second
        // unfinished chunk takes 60 seconds. The 900-second removed chunk takes zero.
        world.Trx(world.Previous, "suite-a", 20);
        world.Trx(world.Previous, "suite-b", 40);
        world.Trx(world.Previous, "suite-removed", 900);
        world.Trx(world.Live, "suite-a", 30, "Fixture.Failed");
        world.Trx(world.Live, "suite-newDone", 70);

        var (exit, report) = world.Run();
        var rc = report["rc"]!;
        var eta = rc["eta"];
        eta.ShouldNotBeNull("the status command must project elapsed TRX timings");
        ((double)eta["ratio"]!).ShouldBe(1.5);
        ((double)eta["projectedRemainingSeconds"]!).ShouldBe(60);
        ((string)eta["projectedFinishUtc"]!).ShouldBe("2026-11-26T12:01:00.0000000+00:00");
        ((int)eta["unknownRemaining"]!).ShouldBe(1);
        ((int)eta["finishedWithoutBaseline"]!).ShouldBe(1);
        ((string)eta["biggestRemaining"]![0]!["chunk"]!).ShouldBe("suite-b");
        ((string)rc["failures"]![0]!["testName"]!).ShouldBe("Fixture.Failed");
        ((int)rc["trxCount"]!).ShouldBe(2);
        ((int)rc["scheduledEntries"]!).ShouldBe(4);
        ((string)rc["candidateId"]!).ShouldBe("rc-2026-12-06-0123456789ab");
        ((string)rc["pin"]!).ShouldBe(Pin);
        ((double)rc["elapsedSeconds"]!).ShouldBe(3600);
        ((bool)rc["heartbeat"]!["hangSuspected"]!).ShouldBeFalse();
        exit.ShouldBe(0);
    }

    [Test]
    public void Missing_previous_run_and_invalid_heartbeat_keep_independent_facts()
    {
        using var world = new Fixture();
        world.Trx(world.Live, "suite-a", 30);
        File.WriteAllText(world.Heartbeat, "{incomplete");
        world.Run().ShouldSatisfyPartial();
    }

    [Test]
    public void Status_uses_typed_handoff_and_exposes_fleet_facts_even_without_rc_sources()
    {
        using var world = new Fixture();
        var (exit, report) = world.Run(includeRc: false);
        exit.ShouldBe(2);
        var tasks = report["tasks"]!.AsArray();
        var blocked = tasks.Single(task => (string?)task!["shortId"] == "12345678")!;
        ((string)blocked["reason"]!).ShouldBe("first line");
        ((string)blocked["nextStage"]!).ShouldBe("Review");
        ((string)blocked["handoff"]!).ShouldBe("review the candidate");
        ((string)blocked["runner"]!).ShouldBe("fixture-host");
        ((string)report["mergeInFlight"]![0]!["shortId"]!).ShouldBe("87654321");
        ((string)report["landQueue"]![0]!["shortId"]!).ShouldBe("12345678");
        ((int)report["runnerSlots"]!["fixture-host"]!["occupied"]!).ShouldBe(1);
        ((string)report["master"]!["tip"]!).ShouldBe(Pin);
        report["errors"]!.AsArray().Select(error => (string?)error).ShouldContain(error => error!.Contains("rc", StringComparison.OrdinalIgnoreCase));
        world.Requests.ShouldAllBe(request => request.StartsWith("GET ", StringComparison.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly string _state;
        public readonly string Previous;
        public readonly string Live;
        public readonly string Heartbeat;
        public readonly List<string> Requests = [];

        public Fixture()
        {
            _root = Directory.CreateTempSubdirectory("c2087-status-").FullName;
            Previous = Path.Combine(_root, "candidates", "rc-2026-12-05-abcdefabcdef", "logs", "2026-11-25-1528-previous");
            Live = Path.Combine(_root, "candidates", "rc-2026-12-06-0123456789ab", "logs", "2026-11-26-1100-live");
            _state = Path.Combine(_root, "state-live");
            Heartbeat = Path.Combine(_root, "task-live", "supervisor-heartbeat.json");
            Directory.CreateDirectory(Live);
            Directory.CreateDirectory(_state);
            Directory.CreateDirectory(Path.GetDirectoryName(Heartbeat)!);
            File.WriteAllText(Path.Combine(Live, "dispatch-schedule.json"),
                """{"runId":"2026-11-26-1100-live","entries":[{"suite":"suite","chunk":"a"},{"suite":"suite","chunk":"b"},{"suite":"suite","chunk":"newDone"},{"suite":"suite","chunk":"newWaiting"}]}""");
            File.WriteAllText(Path.Combine(Live, "active-chunk.json"), """{"chunk":"b"}""");
            File.WriteAllText(Path.Combine(_state, "last-run.json"), $$"""
                {"runId":"2026-11-26-1100-live","lane":"rc","profile":"Full","candidateId":"rc-2026-12-06-0123456789ab","expectedSha":"{{Pin}}","phase":"tests","startedAt":"2026-11-26T11:00:00Z"}
                """);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Heartbeat)!, "supervisor-identity.json"), """{"runId":"2026-11-26-1100-live"}""");
            File.WriteAllText(Heartbeat, """{"observedAtUtc":"2026-11-26T11:59:00Z","idleMinutes":1,"hangSuspected":false}""");
        }

        public void Trx(string directory, string name, int seconds, string? failure = null)
        {
            Directory.CreateDirectory(directory);
            var result = failure is null ? "<UnitTestResult outcome=\"Passed\" testName=\"Fixture.Passed\"/>"
                : $"""<UnitTestResult outcome="Failed" testName="{failure}"/>""";
            File.WriteAllText(Path.Combine(directory, name + ".trx"), $"""
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Times finish="{Now.AddSeconds(seconds):O}" start="{Now:O}" />
                  <Results>{result}</Results>
                </TestRun>
                """);
        }

        public (int Exit, JsonObject Report) Run(bool includeRc = true)
        {
            using var output = new StringWriter();
            var host = QualificationEtaTests.Host(output, output, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
            var args = new List<string> { "--json", "--project-id", Project };
            if (includeRc) args.AddRange(["--rc-run-log-dir", Live, "--rc-state-dir", _state, "--rc-heartbeat-path", Heartbeat]);
            var options = StatusSnapshotOptions.Parse(args, "http://fixture", "synthetic-token", _root);
            var exit = StatusSnapshot.Run(options, host, new FileSystem(), Get, (_, gitArgs) =>
            {
                gitArgs.ShouldContain(arg => arg is "ls-remote" or "log");
                return new NightlyGitResult(0, gitArgs.Contains("ls-remote") ? Pin + "\trefs/heads/master" : Pin + "\tfixture");
            });
            return (exit, JsonNode.Parse(output.ToString())!.AsObject());
        }

        private NightlyReportHttpResponse Get(string method, string url, IReadOnlyDictionary<string, string> headers, string? body)
        {
            var path = new Uri(url).AbsolutePath;
            Requests.Add(method + " " + path);
            return path switch
            {
                "/api/agent-tasks/pipeline" => new(200, $$"""
                    {"stages":[
                        {"role":"Code","inFlight":[],"queued":[],"blocked":[{"taskId":"{{Code}}","shortId":"12345678","title":"Fixture code","card":{"identifier":"CARD-2087"}}],"ready":[]},
                        {"role":"Merge","inFlight":[{"taskId":"{{Merge}}","shortId":"87654321","title":"Fixture merge","status":"Working"}],"queued":[],"blocked":[],"ready":[]}
                    ],"concurrencyScopes":[],"hosts":[{"hostId":"fixture-host","effectiveLimit":3,"inFlight":1,"remaining":2}],
                    "runners":[{"runnerId":"fixture-host"}]}
                    """),
                "/api/agent-tasks" => new(200, $$"""
                    {"items":[{"id":"{{Code}}","role":"Code","status":"Blocked","runnerId":"fixture-host","landRequestedAt":"2026-11-26T11:00:00Z"}]}
                    """),
                "/api/agent-tasks/" + Code => new(200, $$"""
                    {"summary":{"id":"{{Code}}","role":"Code","status":"Blocked","runnerId":"fixture-host","title":"Fixture code","cardIdentifier":"CARD-2087"},
                    "failureReason":"first line\nsecond line","nextStage":"Review","nextHandoff":"review the candidate",
                    "result":"next: invalid","landRequest":{"state":"Queued","queuePosition":1}}
                    """),
                "/api/agent-tasks/" + Merge => new(200, $$"""
                    {"summary":{"id":"{{Merge}}","role":"Merge","status":"Working","runnerId":"fixture-host","title":"Fixture merge"}}
                    """),
                "/api/orchestrator-instructions" => new(200, """{"content":"fixture"}"""),
                "/api/session-runners/fixture-host/slots" => new(200, """{"occupied":1,"declaredCapacity":3,"slots":[]}"""),
                "/api/attention" => new(200, """{"items":[]}"""),
                _ => new(404, "{}"),
            };
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}

file static class OperatorStatusAssertions
{
    public static void ShouldSatisfyPartial(this (int Exit, JsonObject Report) run)
    {
        run.Exit.ShouldBe(2);
        var rc = run.Report["rc"]!;
        rc["eta"]!.AsObject().ContainsKey("projectedFinishUtc").ShouldBeTrue();
        rc["eta"]!["projectedFinishUtc"].ShouldBeNull();
        ((int)rc["trxCount"]!).ShouldBe(1);
        ((string)run.Report["master"]!["tip"]!).ShouldNotBeNullOrEmpty();
        var errors = run.Report["errors"]!.AsArray().Select(error => (string?)error).ToArray();
        errors.ShouldContain(error => error!.Contains("Previous rc run", StringComparison.Ordinal));
        errors.ShouldContain(error => error!.Contains("supervisor-heartbeat.json", StringComparison.Ordinal));
    }
}
