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
    private const string Repair = "bcdefa12-1111-2222-3333-444444444444";
    private const string Pin = "0123456789abcdef0123456789abcdef01234567";
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Exact_eta_matches_finished_chunks_and_lists_failures_and_unknowns()
    {
        using var world = new Fixture();
        // The shared finished chunk's ratio of 1.5 gets 1/21 of the weight. The
        // 40-second unfinished chunk takes 40.952 seconds. The removed chunk takes zero.
        world.Trx(world.Previous, "suite-a", 20);
        world.Trx(world.Previous, "suite-b", 40);
        world.Trx(world.Previous, "suite-removed", 900);
        world.Trx(world.Live, "suite-a", 30, "Fixture.Failed");
        world.Trx(world.Live, "suite-newDone", 70);

        var (exit, report) = world.Run();
        var rc = report["rc"]!;
        var eta = rc["eta"];
        eta.ShouldNotBeNull("the status command must project elapsed TRX timings");
        ((double)eta["ratio"]!).ShouldBe(1.0238095238095237);
        ((double)eta["projectedRemainingSeconds"]!).ShouldBe(40.952);
        ((string)eta["projectedFinishUtc"]!).ShouldBe("2026-11-26T12:00:40.9520000+00:00");
        ((string)eta["confidence"]!).ShouldBe("provisional");
        eta["p95FinishUtc"].ShouldBeNull();
        ((int)eta["unknownRemaining"]!).ShouldBe(1);
        // CARD-2087 D-2: unknown remaining work makes the projection a flagged lower bound.
        ((bool)eta["lowerBound"]!).ShouldBeTrue();
        eta["unknownRemainingChunks"]!.AsArray().Select(chunk => (string?)chunk).ShouldBe(["suite-newWaiting"]);
        ((int)eta["finishedWithoutBaseline"]!).ShouldBe(1);
        ((string)eta["biggestRemaining"]![0]!["chunk"]!).ShouldBe("suite-b");
        ((string)rc["failures"]![0]!["testName"]!).ShouldBe("Fixture.Failed");
        var fix = rc["failures"]![0]!["fixes"]![0]!;
        ((string)fix["shortId"]!).ShouldBe("bcdefa12");
        ((string)fix["landingPublication"]!).ShouldBe("Landed");
        ((string)fix["landingRef"]!).ShouldBe("refs/heads/master");
        ((int)rc["trxCount"]!).ShouldBe(2);
        ((int)rc["scheduledEntries"]!).ShouldBe(4);
        ((string)rc["candidateId"]!).ShouldBe("rc-2026-12-06-0123456789ab");
        ((string)rc["pin"]!).ShouldBe(Pin);
        ((double)rc["elapsedSeconds"]!).ShouldBe(3600);
        ((bool)rc["heartbeat"]!["hangSuspected"]!).ShouldBeFalse();
        report["errors"]!.AsArray().Select(error => (string?)error).ShouldHaveSingleItem()
            .ShouldBe("rc ETA is a lower bound: no previous timing for 1 remaining chunk(s): suite-newWaiting.");
        exit.ShouldBe(2);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = QualificationEtaTests.Host(output, error, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
        QualificationEta.Run(QualificationEtaOptions.Parse(["--timing-root", world.Timing]), host, new FileSystem()).ShouldBe(0, error.ToString());
        output.ToString().Trim().Split(Environment.NewLine).ShouldBe([
            "ETA outstanding=1 running=1 completed=2 unpriced=1 pointSeconds=40.952 p50=2026-11-26T12:00:40.952Z p95=unavailable confidence=provisional lowerBound=true",
            "UNVERIFIED rc ETA is a lower bound: no previous timing for 1 remaining chunk(s): suite-newWaiting.",
        ]);
    }

    [Test]
    public void Stopped_partial_attempt_is_not_the_eta_baseline()
    {
        // CARD-2087 D-2: a newer stopped attempt with one TRX of its four scheduled chunks
        // must not replace the complete earlier run as the baseline.
        using var world = new Fixture();
        world.CompletePrevious();
        world.Trx(world.Stopped, "suite-a", 2);
        world.ScheduleOnly(world.Stopped, "suite-b", "suite-newDone", "suite-newWaiting");
        world.Trx(world.Live, "suite-a", 30);
        var (exit, report) = world.Run(autoDiscover: true);
        var eta = report["rc"]!["eta"]!;
        ((string)eta["previousRunLogDir"]!).ShouldBe(world.Previous);
        ((double)eta["ratio"]!).ShouldBe(1.0238095238095237);
        ((double)eta["projectedRemainingSeconds"]!).ShouldBe(61.429);
        ((bool)eta["lowerBound"]!).ShouldBeFalse();
        report["errors"]!.AsArray().ShouldBeEmpty();
        exit.ShouldBe(0);
    }

    [Test]
    public void Live_trx_without_a_TestRun_root_is_unverified_not_a_crash()
    {
        // CARD-2087 D-1: a well-formed XML file that is not a TeamTest run.
        using var world = new Fixture();
        world.CompletePrevious();
        File.WriteAllText(Path.Combine(world.Live, "suite-a.trx"), "<NotATestRun/>");
        var (exit, report) = world.Run();
        exit.ShouldBe(2);
        var rc = report["rc"]!;
        ((int)rc["trxCount"]!).ShouldBe(1);
        ((int)rc["finishedChunks"]!).ShouldBe(0);
        report["errors"]!.AsArray().Select(error => (string?)error)
            .ShouldContain("rc live TRX suite-a.trx unreadable or incomplete; its failures and duration are unverified.");
    }

    [Test]
    public void Json_shape_keeps_every_documented_key_when_sources_fail()
    {
        // CARD-2087 D-3: the --json key set is the same on a healthy and a failed observation.
        using var world = new Fixture();
        world.CompletePrevious();
        world.Trx(world.Live, "suite-a", 30);
        var (_, healthy) = world.Run();
        world.Override = _ => new NightlyReportHttpResponse(503, "{}");
        var (exit, failed) = world.Run(includeRc: false);
        exit.ShouldBe(2);
        foreach (var report in new[] { healthy, failed })
        {
            report.Select(pair => pair.Key).ShouldBe(TopKeys, ignoreOrder: true);
            report["master"]!.AsObject().Select(pair => pair.Key).ShouldBe(["tip", "cachedOriginMasterLog", "historyIsCached"], ignoreOrder: true);
            report["rc"]!.AsObject().Select(pair => pair.Key).ShouldBe(RcKeys, ignoreOrder: true);
            report["rc"]!["eta"]!.AsObject().Select(pair => pair.Key).ShouldBe(EtaKeys, ignoreOrder: true);
        }
        failed["landQueue"].ShouldBeNull();
        failed["cardsAtRisk"].ShouldBeNull();
        failed["rc"]!["failures"].ShouldBeNull();
    }

    [Test]
    public void Slow_sources_are_bounded_and_task_details_are_read_concurrently()
    {
        // CARD-2087 D-5: a hung attention endpoint costs its own short deadline, and two task
        // details only answer when both requests are in flight at once.
        using var world = new Fixture();
        using var rendezvous = new CountdownEvent(2);
        world.Override = path =>
        {
            if (path == "/api/attention")
            {
                world.Release.Wait(TimeSpan.FromSeconds(20));
                return new NightlyReportHttpResponse(200, """{"items":[]}""");
            }
            if (path != "/api/agent-tasks/" + Code && path != "/api/agent-tasks/" + Merge) return null;
            rendezvous.Signal();
            return rendezvous.Wait(TimeSpan.FromSeconds(10)) ? null : new NightlyReportHttpResponse(503, "{}");
        };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (exit, report) = world.Run(includeRc: false, tune: options =>
        {
            options.SlowSourceTimeout = TimeSpan.FromSeconds(1);
            options.SourceTimeout = TimeSpan.FromSeconds(15);
            options.StatusBudget = TimeSpan.FromSeconds(30);
        });
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(12));
        exit.ShouldBe(2);
        var errors = report["errors"]!.AsArray().Select(error => (string?)error).ToArray();
        errors.ShouldContain("GET /api/attention unavailable (no answer within 1 s).");
        errors.ShouldNotContain(error => error!.StartsWith("GET /api/agent-tasks/", StringComparison.Ordinal));
        ((string)report["tasks"]!.AsArray().Single(task => (string?)task!["shortId"] == "12345678")!["nextStage"]!).ShouldBe("Review");
        report["cardsAtRisk"].ShouldBeNull();
    }

    private static readonly string[] TopKeys =
    [
        "observedAtUtc", "projectId", "runner", "counts", "pipeline", "orchestratorInstructions", "master", "rcRefs", "rc",
        "taskScope", "tasks", "mergeInFlight", "fleetHosts", "fleetConcurrencyScopes", "runnerDefaults", "landQueue",
        "runnerCatalogue", "runnerSlots", "cardsAtRisk", "errors",
    ];

    private static readonly string[] RcKeys =
    [
        "candidateId", "pin", "runId", "phase", "elapsedSeconds", "lastRun", "runLogDir", "stateDir", "activeChunk",
        "heartbeat", "heartbeatAgeSeconds", "trxCount", "finishedChunks", "scheduledEntries", "failures", "eta",
    ];

    private static readonly string[] EtaKeys =
    [
        "status", "previousRunLogDir", "ratio", "matchedFinished", "knownRemaining", "unknownRemaining", "unknownRemainingChunks",
        "finishedWithoutBaseline", "baselineRemainingSeconds", "projectedRemainingSeconds", "projectedFinishUtc", "lowerBound",
        "p95FinishUtc", "confidence", "observedStartupSeconds", "startupOverheadNote", "biggestRemaining",
    ];

    [Test]
    public void Missing_previous_run_and_invalid_heartbeat_keep_independent_facts()
    {
        using var world = new Fixture();
        world.Trx(world.Live, "suite-a", 30);
        File.WriteAllText(world.Heartbeat, "{incomplete");
        world.Run().ShouldSatisfyPartial();
    }

    [Test]
    public void Release_layout_lookup_binds_the_state_and_heartbeat_to_the_full_run_id()
    {
        using var world = new Fixture();
        world.CompletePrevious();
        world.Trx(world.Live, "suite-a", 30);
        // CARD-2087 D-4: decoy state and supervisor records share the run id's 8-hex prefix
        // and carry a different heartbeat; a prefix match is ambiguous or binds the wrong age.
        world.Decoys();
        var (exit, report) = world.Run(autoDiscover: true);
        report["errors"]!.AsArray().ShouldBeEmpty();
        exit.ShouldBe(0);
        var rc = report["rc"]!;
        ((string)rc["runId"]!).ShouldBe("1fe763a9-04c0-4136-a9ec-46c833a411d0");
        ((string)rc["runLogDir"]!).ShouldBe(world.Live);
        ((double)rc["heartbeatAgeSeconds"]!).ShouldBe(60);
        ((string)rc["stateDir"]!).ShouldBe(world.State);
        ((double)rc["eta"]!["projectedRemainingSeconds"]!).ShouldBe(61.429);
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
        world.Snapshot().ShouldAllBe(request => request.StartsWith("GET ", StringComparison.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private const string RunId = "1fe763a9-04c0-4136-a9ec-46c833a411d0";
        private const string DecoyRunId = "1fe763a9-ffff-4136-a9ec-46c833a411d0";
        private readonly List<string> _requests = [];
        private readonly Dictionary<string, List<string>> _schedules = new(StringComparer.Ordinal);
        public readonly string State;
        public readonly string Previous;
        public readonly string Stopped;
        public readonly ManualResetEventSlim Release = new(false);
        public Func<string, NightlyReportHttpResponse?>? Override;
        public readonly string Live;
        public readonly string Heartbeat;
        public readonly string Timing;

        public Fixture()
        {
            _root = Directory.CreateTempSubdirectory("c2087-status-").FullName;
            Previous = Path.Combine(_root, "candidates", "rc-2026-12-05-abcdefabcdef", "logs", "2026-11-25-1528-previous");
            Stopped = Path.Combine(_root, "candidates", "rc-2026-12-05-abcdefabcdef", "logs", "2026-11-26-0900-stopped");
            Live = Path.Combine(_root, "candidates", "rc-2026-12-06-0123456789ab", "logs", "2026-11-26-1100-1fe763a9");
            State = Path.Combine(_root, "state-live");
            Heartbeat = Path.Combine(_root, "task-live", "supervisor-heartbeat.json");
            Directory.CreateDirectory(Live);
            Directory.CreateDirectory(State);
            Directory.CreateDirectory(Path.GetDirectoryName(Heartbeat)!);
            File.WriteAllText(Path.Combine(Live, "dispatch-schedule.json"),
                $$$"""{"runId":"{{{RunId}}}","entries":[{"suite":"suite","chunk":"a"},{"suite":"suite","chunk":"b"},{"suite":"suite","chunk":"newDone"},{"suite":"suite","chunk":"newWaiting"}]}""");
            Timing = Path.Combine(Path.GetDirectoryName(Live)!, "timing-" + RunId);
            Directory.CreateDirectory(Timing);
            File.WriteAllText(Path.Combine(Timing, "active-chunk.json"), $$$"""{"runId":"{{{RunId}}}","suite":"suite","chunk":"b","state":"Active"}""");
            File.WriteAllText(Path.Combine(State, "last-run.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                runId = RunId, lane = "rc", profile = "rc", candidateId = "rc-2026-12-06-0123456789ab", expectedSha = Pin,
                phase = "tests", startedAt = "2026-11-26T11:00:00Z", logDir = Live,
            }));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Heartbeat)!, "supervisor-identity.json"), $$$"""{"runId":"{{{RunId}}}"}""");
            File.WriteAllText(Heartbeat, """{"observedAtUtc":"2026-11-26T11:59:00Z","idleMinutes":1,"hangSuspected":false}""");
        }

        public string[] Snapshot()
        {
            lock (_requests) return [.. _requests];
        }

        // The previous run with every live-scheduled chunk except the removed one timed.
        public void CompletePrevious()
        {
            Trx(Previous, "suite-a", 20);
            Trx(Previous, "suite-b", 40);
            Trx(Previous, "suite-newDone", 10);
            Trx(Previous, "suite-newWaiting", 20 - 10);
        }

        // Scheduled in a historical run's dispatch-schedule.json without a TRX.
        public void ScheduleOnly(string directory, params string[] names)
        {
            foreach (var name in names) Schedule(directory, name);
        }

        public void Decoys()
        {
            var state = Path.Combine(_root, "state-decoy");
            var task = Path.Combine(_root, "task-decoy");
            Directory.CreateDirectory(state);
            Directory.CreateDirectory(task);
            File.WriteAllText(Path.Combine(state, "last-run.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                runId = DecoyRunId, lane = "rc", profile = "rc", candidateId = "rc-2026-12-06-0123456789ab", expectedSha = Pin,
                phase = "tests", startedAt = "2026-11-26T10:00:00Z", completedAt = "2026-11-26T10:30:00Z", exitCode = 1, logDir = Path.Combine(Path.GetDirectoryName(Live)!, "2026-11-26-1000-1fe763a9"),
            }));
            File.WriteAllText(Path.Combine(task, "supervisor-identity.json"), $$$"""{"runId":"{{{DecoyRunId}}}"}""");
            File.WriteAllText(Path.Combine(task, "supervisor-heartbeat.json"), """{"observedAtUtc":"2026-11-26T11:30:00Z","idleMinutes":30,"hangSuspected":true}""");
        }

        private void Schedule(string directory, string name)
        {
            Directory.CreateDirectory(directory);
            if (!_schedules.TryGetValue(directory, out var names)) _schedules[directory] = names = [];
            names.Add(name);
            var entries = string.Join(",", names.Select(entry => $$$"""{"suite":"suite","chunk":"{{{entry["suite-".Length..]}}}"}"""));
            File.WriteAllText(Path.Combine(directory, "dispatch-schedule.json"), $$$"""{"runId":"previous","preparationComplete":true,"entries":[{{{entries}}}]}""");
        }

        public void Trx(string directory, string name, int seconds, string? failure = null)
        {
            Directory.CreateDirectory(directory);
            if (directory != Live) Schedule(directory, name);
            var result = failure is null ? "<UnitTestResult outcome=\"Passed\" testName=\"Fixture.Passed\"/>"
                : $"""<UnitTestResult outcome="Failed" testName="{failure}"/>""";
            File.WriteAllText(Path.Combine(directory, name + ".trx"), $"""
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Times finish="{Now.AddMinutes(-59).AddSeconds(seconds):O}" start="{Now.AddMinutes(-59):O}" />
                  <Results>{result}</Results>
                </TestRun>
                """);
        }

        public (int Exit, JsonObject Report) Run(bool includeRc = true, bool autoDiscover = false, Action<StatusSnapshotOptions>? tune = null)
        {
            using var output = new StringWriter();
            var host = QualificationEtaTests.Host(output, output, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
            var args = new List<string> { "--json", "--project-id", Project };
            if (autoDiscover || !includeRc) args.AddRange(["--rc-release-root", autoDiscover ? _root : Path.Combine(_root, "absent")]);
            else args.AddRange(["--rc-run-log-dir", Live, "--rc-state-dir", State, "--rc-heartbeat-path", Heartbeat]);
            var options = StatusSnapshotOptions.Parse(args, "http://fixture", "synthetic-token", _root);
            tune?.Invoke(options);
            var exit = StatusSnapshot.Run(options, host, new FileSystem(), Get, (_, gitArgs) =>
            {
                gitArgs.ShouldContain(arg => arg == "ls-remote" || arg == "log");
                return new NightlyGitResult(0, gitArgs.Contains("ls-remote") ? Pin + "\trefs/heads/master" : Pin + "\tfixture");
            });
            return (exit, JsonNode.Parse(output.ToString())!.AsObject());
        }

        private NightlyReportHttpResponse Get(string method, string url, IReadOnlyDictionary<string, string> headers, string? body)
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            lock (_requests) _requests.Add(method + " " + path);
            if (Override?.Invoke(uri.PathAndQuery) is { } replaced) return replaced;
            return path switch
            {
                "/api/agent-tasks/pipeline" => new(200, $$$"""
                    {"stages":[
                        {"role":"Code","inFlight":[],"queued":[],"blocked":[{"taskId":"{{{Code}}}","shortId":"12345678","title":"Fixture code","card":{"identifier":"CARD-2087"}}],"ready":[]},
                        {"role":"Merge","inFlight":[{"taskId":"{{{Merge}}}","shortId":"87654321","title":"Fixture merge","status":"Working"}],"queued":[],"blocked":[],"ready":[]}
                    ],"concurrencyScopes":[],"hosts":[{"hostId":"fixture-host","effectiveLimit":3,"inFlight":1,"remaining":2}],
                    "runners":[{"runnerId":"fixture-host"}]}
                    """),
                "/api/agent-tasks" when uri.Query == "?landPending=true" => new(200, $$$"""
                    {"items":[{"id":"{{{Code}}}","role":"Code","status":"Blocked","runnerId":"fixture-host","landRequestedAt":"2026-11-26T11:00:00Z"}]}
                    """),
                "/api/agent-tasks" when uri.Query.StartsWith("?since=", StringComparison.Ordinal) => new(200, $$$"""
                    {"items":[{"id":"{{{Repair}}}","role":"Debug","status":"Succeeded","runnerId":"fixture-host","title":"rc failure: Fixture.Failed"}]}
                    """),
                "/api/agent-tasks/" + Repair => new(200, $$$"""
                    {"summary":{"id":"{{{Repair}}}","role":"Debug","status":"Succeeded","runnerId":"fixture-host","title":"rc failure: Fixture.Failed"},
                    "goal":"Repair Fixture.Failed in Windows run {{{RunId}}}.","landing":{"publication":"Landed","destinationRef":"refs/heads/master"}}
                    """),
                "/api/agent-tasks/" + Code => new(200, $$$"""
                    {"summary":{"id":"{{{Code}}}","role":"Code","status":"Blocked","runnerId":"fixture-host","title":"Fixture code","cardIdentifier":"CARD-2087"},
                    "failureReason":"first line\nsecond line","nextStage":"Review","nextHandoff":"review the candidate",
                    "result":"next: invalid","landRequest":{"state":"Queued","requestedAt":"2026-11-26T11:00:00Z"}}
                    """),
                "/api/agent-tasks/" + Merge => new(200, $$$"""
                    {"summary":{"id":"{{{Merge}}}","role":"Merge","status":"Working","runnerId":"fixture-host","title":"Fixture merge"}}
                    """),
                "/api/orchestrator-instructions" => new(200, "## Live instructions\nFixture text."),
                "/api/session-runners/fixture-host/slots" => new(200, """{"occupied":1,"declaredCapacity":3,"slots":[]}"""),
                "/api/attention" => new(200, """{"items":[]}"""),
                _ => new(404, "{}"),
            };
        }

        public void Dispose()
        {
            Release.Set();
            Directory.Delete(_root, recursive: true);
        }
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
