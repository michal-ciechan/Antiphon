using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSlotExecutorTests : CheckpointTestBase
{
    private static readonly Guid TaskId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const string Token = "C833-SYNTHETIC-OWNER";

    [Test]
    public async Task executor_reachable_broker_grants_build_and_rows()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync(budget: 2);
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording();
        var driver = new FakeDriver();
        var entered = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedOccupancy = 0;
        var siblingSurvivedFirstRelease = false;
        var holderStarts = new System.Collections.Concurrent.ConcurrentDictionary<int, DateTime>();
        driver.When(CheckpointFixtures.IsRun, async (request, _) =>
        {
            observedOccupancy = Math.Max(observedOccupancy, host.Broker.List().Occupied);
            var index = Interlocked.Increment(ref entered);
            if (index == 2) both.TrySetResult();
            await Task.WhenAny(both.Task, Task.Delay(3000));
            if (index == 2)
            {
                for (var attempt = 0; attempt < 100 && !siblingSurvivedFirstRelease; attempt++)
                {
                    siblingSurvivedFirstRelease = host.Broker.List().Occupied == 1;
                    if (!siblingSurvivedFirstRelease) await Task.Delay(10);
                }
            }
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("ExampleSurfaceTests.case", "Passed"));
            return new DriverResult(0, "", "");
        });
        var manifest = BuiltManifest(twoRows: true);
        var run = NewRun(manifest);
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(recorder, driver,
            startTimeReader: process =>
            {
                var started = process.StartTime.ToUniversalTime();
                holderStarts[process.Id] = started;
                return started;
            }))).ShouldBe(0);
        entered.ShouldBe(2, "actual driver roster: both row drivers must run");
        observedOccupancy.ShouldBe(2, "simultaneous-holder-roster: rows must have distinct held leases");
        siblingSurvivedFirstRelease.ShouldBeTrue("simultaneous-holder-roster: sibling survives first release");
        driver.Calls.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        driver.Calls.Single(CheckpointFixtures.IsBuild).Arguments.ShouldContain("-maxcpucount:6");
        var report = ReadReport(run);
        report.Rows.Count.ShouldBe(2);
        report.Rows.ShouldAllBe(row => row.Slot == "granted" && row.Line!.Contains("slot=granted"));
        report.Builds.Single().Slot.ShouldBe("granted");
        host.Broker.List().Occupied.ShouldBe(0);
        var posts = recorder.Calls.Where(call => call.Method == "POST" && call.Path == "/build-slots").ToList();
        posts.Count.ShouldBeGreaterThanOrEqualTo(3);
        holderStarts.Count.ShouldBe(3, "simultaneous-holder-roster: build and rows need distinct children");
        foreach (var post in posts)
        {
            using var body = JsonDocument.Parse(post.Body);
            var pid = body.RootElement.GetProperty("pid").GetInt32();
            holderStarts.TryGetValue(pid, out var expected).ShouldBeTrue("outbound-start-equals-holder: actual child PID");
            body.RootElement.GetProperty("processStartUtc").GetDateTime().ShouldBe(expected,
                TimeSpan.FromMilliseconds(1), "outbound-start-equals-holder: actual child start");
        }
        await using var holder = ProcessLeaseHolder.Start(Environment.ProcessId);
        var first = holder.ProcessStartUtc;
        await Task.Delay(10);
        holder.ProcessStartUtc.ShouldBe(first, "cached-holder-start: repeated reads must be identical");
        using var child = Process.GetProcessById(holder.Pid);
        DateTime.Parse(first!).ShouldBe(child.StartTime.ToUniversalTime(), TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task executor_renews_until_driver_finishes_then_releases()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync(renewEvery: 1);
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording();
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, token) => { await Task.Delay(2400, token); return new DriverResult(0, "", ""); });
        var run = NewRun(CommandManifest());
        Process? child = null;
        try
        {
            (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(recorder, driver,
                startTimeReader: process =>
                {
                    child = Process.GetProcessById(process.Id);
                    return process.StartTime;
                }))).ShouldBe(0);
            var calls = recorder.Calls.ToArray();
            calls.Count(call => call.Path.EndsWith("/renew", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(2);
            calls.Last(call => call.Method == "DELETE").Path.ShouldNotBeNullOrWhiteSpace();
            calls.Last().Method.ShouldBe("DELETE", "renew-before-release: no renew after disposal");
            host.Broker.List().Occupied.ShouldBe(0);
            child.ShouldNotBeNull();
            child.HasExited.ShouldBeTrue("renew-before-release: holder exits after driver completion");
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }

    [Test]
    public async Task executor_rejection_launches_no_build_or_rows_and_reports_reason()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording(omitStart: true);
        var driver = new FakeDriver();
        var run = NewRun(BuiltManifest(twoRows: true));
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(recorder, driver))).ShouldBe(2);
        driver.Calls.ShouldBeEmpty("build-driver-count: refusal must precede all drivers");
        recorder.Calls.Count(call => call.Method == "POST" && call.Path == "/build-slots").ShouldBe(1);
        var log = File.ReadAllText(Path.Combine(run, "executor.log"));
        log.ShouldContain("operation=acquire status=400 reason=build_slot_invalid");
        log.ShouldContain("no processStartUtc was given");
        var report = ReadReport(run);
        report.Rows.Count.ShouldBe(2);
        report.Rows.ShouldAllBe(row => row.State == "slot-refused" && row.ExitCode == 2
            && row.Executed == 0 && row.SlotReason == "build_slot_invalid" && row.WaitedSeconds >= 0);
        var merged = ReportMerger.Merge([report]);
        merged.Rows.ShouldAllBe(row => row.SlotReason == "build_slot_invalid");
        File.ReadAllText(Path.Combine(run, "report.md")).ShouldContain("slot-reason=build_slot_invalid");
        var probe = new ScriptedHttpHandler();
        probe.Enqueue(HttpStatusCode.BadRequest, """{"type":"probe_denied","detail":"no listing"}""");
        var probeRun = NewRun(BuiltManifest(twoRows: false));
        (await CheckpointApp.ExecuteAsync(probeRun, CancellationToken.None, Runtime(probe, new FakeDriver()))).ShouldBe(2);
        var dependent = ReadReport(probeRun).Rows.Single();
        dependent.SlotReason.ShouldBe("probe_denied", "dependent-admission: probe refusal must reach dependent row");
        dependent.Executed.ShouldBe(0);
    }

    [Test]
    public async Task executor_row_rejection_does_not_run_command()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording(omitStart: true);
        var driver = new FakeDriver();
        var run = NewRun(CommandManifest());
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(recorder, driver))).ShouldBe(2);
        driver.Calls.ShouldBeEmpty("refused-row-driver-count: command must not launch");
        ReadReport(run).Rows.Single().SlotReason.ShouldBe("build_slot_invalid");
        var client = new BuildSlotClient(host.Recording(omitStart: true), "http://slots.test/build-slots",
            holders: new ProcessLeaseHolderSource());
        var comparer = new BaselineComparer(driver, client);
        await comparer.CompareAsync(TempDir(), TempDir(), "origin/master",
            [new ReportRow { Id = "CP-1", State = "red", Failures = [new ReportFailure { Name = "X.Y" }] }],
            [], CancellationToken.None);
        driver.Calls.ShouldBeEmpty("baseline-driver-count: refused fetch must stop before git");
        comparer.ToolRuns.ShouldContain(line => line.Contains("slot-reason=build_slot_invalid"));
        var builtRecorder = (BuildSlotBrokerFixture.Recorder)host.Recording(omitStartAfter: 1);
        var builtDriver = new FakeDriver();
        var builtRun = NewRun(BuiltManifest(twoRows: false));
        (await CheckpointApp.ExecuteAsync(builtRun, CancellationToken.None, Runtime(builtRecorder, builtDriver))).ShouldBe(2);
        builtDriver.Calls.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        builtDriver.Calls.Count(CheckpointFixtures.IsRun).ShouldBe(0, "refused-row-driver-count: TUnit row must not launch");
        ReadReport(builtRun).Rows.Single().SlotReason.ShouldBe("build_slot_invalid");
    }

    [Test]
    public async Task executor_cancellation_releases_lease_and_holder()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording();
        var driver = new FakeDriver();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        driver.When(_ => true, async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new DriverResult(0, "", "");
        });
        var run = NewRun(CommandManifest());
        using var cancel = new CancellationTokenSource();
        Process? child = null;
        try
        {
            var execute = CheckpointApp.ExecuteAsync(run, cancel.Token, Runtime(recorder, driver,
                startTimeReader: process =>
                {
                    child = Process.GetProcessById(process.Id);
                    return process.StartTime;
                }));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            host.Broker.List().Occupied.ShouldBe(1);
            cancel.Cancel();
            try { await execute.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            host.Broker.List().Occupied.ShouldBe(0);
            recorder.Calls.Count(call => call.Method == "DELETE").ShouldBe(1, "released-lease: exact DELETE");
            child.ShouldNotBeNull();
            child.HasExited.ShouldBeTrue("owned-holder-exited: child exits before fixture cleanup");
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }

    [Test]
    public async Task executor_missing_owner_token_stops_before_slot_probe()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording();
        var driver = new FakeDriver();
        var run = NewRun(CommandManifest());
        var runtime = Runtime(recorder, driver, missingToken: true);
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(7);
        File.Exists(Path.Combine(run, "host.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(run, "git.txt")).ShouldBeFalse();
        recorder.Calls.ShouldBeEmpty();
        driver.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task row_entrypoint_rejection_invokes_no_driver()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording(omitStart: true);
        var driver = new FakeDriver();
        var runtime = Runtime(recorder, driver);
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await Antiphon.Checkpoints.Program.RunAsync(["row", "--repo-root", TempDir(), "--name", "CP-1",
                "--project", "tests/Antiphon.Tests", "--output-path", "bin-c833/",
                "--filter", "/*/*/ExampleSurfaceTests/*"], runtime);
            exit.ShouldBe(2);
        }
        finally { Console.SetOut(original); }
        driver.Calls.ShouldBeEmpty("direct-driver-count: rejected row must not build or test");
        output.ToString().ShouldContain("slot=refused");
        output.ToString().ShouldContain("slot-reason=build_slot_invalid");
    }

    [Test]
    public async Task failed_start_time_capture_disposes_new_holder()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording();
        Process? child = null;
        DateTime Fail(Process process)
        {
            child = Process.GetProcessById(process.Id);
            throw new InvalidOperationException("synthetic start-time failure");
        }
        var driver = new FakeDriver();
        var run = NewRun(CommandManifest());
        try
        {
            var runtime = Runtime(recorder, driver, startTimeReader: Fail);
            (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(2);
            ReadReport(run).Rows.Single().SlotReason.ShouldBe("holder_identity_unavailable");
            child.ShouldNotBeNull();
            child.HasExited.ShouldBeTrue("failed-capture-child-exited: holder must be reaped before refusal returns");
            recorder.Calls.Count(call => call.Method == "POST").ShouldBe(0);
            driver.Calls.ShouldBeEmpty();
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }

    private static CheckpointManifest CommandManifest() => new()
    {
        Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["all"], Command = "true", EstimatedMinutes = 1 }],
    };

    private static CheckpointManifest BuiltManifest(bool twoRows)
    {
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c833", Project = "tests/Antiphon.Tests", OutputPath = "bin-c833/" }],
        };
        for (var i = 1; i <= (twoRows ? 2 : 1); i++)
            manifest.Checkpoints.Add(new CheckpointSpec
            {
                Id = "CP-" + i, After = ["all"], Build = "bin-c833", Filter = "/*/*/ExampleSurfaceTests/*",
                MinExecuted = 1, EstimatedMinutes = 1,
            });
        return manifest;
    }

    private string NewRun(CheckpointManifest manifest) => CheckpointApp.CreateRun(manifest, new RunRequest
    {
        KeepOutputs = true, Parallel = 2, Commit = new string('a', 40), Branch = "test",
        OwnerTaskId = TaskId.ToString(), OwnerSessionId = SessionId.ToString(),
    }, TempDir());

    private static ReportModel ReadReport(string run) =>
        JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;

    private static CheckpointApp.Runtime Runtime(HttpMessageHandler slots, FakeDriver driver, bool missingToken = false,
        Func<Process, DateTime>? startTimeReader = null) => new()
    {
        EnvironmentLookup = name => name switch
        {
            "ANTIPHON_TASK_ID" => TaskId.ToString(),
            "ANTIPHON_SESSION_ID" => SessionId.ToString(),
            "ANTIPHON_API" => "http://owner.invalid",
            "ANTIPHON_TASK_TOKEN" => missingToken ? null : Token,
            _ => null,
        },
        OwnerHandler = new Owner(), SlotHandler = slots, Driver = driver,
        SlotStartTimeReader = startTimeReader,
    };

    private sealed class Owner : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    summary = new { id = TaskId, agentSessionId = SessionId, status = "Working" },
                    session = new { sessionId = SessionId, status = "Running" },
                }), Encoding.UTF8, "application/json"),
            });
    }
}
