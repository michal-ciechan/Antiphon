using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1313 S4. Durable per-set timing is separate from the checkpoint exit code.
[Category("Unit")]
[NotInParallel]
public sealed class TestSetTimingTests : CheckpointTestBase
{
    [Test]
    public void C1313_WallEnvelopes()
    {
        var launched = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var cases = new List<TrxCaseResult>
        {
            new() { StartedAt = launched.AddSeconds(1), EndedAt = launched.AddSeconds(5), DurationSeconds = 4 },
            new() { StartedAt = launched.AddSeconds(2), EndedAt = launched.AddSeconds(6), DurationSeconds = 4 },
        };
        var measured = TimingMeasurements.Measure(launched, launched.AddSeconds(8), TimeSpan.FromSeconds(8), cases);
        measured.HostWallSeconds.ShouldBe(8);
        measured.CaseWindowSeconds.ShouldBe(5, "wall-not-sum");
        measured.CaseDurationSumSeconds.ShouldBe(8, "wall-not-sum");
        measured.StartupSeconds.ShouldBe(1);
        measured.TeardownSeconds.ShouldBe(2);

        var phases = PhaseTimings.Reduce(launched, launched.AddSeconds(8), TimeSpan.FromSeconds(8), cases);
        phases.TestHostWallSeconds.ShouldBe(8, "wall-not-sum");
        phases.TestsWallSeconds.ShouldBe(5);
        phases.CaseWindowSeconds.ShouldBe(5);
        phases.CaseDurationSumSeconds.ShouldBe(8);
        phases.StartupSeconds.ShouldBe(1);
        phases.TeardownSeconds.ShouldBe(2);
    }

    [Test]
    public void C1313_UnavailableMeasurements()
    {
        var launched = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var missing = TimingMeasurements.Measure(launched, launched.AddSeconds(8), TimeSpan.FromSeconds(8), [new TrxCaseResult()]);
        (missing.HostWallSeconds is null).ShouldBeTrue("unknown-not-zero");
        missing.UnavailableReason.ShouldBe("trx_timestamps_missing");

        var reversed = TimingMeasurements.Measure(launched, launched.AddSeconds(8), TimeSpan.FromSeconds(8),
            [new TrxCaseResult { StartedAt = launched.AddSeconds(5), EndedAt = launched.AddSeconds(1), DurationSeconds = 1 }]);
        (reversed.HostWallSeconds is null).ShouldBeTrue("unknown-not-zero");
        reversed.UnavailableReason.ShouldBe("trx_boundary_inconsistent");

        var outside = TimingMeasurements.Measure(launched, launched.AddSeconds(8), TimeSpan.FromSeconds(8),
            [new TrxCaseResult { StartedAt = launched.AddSeconds(-1), EndedAt = launched.AddSeconds(2), DurationSeconds = 3 }]);
        (outside.HostWallSeconds is null).ShouldBeTrue("unknown-not-zero");

        var discontinuous = TimingMeasurements.Measure(launched, launched.AddSeconds(8), TimeSpan.FromSeconds(1),
            [new TrxCaseResult { StartedAt = launched.AddSeconds(1), EndedAt = launched.AddSeconds(2), DurationSeconds = 1 }]);
        (discontinuous.HostWallSeconds is null).ShouldBeTrue("unknown-not-zero");
        discontinuous.UnavailableReason.ShouldBe("host_clock_discontinuity");
        (discontinuous.HostWallSeconds == 0d).ShouldBeFalse("unknown-not-zero");
    }

    [Test]
    public void C1313_DiscoveryJoin()
    {
        var document = new TimingDiscoveryDocument
        {
            AssemblyHash = "hash-a",
            Cases =
            [
                new() { ExpandedId = "uid-1", DisplayName = "Pretty", Categories = ["Unit"] },
                new() { ExpandedId = "uid-1", DisplayName = "Other", Categories = ["Slow"] },
                new() { ExpandedId = "uid-2", DisplayName = "Pretty", Categories = ["Unit"] },
            ],
        };
        var joined = TimingMeasurements.Join(document, "hash-a");
        joined.Accepted.ShouldBeTrue();
        joined.Cases.Select(item => item.ExpandedId).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            .ShouldBe(new[] { "uid-1", "uid-2" }, "category-uid-bound");
        var refused = TimingMeasurements.Join(document, "hash-b");
        refused.Accepted.ShouldBeFalse();
        refused.Reason.ShouldBe("category-build-bound");
    }

    [Test]
    public async Task C1313_ProducerReceipts()
    {
        var root = Path.Combine(TempDir(), "timing-root");
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT");
        Environment.SetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT", root);
        try
        {
            var result = await RunOwnedChild();
            result.ExitCode.ShouldBe(0);
            result.State.ShouldBe("green");
            var events = Directory.GetFiles(root, "events.jsonl", SearchOption.AllDirectories);
            var receipt = string.Concat(events.Select(File.ReadAllText));
            receipt.Contains("\"eventKind\":\"set-completed\"", StringComparison.Ordinal).ShouldBeTrue("producer-receipt");
            await AssertOneProducer();
            await AssertCompletionAfterJoin();
            await AssertStartBeforeLaunch();
            await AssertBuildFailure();
            await AssertCommandUnavailable();
            await AssertFailedOutcome();
            await AssertTimeoutPreserved();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT", previous);
        }
    }

    private async Task AssertOneProducer()
    {
        var root = FreshRoot();
        foreach (var producer in new[] { "tool", "wrapper" })
        {
            var ran = await Run(PassingChild(), Request("CP-9", root, producer: producer, runId: "shared-run"));
            ran.ExitCode.ShouldBe(0);
        }

        TimingJournal.Read(root).Events.Count(item => item.EventKind == "set-completed").ShouldBe(1, "one-row-producer");
    }

