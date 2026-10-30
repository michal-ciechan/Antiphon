using System.Net;
using System.Reflection;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointAdmissionContractTests : CheckpointTestBase
{
    [Test]
    public async Task C1616_admission_timeout_publishes_readable_nonexecuted_receipt()
    {
        await using var broker = await BuildSlotBrokerFixture.StartAsync(budget: 1);
        const string endpoint = "http://slots.test/build-slots";
        const string start = "2026-10-30T00:00:00Z";
        var holder = new BuildSlotClient(broker.Handler, endpoint, pid: 100, processStartUtc: start);
        var session = await holder.ProbeAsync(CancellationToken.None);
        await using var held = await holder.AcquireAsync(session, "held-control", CancellationToken.None);
        held.State.ShouldBe("granted");
        var now = DateTimeOffset.UtcNow;
        var queued = new BuildSlotClient(broker.Recording(), endpoint, pid: 101, processStartUtc: start,
            wait: TimeSpan.FromSeconds(1), clock: () => now,
            delay: (_, _) => { now += TimeSpan.FromSeconds(1); return Task.CompletedTask; });
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "tests", "Proof"));
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1616", Project = "tests/Proof", OutputPath = "bin-c1616/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-c1616", Filter = "/*/*/Proof/*", After = ["S1"] }],
        };
        var driver = new FakeDriver();
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null,
            SourceCapture = _ => CheckpointFixtures.CleanSource(), Slots = queued, Driver = driver, Output = TextWriter.Null };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest(), root, runtime);
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(4);
        driver.Starts.ShouldBeEmpty("admission timeout must precede every build and test driver");
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        state.Phase.ShouldBe("done");
        state.CreatedOutputPaths.ShouldBeEmpty();
        ReportWriter.RequiredReceiptRefusal(run, state).ShouldBeNull("terminal admission receipt must pass the production reader");
        var report = ReceiptJson.Read<ReportModel>(Path.Combine(run, "report.json"), ReportWriter.Json);
        report.ExitCode.ShouldBe(4);
        report.Verdict.ShouldBe("RED");
        report.OutputNames.ShouldBeEmpty();
        report.OutputPaths.ShouldBeEmpty();
        var row = report.Rows.Single();
        row.State.ShouldBe("slot-timeout");
        row.Executed.ShouldBe(0);
        row.ExitCode.ShouldBe(4);
        row.SlotReason.ShouldBe("build_slot_busy");
        row.TerminalReason.ShouldBe("slot-admission-deadline-exceeded");
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([report]), "unexecuted evidence cannot qualify");
        broker.Broker.List().Occupied.ShouldBe(1, "the held control must still own its slot");
    }

    [Test]
    public async Task C1531_inputs_resolve_without_disabling_bounds()
    {
        var root = TempDir();
        var manifestPath = Path.Combine(root, "manifest.yaml");
        File.WriteAllText(manifestPath, """
            schemaVersion: 1
            build:
              slotWaitMinutes: 2
            timeouts:
              hardWallMinutes: 25
            checkpoints:
              - id: CP-1
                after: [S1]
                command: "echo proof"
            """);
        foreach (var name in new[] { "total-timeout", "hard-wall-timeout" })
        foreach (var invalid in new[] { "", " ", "0", "-1m", "banana", "999999999999999999999999999999999999999d" })
            (await Antiphon.Checkpoints.Program.MainAsync(["start", manifestPath, "--" + name, invalid],
                runtimeFactory: _ => new CheckpointApp.Runtime { RepoRoot = root, EnvironmentLookup = _ => null }))
                .ShouldBe(2, name + " must reject " + invalid);
        var options = new Antiphon.Checkpoints.Program.ArgSet();
        options.Positionals.Add(manifestPath);
        options.Add("total-timeout", "10m");
        options.Add("hard-wall-timeout", "30m");
        var selection = ((CheckpointManifest Manifest, RunRequest Request))typeof(Antiphon.Checkpoints.Program)
            .GetMethod("LoadSelection", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [options, root, null, CancellationToken.None])!;
        selection.Request.TotalTimeoutMinutes.ShouldBe(10);
        selection.Request.HardWallTimeoutMinutes.ShouldBe(30);
        selection.Manifest.Timeouts.HardWallMinutes.ShouldBe(25);
        selection.Manifest.Build.SlotWaitMinutes.ShouldBe(2);
        var roundTrip = JsonSerializer.Deserialize<RunRequest>(JsonSerializer.Serialize(selection.Request))!;
        roundTrip.HardWallTimeoutMinutes.ShouldBe(30);
        foreach (var scalar in new[] { "null", "", "\"\"", "0", "-1", "bad", "9999999999999999999" })
            Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml("timeouts:\n  hardWallMinutes: " + scalar, root, validate: false));
        ManifestLoader.LoadYaml("timeouts: {}", root, validate: false).Timeouts.HardWallMinutes.ShouldBeNull();

        var clock = new ControlledTimeProvider();
        var handler = new HeldHttp();
        var client = new BuildSlotClient(handler, "http://slots.test/build-slots",
            wait: TimeSpan.FromMinutes(selection.Manifest.Build.SlotWaitMinutes), timeProvider: clock,
            pid: 123, processStartUtc: "2026-01-01T00:00:00Z");
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var acquire = client.AcquireAsync(new SlotSession("enabled", 1), "proof", watchdog.Token);
        try
        {
            await handler.Entered.Task.WaitAsync(watchdog.Token);
            clock.Advance(TimeSpan.FromMinutes(2));
            await using var lease = await acquire;
            lease.ExitCode.ShouldBe(4);
            lease.WaitedSeconds.ShouldBe(120);
            lease.SlotReason.ShouldBe("slot-admission-deadline-exceeded");
            handler.Exited.ShouldBeTrue();
        }
        finally { watchdog.Cancel(); await acquire; }

        var appClock = new ControlledTimeProvider();
        var appSlots = new HeldSlots();
        var appDriver = new FakeDriver();
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null,
            SourceCapture = _ => CheckpointFixtures.CleanSource(), TimeProvider = appClock,
            Slots = appSlots, Driver = appDriver, Output = TextWriter.Null };
        var appRun = CheckpointApp.CreateRun(selection.Manifest, roundTrip, root, runtime);
        using var appStop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = CheckpointApp.ExecuteAsync(appRun, appStop.Token, runtime);
        try
        {
            await appSlots.Entered.Task.WaitAsync(appStop.Token);
            await CheckpointFixtures.WaitUntil(() => appClock.Events.LastOrDefault(e => e.Action == "change")?.DueTime == Timeout.InfiniteTimeSpan);
            appClock.Advance(TimeSpan.FromMinutes(2));
            (await execution).ShouldBe(4);
            var state = new RunStateStore().TryRead(Path.Combine(appRun, "state.json"))!;
            state.Budget!.HardWallLimitSeconds.ShouldBe(1800, "the serialized CLI wall override must reach Execute");
            state.Budget.ExecutionLimitSeconds.ShouldBe(600);
            state.Budget.AdmissionLimitSeconds.ShouldBe(120);
            state.Rows.Single().WaitedSeconds.ShouldBe(120);
            appDriver.Starts.ShouldBeEmpty();
        }
        finally { appStop.Cancel(); await execution; }
    }

    [Test]
    public async Task C1531_receipts_keep_wait_execution_and_counts()
    {
        var root = TempDir();
        var clock = new ControlledTimeProvider();
        var slots = new HeldSlots();
        var driver = new FakeDriver();
        var manifest = new CheckpointManifest
        {
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Command = "echo proof", After = ["S1"] }],
        };
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null, SourceCapture = _ => CheckpointFixtures.CleanSource(),
            Driver = driver, Slots = slots, TimeProvider = clock, Output = TextWriter.Null };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest { TotalTimeoutMinutes = 10, HardWallTimeoutMinutes = 30, KeepOutputs = true }, root, runtime);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execute = CheckpointApp.ExecuteAsync(run, cancel.Token, runtime);
        try
        {
            await slots.Entered.Task.WaitAsync(cancel.Token);
            await CheckpointFixtures.WaitUntil(() => clock.Events.LastOrDefault(e => e.Action == "change")?.DueTime == Timeout.InfiniteTimeSpan);
            clock.Advance(TimeSpan.FromMinutes(3));
            cancel.Cancel();
            (await execute).ShouldBe(130);
            var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
            report.Rows.Single().WaitedSeconds.ShouldBe(180);
            report.Rows.Single().ExecutedSeconds.ShouldBe(0);
            report.Budget!.AdmissionOnlySeconds.ShouldBe(180);
            report.ExitCode.ShouldBe(130);
            File.ReadAllText(Path.Combine(run, "report.md")).ShouldContain("CHECKPOINT-TIMING CP-1 waited=180s executed=0.000s wall=180.000s");
            driver.Starts.ShouldBeEmpty();
        }
        finally { cancel.Cancel(); await execute; }

        // Actual TRX failure/count projection is separate from elapsed seconds.
        var builtRoot = TempDir();
        Directory.CreateDirectory(Path.Combine(builtRoot, "tests", "Proof"));
        var builtManifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-proof", Project = "tests/Proof", OutputPath = "bin-proof/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-proof", Filter = "/*/*/Proof/*", After = ["S1"] }],
        };
        var failed = new FakeDriver();
        failed.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("N.Proof.assertion", "Failed"), ("N.Proof.control", "Passed"));
            return Task.FromResult(new DriverResult(1, "", ""));
        });
        var failedRuntime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null, SourceCapture = _ => CheckpointFixtures.CleanSource(),
            Driver = failed, Slots = new FixedSlotClient("off"), Output = TextWriter.Null };
        var failedRun = CheckpointApp.CreateRun(builtManifest, new RunRequest { KeepOutputs = true }, builtRoot, failedRuntime);
        (await CheckpointApp.ExecuteAsync(failedRun, CancellationToken.None, failedRuntime)).ShouldBe(1);
        var failure = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(failedRun, "report.json")), ReportWriter.Json)!;
        failure.Rows.Single().Executed.ShouldBe(2);
        failure.Rows.Single().Failed.ShouldBe(1);
        failure.Rows.Single().Line.ShouldContain("executed=2 passed=1 failed=1");
        failure.Rows.Single().ExecutedSeconds.ShouldNotBeNull();
    }

    [Test]
    public void C1531_legacy_receipts_remain_unknown_not_zero()
    {
        foreach (var schema in new[] { 2, 3 })
        {
            var legacy = JsonSerializer.Deserialize<ReportModel>($"{{\"schemaVersion\":{schema},\"exitCode\":5,\"cleanupRefusals\":[\"still-present\"],\"rows\":[{{\"id\":\"CP-1\",\"exitCode\":5,\"waitedSeconds\":180}}]}}", ReportWriter.Json)!;
            legacy.Rows.Single().ExecutedSeconds.ShouldBeNull();
            legacy.Rows.Single().WallSeconds.ShouldBeNull();
            legacy.Budget.ShouldBeNull();
            var restored = JsonSerializer.Deserialize<ReportModel>(ReportWriter.JsonText(legacy), ReportWriter.Json)!;
            restored.Rows.Single().ExecutedSeconds.ShouldBeNull();
            restored.Rows.Single().WaitedSeconds.ShouldBe(180);
            restored.ExitCode.ShouldBe(5);
            ReportValidator.Validate(restored, new string('a', 40)).ShouldBe("cleanup_refused");
        }
        var now = DateTimeOffset.UtcNow;
        ReportModel Attempt(int exit, double execution)
        {
            var source = new SourceEvidence { Start = CheckpointFixtures.CleanSource(), End = CheckpointFixtures.CleanSource(), State = "clean", BuildSource = "verified" };
            return new ReportModel { Commit = new string('a', 40), Source = source, EndedAt = now.AddMinutes(execution), ExitCode = exit,
                Rows = [new ReportRow { Id = "CP-1", Source = source, ExitCode = exit, ExecutedSeconds = execution, WallSeconds = execution + 180, WaitedSeconds = 180 }] };
        }
        var merged = ReportMerger.Merge([Attempt(5, 1), Attempt(0, 2)]);
        merged.Attempts.Count.ShouldBe(2);
        merged.Attempts[0].ExitCode.ShouldBe(5);
        merged.Attempts[0].Rows.Single().ExecutedSeconds.ShouldBe(1);
        merged.Rows.Single().ExecutedSeconds.ShouldBe(2);
        foreach (var invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            var malformed = Attempt(0, 2);
            malformed.Budget = new RunBudgetEvidence { AdmissionLimitSeconds = invalid };
            ReportValidator.Validate(malformed, new string('a', 40)).ShouldBe("invalid_timing");
        }
    }

    private sealed class HeldHttp : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Exited { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new HttpResponseMessage(HttpStatusCode.OK); }
            finally { Exited = true; }
        }
    }
    private sealed class HeldSlots : IBuildSlotClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SlotSession> ProbeAsync(CancellationToken ct) => Task.FromResult(new SlotSession("enabled", 1));
        public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new SlotLease();
        }
    }
}
