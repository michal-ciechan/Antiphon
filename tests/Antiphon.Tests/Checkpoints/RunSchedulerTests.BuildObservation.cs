using System.Net;
using System.Net.Http.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

public sealed partial class RunSchedulerTests
{
    private const string ObservationBuild = "bin-c2103-observe";
    private const string ObservationFilter = "/*/*/ExampleSurfaceTests/case";
    private static readonly DateTimeOffset ObservationEpoch = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid ObservationLease = Guid.Parse("680dd065-b0f2-4c44-925f-6dda1e331d5e");
    private static readonly SourceObservation ObservationSource = new(new string('a', 40), 0,
        new string('b', 64), ObservationEpoch, "known");

    [Test]
    public async Task C2103_observation_preserves_command_and_receipt_bytes()
    {
        foreach (var grouped in new[] { true, false })
        foreach (var fails in new[] { false, true })
        {
            var root = TempDir();
            var repo = Path.Combine(root, "repo");
            var project = Path.Combine(repo, "sample");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var results = Path.Combine(root, "results");
            var baseline = await RunObservationArmAsync(root, repo, results, grouped, fails, null);
            var positive = await RunObservationArmAsync(root, repo, results, grouped, fails, TimingFault.None);
            var sinkFailure = await RunObservationArmAsync(root, repo, results, grouped, fails, TimingFault.BeforeSpool);
            var label = (grouped ? "grouped" : "standalone") + "-" + (fails ? "red" : "green");
            baseline.Exit.ShouldBe(fails ? 1 : 0, label + "-baseline-exit");
            positive.Exit.ShouldBe(baseline.Exit, label + "-exit");
            sinkFailure.Exit.ShouldBe(baseline.Exit, label + "-exit-with-sink-failure");
            baseline.Commands.Count.ShouldBe(2, label + "-build-and-test-invocation-count");
            baseline.Commands[0].Arguments.ShouldContain("-maxcpucount:6");
            baseline.Commands[1].Arguments.ShouldContain(ObservationFilter);
            baseline.Trx.Failed.ShouldBe(fails ? 1 : 0);
            baseline.Trx.Passed.ShouldBe(fails ? 0 : 1);
            baseline.Trx.Executed.ShouldBe(1);
            foreach (var other in new[] { positive, sinkFailure })
            {
                other.Commands.Count.ShouldBe(baseline.Commands.Count);
                other.Commands[0].Arguments.ShouldContain("-maxcpucount:6", label + "-argv");
                foreach (var (original, observed) in baseline.Commands.Zip(other.Commands))
                {
                    observed.FileName.ShouldBe(original.FileName);
                    observed.Arguments.ShouldBe(original.Arguments, label + "-argv");
                    observed.WorkingDirectory.ShouldBe(original.WorkingDirectory);
                    (observed.Environment ?? new Dictionary<string, string>()).ToArray()
                        .ShouldBe((original.Environment ?? new Dictionary<string, string>()).ToArray());
                    observed.ClearedEnvironmentPrefixes.ShouldBe(original.ClearedEnvironmentPrefixes);
                }
                other.Slots.ShouldBe(baseline.Slots);
                other.Json.ShouldBe(baseline.Json, label + "-json-bytes");
                other.Markdown.ShouldBe(baseline.Markdown, label + "-markdown-bytes");
                other.Line.ShouldBe(baseline.Line, label + "-checkpoint-line");
            }
            positive.Read.Events.Single().BuildPhase!.BuildId.ShouldBe(ObservationBuild);
            positive.Read.Events.Single().BuildPhase!.Invocation.ShouldBe(grouped ? "grouped-producer" : "standalone-composite");
            baseline.Read.CoverageReason.ShouldBe("observation-missing");
            sinkFailure.Read.CoverageReason.ShouldBe("observation-missing");
        }
    }