    private async Task AssertCompletionAfterJoin()
    {
        var root = FreshRoot();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, async (request, _) =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return new DriverResult(0, "", "");
        });
        var running = Run(driver, Request("CP-join", root));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            TimingJournal.Read(root).Events.Any(item => item.EventKind == "set-completed").ShouldBeFalse("completion-after-join");
        }
        finally
        {
            release.TrySetResult();
        }

        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        result.ExitCode.ShouldBe(0);
        TimingJournal.Read(root).Events.Any(item => item.EventKind == "set-completed").ShouldBeTrue("completion-after-join");
    }

    private async Task AssertStartBeforeLaunch()
    {
        var root = FreshRoot();
        var journal = TimingJournal.Open(root);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        journal.FlushGate = () => gate.Task;
        journal.FlushEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = PassingChild();
        var running = Run(driver, Request("CP-start", root, journal: journal));
        try
        {
            await journal.FlushEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            driver.Count(CheckpointFixtures.IsBuild).ShouldBe(0, "start-before-launch");
            driver.Count(CheckpointFixtures.IsRun).ShouldBe(0, "start-before-launch");
        }
        finally
        {
            gate.TrySetResult();
        }

        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        result.ExitCode.ShouldBe(0);
    }

    private async Task AssertBuildFailure()
    {
        var root = FreshRoot();
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(1, "build failed", "")));
        var result = await Run(driver, Request("CP-build", root, buildId: "build-a"));
        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        result.State.ShouldBe("build-failed");
        var completed = Completed(root);
        completed.FailurePhase.ShouldBe("build", "failed-attempt-recorded");
        (completed.PassedCount is null).ShouldBeTrue("failed-attempt-recorded");
        (completed.BuildSeconds is not null).ShouldBeTrue("failed-attempt-recorded");
        (completed.WallSeconds is not null).ShouldBeTrue("failed-attempt-recorded");
        completed.ProducingBuildId.ShouldBe("build-a", "failed-attempt-recorded");
    }

    private async Task AssertCommandUnavailable()
    {
        var root = FreshRoot();
        var result = await Run(new FakeDriver(), Request("CP-cmd", root, command: "echo ok"));
        result.ExitCode.ShouldBe(0);
        var completed = Completed(root);
        completed.SetKind.ShouldBe("command");
        completed.Category.ShouldBe("unavailable");
    }

    private async Task AssertFailedOutcome()
    {
        var root = FreshRoot();
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Failed"));
            return Task.FromResult(new DriverResult(1, "", ""));
        });
        var result = await Run(driver, Request("CP-fail", root, buildId: "build-a"));
        result.ExitCode.ShouldBe(ExitCodes.FailedTests);
        var completed = Completed(root);
        completed.Outcome.ShouldBe("failed", "producer-receipt");
        completed.ProducingBuildId.ShouldBe("build-a", "producer-receipt");
        completed.FailedCount.ShouldBe(1);
    }

    private async Task AssertTimeoutPreserved()
    {
        var root = FreshRoot();
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (_, _) => Task.FromResult(new DriverResult(0, "", "", TimedOut: true)));
        var result = await Run(driver, Request("CP-time", root, buildId: "build-a"));
        result.ExitCode.ShouldBe(ExitCodes.Timeout);
        var completed = Completed(root);
        completed.TimedOut.ShouldBeTrue("producer-receipt");
        completed.Outcome.ShouldBe("timeout");
        completed.ProducingBuildId.ShouldBe("build-a");
    }

    private async Task<RowRunResult> RunOwnedChild()
    {
        var driver = PassingChild();
        var workingDirectory = TempDir();
        Directory.CreateDirectory(Path.Combine(workingDirectory, "tests/Antiphon.Tests"));
        return await Run(driver, new RowRequest
        {
            Name = "CP-9",
            Project = "tests/Antiphon.Tests",
            OutputPath = "bin-ex/",
            Filter = "/*/*/Sample/*",
            ResultsDirectory = Path.Combine(TempDir(), "row"),
            WorkingDirectory = workingDirectory,
            MinExecuted = 1,
            Commit = new string('a', 40),
        });
    }

    private string FreshRoot()
    {
        var root = Path.Combine(TempDir(), "timing-root");
        Directory.CreateDirectory(root);
        return root;
    }

    private RowRequest Request(string name, string root, TimingJournal? journal = null, string producer = "tool",
        string? runId = null, string? buildId = null, string? command = null) 
    {
        var workingDirectory = TempDir();
        Directory.CreateDirectory(Path.Combine(workingDirectory, "tests/Antiphon.Tests"));
        return new RowRequest
        {
            Name = name,
            Project = command is null ? "tests/Antiphon.Tests" : null,
            OutputPath = command is null ? "bin-ex/" : null,
            Filter = command is null ? "/*/*/Sample/*" : null,
            Command = command,
            ResultsDirectory = Path.Combine(TempDir(), "row"),
            WorkingDirectory = workingDirectory,
            MinExecuted = 1,
            Commit = new string('a', 40),
            TimingRoot = root,
            TimingJournal = journal,
            TimingProducer = producer,
            TimingRunId = runId ?? name,
            ProducingBuildId = buildId,
        };
    }

    private static FakeDriver PassingChild()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        return driver;
    }

    private Task<RowRunResult> Run(FakeDriver driver, RowRequest request) =>
        new RowRunner(driver, new FakePlatform()).RunAsync(request, TextWriter.Null, CancellationToken.None);

    private static TimingEvent Completed(string root) =>
        TimingJournal.Read(root).Events.Single(item => item.EventKind == "set-completed");
}