    private static async Task<ObservationArm> RunObservationArmAsync(string root, string repo, string results,
        bool grouped, bool fails, TimingFault? fault)
    {
        var clock = new ControlledTimeProvider(ObservationEpoch);
        var driver = new FakeDriver();
        var slots = new ObservationSlotTrace();
        var console = new StringWriter();
        var runtime = new CheckpointApp.Runtime
        {
            RepoRoot = repo, Driver = driver, Slots = slots, SourceCapture = _ => ObservationSource,
            EnvironmentLookup = _ => null, TimeProvider = clock, Output = console,
            CleanupContext = new CheckpointCleanupContext(new CheckpointNativeIo(), new CheckpointNativeGitRunner(), clock),
            RowStamp = () => "20261127-000000-abcd", RunStamp = () => "20261127-000000-abcd",
            ReportHost = new ReportHost { Os = "fixture", Cores = 6, MemAvailableMb = 65536, LoadAvg = "fixture" },
            HostSnapshotText = "fixture", GitSnapshotText = "fixture",
            BuildObservationJournal = path =>
            {
                var journal = TimingJournal.Open(path);
                journal.Fault = fault.GetValueOrDefault();
                return journal;
            },
        };
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request),
                ("Antiphon.Tests.ExampleSurfaceTests.case", fails ? "Failed" : "Passed"));
            return Task.FromResult(new DriverResult(fails ? 1 : 0, "", ""));
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string run;
        int exit;
        if (grouped)
        {
            var manifest = new CheckpointManifest
            {
                ResultsRoot = results,
                Builds = [new BuildSpec { Id = ObservationBuild, Project = "sample", OutputPath = ObservationBuild + "/" }],
                Checkpoints = [new CheckpointSpec
                {
                    Id = "CP-1", After = ["S0"], Build = ObservationBuild, Filter = ObservationFilter, MinExecuted = 1,
                }],
            };
            run = CheckpointApp.CreateRun(manifest, new RunRequest
            {
                Commit = ObservationSource.Commit!, Branch = "fixture", KeepOutputs = true, ObserveBuilds = fault.HasValue,
            }, repo, runtime);
            exit = await CheckpointApp.ExecuteAsync(run, deadline.Token, runtime);
        }
        else
        {
            string[] args = ["row", "--name", "CP-1", "--project", "sample", "--output-path", ObservationBuild + "/",
                "--filter", ObservationFilter, "--results-root", results, "--min-executed", "1"];
            if (fault.HasValue)
                args = [.. args, "--observe-builds"];
            exit = await Antiphon.Checkpoints.Program.RunAsync(args, runtime);
            run = Path.Combine(results, "CP-1-20261127-000000-abcd");
        }
        var json = File.ReadAllBytes(Path.Combine(run, "report.json"));
        var markdown = File.ReadAllBytes(Path.Combine(run, "report.md"));
        var report = ReceiptJson.Parse<ReportModel>(System.Text.Encoding.UTF8.GetString(json), ReportWriter.Json);
        var row = report.Rows.Single();
        var trx = TrxReport.Parse(row.Trx!);
        var read = await BuildObservationReplay.ReadAsync(run, deadline.Token);
        var archive = Path.Combine(root, fault?.ToString() ?? "disabled");
        Directory.CreateDirectory(archive);
        Directory.Move(run, Path.Combine(archive, "results"));
        Directory.Move(Path.Combine(repo, "sample", ObservationBuild), Path.Combine(archive, "output"));
        return new(exit, driver.Calls, slots.Events, json, markdown, row.Line!, trx, read);
    }

    private sealed record ObservationArm(int Exit, IReadOnlyList<DriverRequest> Commands, IReadOnlyList<string> Slots,
        byte[] Json, byte[] Markdown, string Line, TrxParseResult Trx, BuildObservationRead Read);

    private sealed class ObservationSlotTrace : IBuildSlotClient
    {
        internal List<string> Events { get; } = [];
        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new SlotSession("enabled", 6));

        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            Events.Add("grant " + label);
            return Task.FromResult(new SlotLease
            {
                State = "granted", MaxCpuCount = session.MaxCpuCount, LeaseId = ObservationLease.ToString(),
                ReleaseAsync = _ =>
                {
                    Events.Add("release " + label);
                    return Task.CompletedTask;
                },
            });
        }
    }

    [Test]
    public async Task C2103_broker_telemetry_gap_is_explicit()
    {
        var (binding, history, request, producer, custody) = await ObserveBrokerAndCustodyAsync();
        var held = BuildObservationReplay.MeasureProducerHold(binding, history);
        held.ShouldBe(new BrokerCoverage("known", null, 5));
        BuildObservationReplay.Classify(producer, request, held, custody).Status.ShouldBe("eligible-ready");

        var unknown = BuildObservationReplay.MeasureProducerHold(binding, null);
        unknown.ShouldBe(new BrokerCoverage("unknown", "broker-telemetry-unreachable", null), "broker-405");
        BuildObservationReplay.Classify(producer, request, unknown, custody).Status.ShouldBe("unknown");
        BuildObservationReplay.MeasureProducerHold(binding, []).Reason.ShouldBe("broker-pair-missing");
    }

    [Test]
    public async Task C2103_custody_telemetry_gap_is_explicit()
    {
        var (binding, history, request, producer, custody) = await ObserveBrokerAndCustodyAsync();
        var held = BuildObservationReplay.MeasureProducerHold(binding, history);
        held.HeldSeconds.ShouldBe(5);
        BuildObservationReplay.Classify(producer, request, held, custody).Status.ShouldBe("eligible-ready");
        BuildObservationReplay.Classify(producer, request, held, null)
            .ShouldBe(new BuildAvailabilityResult("unknown", "custody-record-unreachable"), "custody-access-denied");
        BuildObservationReplay.Classify(producer, request, held, custody with { RemovedAt = request.RequestedAt })
            .ShouldBe(new BuildAvailabilityResult("ineligible", "output-absent"));
    }

    private async Task<(ProducerLeaseBinding Binding, ProducerLeaseTransition[] History, BuildAvailabilityRequest Request,
        ObservedBuild Producer, BuildCustodyEvidence Custody)> ObserveBrokerAndCustodyAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clock = new ControlledTimeProvider(ObservationEpoch);
        var transitions = new LeaseLog();
        await using var fixture = await BuildSlotBrokerFixture.StartAsync(clock: clock, log: transitions);
        var label = "build:" + ObservationBuild;
        using var acquired = await fixture.Http.PostAsJsonAsync("build-slots",
            new BuildSlotRequest(20, clock.GetUtcNow().UtcDateTime.AddSeconds(-1), label, TaskId: "producer"),
            deadline.Token);
        acquired.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lease = (await acquired.Content.ReadFromJsonAsync<BuildSlotGrant>(deadline.Token))!.LeaseId!.Value;
        var grant = fixture.Broker.List().Leases.Single();
        grant.LeaseId.ShouldBe(lease);
        grant.TaskId.ShouldBe("producer");
        clock.Advance(TimeSpan.FromSeconds(5));
        using var released = await fixture.Http.DeleteAsync("build-slots/" + lease, deadline.Token);
        released.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var terminal = transitions.Entries.Single(entry => entry.Id == lease && entry.Held is not null);
        terminal.Held.ShouldBe(5);
        fixture.Broker.List().Occupied.ShouldBe(0);
        using var denied = await fixture.Http.GetAsync("build-slots/" + lease, deadline.Token);
        denied.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);

        var binding = new ProducerLeaseBinding("fixture-broker", fixture.Broker.Metrics().BrokerStartedAtUtc.ToString("O"),
            lease, grant.TaskId!, "fixture-run", ObservationBuild);
        var grantedAt = new DateTimeOffset(grant.GrantedAtUtc);
        var releasedAt = grantedAt.AddSeconds(terminal.Held!.Value);
        var input = new BuildInputDescriptor("antiphon", ObservationSource.Commit!, ObservationSource.Fingerprint!,
            "sample", "requested", "graph", "toolchain", "fixture-runner", "linux", "x64");
        var output = Path.Combine(TempDir(), "fixture-runtime");
        File.WriteAllText(output, "fixture-owned runtime");
        File.Exists(output).ShouldBeTrue();
        var at = releasedAt.AddSeconds(1);
        return (binding, [new(binding, "grant", grantedAt), new(binding, "release", releasedAt)],
            new("consumer", at, input), new("producer", ObservationBuild, input, grantedAt, releasedAt, "ok", "generation", false),
            new(input.Runner, ObservationBuild, "generation", true, releasedAt, at.AddSeconds(2), null,
                releasedAt, at.AddSeconds(2), null));
    }

    private sealed class LeaseLog : ILoggerProvider, ILogger
    {
        public List<(Guid Id, double? Held)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Dispose() { }

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> fields)
                return;
            if (fields.FirstOrDefault(field => field.Key == "LeaseId").Value is Guid id)
                Entries.Add((id, fields.FirstOrDefault(field => field.Key == "HeldSeconds").Value as double?));
        }
    }

    [Test]
    public async Task C2103_observation_reaches_the_reader_after_handoff()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clock = new ControlledTimeProvider(ObservationEpoch);
        foreach (var fault in new[] { TimingFault.AfterSpoolBeforeAppend, TimingFault.AfterFlushBeforeAck,
                     TimingFault.None, TimingFault.BeforeSpool })
        {
            var root = TempDir();
            var writer = new CheckpointBuildObservation(root, "run", null, null, clock);
            var attempted = writer.Begin("grouped-producer", "build-1", "sample", ObservationSource);
            writer.Complete(attempted, "ok", "verified");
            var diagnostics = new List<string>();
            await writer.PublishAsync(path =>
            {
                var journal = TimingJournal.Open(path);
                journal.Fault = fault;
                return journal;
            }, diagnostics.Add);
            var journalRoot = CheckpointBuildObservation.JournalRoot(root);
            var spooled = Directory.Exists(Path.Combine(journalRoot, "spool"))
                ? Directory.GetFiles(Path.Combine(journalRoot, "spool"), "*.json")
                : [];
            var before = TimingJournal.Read(journalRoot);
            if (fault == TimingFault.BeforeSpool)
            {
                diagnostics.Single().ShouldContain("coverage=unknown");
                (await BuildObservationReplay.ReadAsync(root, deadline.Token)).CoverageReason.ShouldBe("observation-missing");
                continue;
            }
            if (fault == TimingFault.AfterSpoolBeforeAppend)
                before.Events.Count.ShouldBe(0, "after-spool-before-append");
            var eventId = spooled.Length == 1 ? Path.GetFileNameWithoutExtension(spooled[0]) : before.Events.Single().EventId;
            var reopened = await BuildObservationReplay.ReadAsync(root, deadline.Token);
            var recorded = reopened.Events.Single();
            recorded.EventId.ShouldBe(eventId, "after-spool-before-append");
            recorded.BuildPhase!.BuildId.ShouldBe("build-1");
            reopened.CoverageReason.ShouldBeNull();
            (await BuildObservationReplay.ReadAsync(root, deadline.Token)).Events.Count.ShouldBe(1);
            if (fault == TimingFault.AfterFlushBeforeAck)
            {
                var digest = recorded.PayloadIdentity;
                recorded.BuildPhase = recorded.BuildPhase with { RequestedPropertiesDigest = "different" };
                var conflict = await Should.ThrowAsync<TimingIntegrityException>(
                    () => TimingJournal.Open(journalRoot).AppendAsync(recorded, deadline.Token));
                conflict.Message.ShouldBe("event-conflict", "conflicting-replay");
                TimingJournal.Read(journalRoot).Events.Single().PayloadIdentity.ShouldBe(digest);
            }
        }

        var quotaRoot = TempDir();
        var capped = new CheckpointBuildObservation(quotaRoot, "quota", null, null, clock, eventLimit: 3);
        foreach (var build in new[] { "first", "second", "overflow" })
            capped.Complete(capped.Begin("grouped-producer", build, "sample", ObservationSource), "ok", "verified");
        await capped.PublishAsync(null, TextWriter.Null.WriteLine);
        var retained = await BuildObservationReplay.ReadAsync(quotaRoot, deadline.Token);
        retained.Events.Count.ShouldBe(2, "full-observation-shard");
        retained.Events.Select(item => item.BuildPhase!.BuildId).ShouldBe(["first", "second"]);
        retained.CoverageReason.ShouldBe("observation-truncated");
    }
}
